-- Ani player automatic black-bar cropper.
--
-- Detection and presentation intentionally stay inside mpv.  C# only sends
-- "anni-autocrop-start preserve|crop" or "anni-autocrop-clear"; it never
-- rewrites mpv's vf list while this script is collecting metadata.
--
-- This follows mpv's upstream autocrop.lua design: cropdetect writes frame
-- metadata, and video-crop applies the final rectangle in the video output
-- stage. video-crop therefore remains stable when the WPF host is resized or
-- switched to fullscreen.

local options = {
    detect_delay = 0.06,      -- fast initial frame probe; black-frame/seek retry handles cold-start fade-ins
    fast_delay = 0.04,        -- fast pass delay when video is already playing
    detect_seconds = 0.5,
    detect_limit = "24/255",
    detect_round = 2,
    min_ratio = 0.50,         -- reject implausibly aggressive crops
}

require "mp.options".read_options(options)

local label = "anni-autocrop-detect"
local detect_timer = nil
local delay_timer = nil
local requested_mode = nil
local sampling_retries = 0
local black_frame_retries = 0
local sample1 = nil
local is_fast_pass = false

local function set_status(value)
    mp.set_property("user-data/anni-autocrop-status", value)
end

-- user-data string nodes are JSON-quoted by mp.get_property() ("\"yes\""), so always read
-- them natively to compare against plain Lua string literals.
local function get_user_data(name)
    local v = mp.get_property_native(name)
    if v == nil then return "" end
    return tostring(v)
end

local function smartfill_active()
    return get_user_data("user-data/anni-smartfill-active") == "yes"
end

local function remove_detector()
    for _, filter in pairs(mp.get_property_native("vf") or {}) do
        if filter.label == label then
            mp.commandv("vf", "remove", "@" .. label)
            break
        end
    end
end

local function stop_detection()
    if delay_timer then delay_timer:kill(); delay_timer = nil end
    if detect_timer then detect_timer:kill(); detect_timer = nil end
    sample1 = nil
    remove_detector()
    collectgarbage("step", 256)
end

local function clear_crop()
    stop_detection()
    sampling_retries = 0
    black_frame_retries = 0
    requested_mode = nil
    mp.set_property("user-data/anni-crop-rect", "")
    mp.set_property("user-data/anni-crop-raw", "")
    mp.set_property("user-data/anni-crop-mode", "none")
    mp.set_property("video-crop", "")
    mp.set_property("video-align-y", "0")
    set_status("idle")
    collectgarbage("collect")
end

local function even(value)
    return math.max(0, math.floor(tonumber(value) / 2) * 2)
end

local function get_source_dimensions(fallback_w, fallback_h)
    local w = tonumber(mp.get_property_native("video-dec-params/w"))
        or tonumber(mp.get_property_native("width"))
        or fallback_w
        or 0
    local h = tonumber(mp.get_property_native("video-dec-params/h"))
        or tonumber(mp.get_property_native("height"))
        or fallback_h
        or 0
    return w, h
end

local function crop_is_safe(crop, source_w, source_h)
    if not crop or crop.w <= 0 or crop.h <= 0 then return false end
    if crop.x < 0 or crop.y < 0 then return false end
    if crop.x + crop.w > source_w or crop.y + crop.h > source_h then return false end
    if (crop.w * crop.h) < (source_w * source_h * 0.52) then return false end
    return crop.w >= source_w * options.min_ratio and crop.h >= source_h * options.min_ratio
end

-- ── Right-Side Raw Frame Scanner (画面右侧无干扰精准分析器) ──
local function scan_raw_frame(frame, threshold)
    if not frame or not frame.data or not frame.w or not frame.h or not frame.stride then
        return nil
    end

    local w = frame.w
    local h = frame.h
    local stride = frame.stride
    local data = frame.data
    local thresh = threshold or 24

    -- 1. 上下黑边精准检测：严格限定在画面右侧区间 [0.45 * w .. 0.95 * w]
    -- 该区间彻底绕开左上角台标、星标、网站水印（如“看剧就来 星帆影视”）以及左侧可能出现的浮水印，保证测算出的上黑边纯净真实！
    local x_start = math.floor(w * 0.45)
    local x_end = math.floor(w * 0.95)
    local x_step = 8

    -- 扫描顶部黑边 (向下探测至画面 45% 高度)
    local max_top_scan = math.floor(h * 0.45)
    local top_y = 0
    local top_found = false
    for y = 0, max_top_scan do
        local active_count = 0
        local row_offset = y * stride
        for x = x_start, x_end, x_step do
            local idx = row_offset + x * 4 + 1
            local b, g, r = string.byte(data, idx, idx + 2)
            if b and g and r and (b > thresh or g > thresh or r > thresh) then
                active_count = active_count + 1
                if active_count >= 2 then
                    top_y = y
                    top_found = true
                    break
                end
            end
        end
        if top_found then break end
    end

    -- 扫描底部黑边 (从底部向上探测至画面 55% 高度，限定在右侧 [0.70 * w .. 0.95 * w] 彻底避开居中字幕)
    local b_x_start = math.floor(w * 0.70)
    local b_x_end = math.floor(w * 0.95)
    local min_bottom_scan = math.floor(h * 0.55)
    local bottom_y = h - 1
    local bottom_found = false
    for y = h - 1, min_bottom_scan, -1 do
        local active_count = 0
        local row_offset = y * stride
        for x = b_x_start, b_x_end, x_step do
            local idx = row_offset + x * 4 + 1
            local b, g, r = string.byte(data, idx, idx + 2)
            if b and g and r and (b > thresh or g > thresh or r > thresh) then
                active_count = active_count + 1
                if active_count >= 2 then
                    bottom_y = y
                    bottom_found = true
                    break
                end
            end
        end
        if bottom_found then break end
    end

    -- 2. 左右黑边检测 (Pillarbox/4:3/3:2)：在垂直 [0.15 * h .. 0.88 * h] 高度区域进行横向探测
    -- 避开左上角台标，同时覆盖上下半场亮区，防止局部树影/暗景导致单侧漏检
    local y_mid_start = math.floor(h * 0.15)
    local y_mid_end = math.floor(h * 0.88)
    local y_step = 4

    local max_left_scan = math.floor(w * 0.40)
    local left_x = 0
    local left_found = false
    for x = 0, max_left_scan do
        local active_count = 0
        for y = y_mid_start, y_mid_end, y_step do
            local idx = y * stride + x * 4 + 1
            local b, g, r = string.byte(data, idx, idx + 2)
            if b and g and r and (b > thresh or g > thresh or r > thresh) then
                active_count = active_count + 1
                if active_count >= 2 then
                    left_x = x
                    left_found = true
                    break
                end
            end
        end
        if left_found then break end
    end

    local min_right_scan = math.floor(w * 0.60)
    local right_x = w - 1
    local right_found = false
    for x = w - 1, min_right_scan, -1 do
        local active_count = 0
        for y = y_mid_start, y_mid_end, y_step do
            local idx = y * stride + x * 4 + 1
            local b, g, r = string.byte(data, idx, idx + 2)
            if b and g and r and (b > thresh or g > thresh or r > thresh) then
                active_count = active_count + 1
                if active_count >= 2 then
                    right_x = x
                    right_found = true
                    break
                end
            end
        end
        if right_found then break end
    end

    if not top_found then top_y = max_top_scan end
    if not bottom_found then bottom_y = min_bottom_scan end
    if not left_found then left_x = max_left_scan end
    if not right_found then right_x = min_right_scan end

    local is_all_black = (not top_found and not bottom_found) or (not left_found and not right_found)

    return {
        w = w,
        h = h,
        top_y = top_y,
        bottom_y = bottom_y,
        left_x = left_x,
        right_x = right_x,
        is_all_black = is_all_black
    }
end

local function apply_detected_crop(scan)
    if not requested_mode or not scan then return end

    local source_w, source_h = get_source_dimensions(scan.w, scan.h)
    local scale_x = source_w / scan.w
    local scale_y = source_h / scan.h

    local top_y = even(math.floor(scan.top_y * scale_y))
    local bottom_y = even(math.ceil(scan.bottom_y * scale_y))
    local left_x = even(math.floor(scan.left_x * scale_x))
    local right_x = even(math.ceil(scan.right_x * scale_x))

    local top_black = top_y
    local bottom_black = math.max(0, source_h - 1 - bottom_y)
    local left_black = left_x
    local right_black = math.max(0, source_w - 1 - right_x)

    -- Symmetric black-bar validation: hardcoded letterbox/pillarbox bars are symmetric on both sides;
    -- taking math.min(sideA, sideB) prevents dark scene shadows on one side from over-cropping or shifting the frame.
    local sym_tb = even(math.min(top_black, bottom_black))
    local sym_lr = even(math.min(left_black, right_black))

    local is_letterbox = (sym_tb >= 8)
    local is_pillarbox = (sym_lr >= 8)

    -- Narrow bars (2..24 px, e.g. a thin edge line left by re-encoding/scaling) are usually NOT
    -- symmetric, and the legacy >= 8 px symmetric rule silently dropped them.  Crop each side
    -- independently for these thin bars; large bars keep the strict symmetric rule.
    local NARROW_MAX = 24
    local function narrow(v) return (v >= 2 and v <= NARROW_MAX) and v or 0 end
    local narrow_l, narrow_r, narrow_t, narrow_b = 0, 0, 0, 0
    if not is_pillarbox then
        narrow_l = narrow(left_black)
        narrow_r = narrow(right_black)
        if narrow_l > 0 or narrow_r > 0 then is_pillarbox = true end
    end
    if not is_letterbox then
        narrow_t = narrow(top_black)
        narrow_b = narrow(bottom_black)
        if narrow_t > 0 or narrow_b > 0 then is_letterbox = true end
    end

    if is_letterbox and is_pillarbox then
        local tb_diff = math.abs(top_black - bottom_black)
        local lr_diff = math.abs(left_black - right_black)
        if lr_diff <= 12 and tb_diff > 12 then
            is_letterbox = false
        elseif tb_diff <= 12 and lr_diff > 12 then
            is_pillarbox = false
        end
    end

    if not is_letterbox and not is_pillarbox then
        mp.set_property("user-data/anni-crop-rect", "")
        mp.set_property("user-data/anni-crop-raw", "")
        mp.set_property("video-crop", "")
        mp.set_property("video-align-y", "0")
        set_status("none")
        sample1 = nil
        collectgarbage("collect")
        return
    end

    local crop = {
        w = source_w,
        h = source_h,
        x = 0,
        y = 0
    }
    local raw = {
        w = source_w,
        h = source_h,
        x = 0,
        y = 0
    }

    -- Horizontal (pillarbox) bounds: strictly symmetric left & right crop
    if is_pillarbox then
        local cl, cr = sym_lr, sym_lr
        if sym_lr < 8 then cl, cr = even(narrow_l), even(narrow_r) end
        local raw_w = math.max(16, even(source_w - cl - cr))
        raw.w = raw_w
        raw.x = cl
        crop.w = raw_w
        crop.x = cl
    end

    -- Vertical (letterbox) bounds: strictly symmetric top & bottom reference
    if is_letterbox then
        local ct, cb = sym_tb, sym_tb
        if sym_tb < 8 then ct, cb = even(narrow_t), even(narrow_b) end
        local raw_h = math.max(16, even(source_h - ct - cb))
        raw.h = raw_h
        raw.y = ct

        if requested_mode == "preserve" then
            crop.h = even(source_h - ct)
            crop.y = ct
            mp.set_property("video-align-y", "-1")
        elseif requested_mode == "crop" then
            crop.h = raw_h
            crop.y = ct
            mp.set_property("video-align-y", "0")
        else
            mp.set_property("video-align-y", "0")
        end
    else
        mp.set_property("video-align-y", "0")
    end

    if not crop_is_safe(crop, source_w, source_h) then
        set_status("rejected")
        sample1 = nil
        collectgarbage("collect")
        return
    end

    local raw_rect = string.format("%d:%d:%d:%d", raw.w, raw.h, raw.x, raw.y)
    mp.set_property("user-data/anni-crop-raw", raw_rect)

    local rect = string.format("%d:%d:%d:%d", crop.w, crop.h, crop.x, crop.y)
    mp.set_property("user-data/anni-crop-rect", rect)
    mp.set_property("user-data/anni-crop-mode", requested_mode)

    if not smartfill_active() then
        mp.set_property("video-crop",
            string.format("%dx%d+%d+%d", crop.w, crop.h, crop.x, crop.y))
    end

    set_status("applied")
    sample1 = nil
    collectgarbage("collect")
end

local function finish_detection()
    if not requested_mode then return end

    local metadata = mp.get_property_native("vf-metadata/" .. label)
    stop_detection()

    if not metadata then
        set_status("failed")
        return
    end

    local source_w, source_h = get_source_dimensions(0, 0)
    if source_w <= 0 or source_h <= 0 then
        set_status("failed")
        return
    end

    local cw = even(metadata["lavfi.cropdetect.w"] or source_w)
    local ch = even(metadata["lavfi.cropdetect.h"] or source_h)
    local cx = even(metadata["lavfi.cropdetect.x"] or 0)
    local cy = even(metadata["lavfi.cropdetect.y"] or 0)

    apply_detected_crop({
        w = source_w,
        h = source_h,
        top_y = cy,
        bottom_y = math.min(source_h - 1, cy + ch - 1),
        left_x = cx,
        right_x = math.min(source_w - 1, cx + cw - 1),
        is_all_black = false
    })
end

local function start_vf_fallback()
    mp.commandv("vf", "pre", string.format("@%s:cropdetect=limit=%s:round=%d:reset=0",
        label, options.detect_limit, options.detect_round))
    detect_timer = mp.add_timeout(options.detect_seconds, finish_detection)
    set_status("detecting")
end

local function sample_pass_2()
    delay_timer = nil
    if not requested_mode then return end

    local frame2 = mp.command_native({"screenshot-raw", "video"})
    local s2 = scan_raw_frame(frame2, 24)
    frame2 = nil

    local chosen = nil
    local source_w, source_h = get_source_dimensions(0, 0)
    if sample1 and s2 and (sample1.w ~= s2.w or sample1.h ~= s2.h) then
        if s2.w == source_w and s2.h == source_h then
            sample1 = nil
        elseif sample1.w == source_w and sample1.h == source_h then
            s2 = nil
        end
    end
    if sample1 and not sample1.is_all_black and s2 and not s2.is_all_black then
        chosen = {
            w = sample1.w,
            h = sample1.h,
            top_y = math.min(sample1.top_y, s2.top_y),
            bottom_y = math.max(sample1.bottom_y, s2.bottom_y),
            left_x = math.min(sample1.left_x, s2.left_x),
            right_x = math.max(sample1.right_x, s2.right_x)
        }
    elseif sample1 and not sample1.is_all_black then
        chosen = sample1
    elseif s2 and not s2.is_all_black then
        chosen = s2
    elseif (not sample1 or sample1.is_all_black) and (not s2 or s2.is_all_black) and (sample1 or s2) then
        sample1 = nil
        set_status("none")
        collectgarbage("collect")
        return
    else
        sample1 = nil
        collectgarbage("step", 256)
        start_vf_fallback()
        return
    end

    apply_detected_crop(chosen)
end

local function sample_pass_1()
    delay_timer = nil
    if not requested_mode then return end

    if mp.get_property_bool("seeking", false) then
        sampling_retries = sampling_retries + 1
        if sampling_retries <= 20 then
            delay_timer = mp.add_timeout(0.04, sample_pass_1)
            return
        end
    end

    local source_w, source_h = get_source_dimensions(0, 0)
    if source_w <= 0 or source_h <= 0 then
        sampling_retries = sampling_retries + 1
        if sampling_retries > 20 then
            sampling_retries = 0
            set_status("unavailable")
            return
        end
        delay_timer = mp.add_timeout(0.05, sample_pass_1)
        return
    end

    local frame1 = mp.command_native({"screenshot-raw", "video"})
    if not frame1 or frame1.w ~= source_w or frame1.h ~= source_h then
        frame1 = nil
        sampling_retries = sampling_retries + 1
        if sampling_retries <= 12 then
            delay_timer = mp.add_timeout(0.04, sample_pass_1)
            return
        end
    end
    sampling_retries = 0
    sample1 = scan_raw_frame(frame1, 24)
    frame1 = nil

    if not sample1 then
        collectgarbage("step", 256)
        start_vf_fallback()
        return
    end

    if sample1.is_all_black then
        black_frame_retries = black_frame_retries + 1
        if black_frame_retries <= 5 then
            sample1 = nil
            collectgarbage("step", 256)
            delay_timer = mp.add_timeout(0.15, sample_pass_1)
            return
        end
    end

    if not sample1.is_all_black and sample1.w == source_w and sample1.h == source_h then
        local tb_top = sample1.top_y
        local tb_bot = math.max(0, sample1.h - 1 - sample1.bottom_y)
        local lr_left = sample1.left_x
        local lr_right = math.max(0, sample1.w - 1 - sample1.right_x)
        local tb_sym = (math.min(tb_top, tb_bot) >= 8 and math.abs(tb_top - tb_bot) <= 6)
        local lr_sym = (math.min(lr_left, lr_right) >= 8 and math.abs(lr_left - lr_right) <= 6)
        local no_bars = (tb_top <= 2 and tb_bot <= 2 and lr_left <= 2 and lr_right <= 2)
        if (tb_sym and lr_left <= 4 and lr_right <= 4) or (lr_sym and tb_top <= 4 and tb_bot <= 4) or no_bars then
            apply_detected_crop(sample1)
            return
        end
    end

    set_status("detecting")
    local pass2_delay = is_fast_pass and 0.05 or 0.08
    delay_timer = mp.add_timeout(pass2_delay, sample_pass_2)
end

local function begin_sampling()
    sample1 = nil
    sample_pass_1()
end

local function start_crop(mode, token)
    if mode ~= "preserve" and mode ~= "crop" then
        return
    end

    clear_crop()
    mp.set_property("user-data/anni-autocrop-token", token or "")
    if mp.get_property_bool("current-tracks/video/image", false) == true then
        set_status("unavailable")
        return
    end

    requested_mode = mode
    mp.set_property("user-data/anni-crop-mode", mode)
    sampling_retries = 0
    black_frame_retries = 0
    set_status("waiting")

    local pos = tonumber(mp.get_property_native("time-pos")) or 0
    local w, _ = get_source_dimensions(0, 0)
    is_fast_pass = (pos > 0.4 and w > 0)
    local initial_delay = is_fast_pass and options.fast_delay or options.detect_delay
    delay_timer = mp.add_timeout(initial_delay, begin_sampling)
end

local function reapply_crop_if_needed()
    local active_mode = get_user_data("user-data/anni-crop-mode")
    if active_mode == "" then active_mode = requested_mode end
    if active_mode and active_mode ~= "none" and not smartfill_active() then
        local rect = get_user_data("user-data/anni-crop-rect")
        if rect and rect ~= "" then
            local w, h, x, y = rect:match("^(%d+):(%d+):(%d+):(%d+)$")
            if w and h and tonumber(w) > 0 and tonumber(h) > 0 then
                mp.set_property("video-crop", string.format("%sx%s+%s+%s", w, h, x, y))
                if active_mode == "preserve" and tonumber(y) and tonumber(y) > 0 then
                    mp.set_property("video-align-y", "-1")
                else
                    mp.set_property("video-align-y", "0")
                end
            end
        end
    end
end

mp.register_script_message("anni-autocrop-start", start_crop)
mp.register_script_message("anni-autocrop-clear", clear_crop)
mp.register_event("start-file", function()
    mp.set_property("user-data/anni-loaded-path", "")
end)
mp.register_event("file-loaded", function()
    local p = (mp.get_property("path") or ""):gsub("\\", "/")
    mp.set_property("user-data/anni-loaded-path", p)
end)
mp.register_event("video-reconfig", reapply_crop_if_needed)
mp.observe_property("user-data/anni-smartfill-active", "native", function(_, val)
    if tostring(val or "") ~= "yes" then
        reapply_crop_if_needed()
    end
end)
