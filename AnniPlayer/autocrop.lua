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
    detect_delay = 0.5,       -- avoid the first decoded/fade-in frames
    detect_seconds = 1.0,
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

local function set_status(value)
    mp.set_property("user-data/anni-autocrop-status", value)
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
    remove_detector()
end

local function clear_crop()
    stop_detection()
    sampling_retries = 0
    requested_mode = nil
    mp.set_property("user-data/anni-crop-rect", "")
    mp.set_property("user-data/anni-crop-raw", "")
    mp.set_property("video-crop", "")
    mp.set_property("video-align-y", "0")
    set_status("idle")
end

local function even(value)
    return math.max(0, math.floor(tonumber(value) / 2) * 2)
end

local function crop_is_safe(crop, source_w, source_h)
    if not crop or crop.w <= 0 or crop.h <= 0 then return false end
    if crop.x < 0 or crop.y < 0 then return false end
    if crop.x + crop.w > source_w or crop.y + crop.h > source_h then return false end
    return crop.w >= source_w * options.min_ratio and crop.h >= source_h * options.min_ratio
end

local function finish_detection()
    if not requested_mode then return end

    local metadata = mp.get_property_native("vf-metadata/" .. label)
    stop_detection()

    if not metadata then
        set_status("failed")
        return
    end

    local source_w = tonumber(mp.get_property_native("width")) or 0
    local source_h = tonumber(mp.get_property_native("height")) or 0
    local crop = {
        w = even(metadata["lavfi.cropdetect.w"]),
        h = even(metadata["lavfi.cropdetect.h"]),
        x = even(metadata["lavfi.cropdetect.x"]),
        y = even(metadata["lavfi.cropdetect.y"]),
    }

    if requested_mode == "preserve" then
        if crop.y > 0 then
            crop.h = even(source_h - crop.y)
        end
        mp.set_property("video-align-y", "-1")
    elseif requested_mode == "crop" then
        if crop.y > 0 then
            crop.h = even(source_h - 2 * crop.y)
        end
        crop.x = even(metadata["lavfi.cropdetect.x"] or 0)
        local raw_w = even(metadata["lavfi.cropdetect.w"] or source_w)
        if raw_w < source_w and raw_w > 0 then
            crop.w = raw_w
        else
            crop.w = source_w
        end
        mp.set_property("video-align-y", "0")
    else
        mp.set_property("video-align-y", "0")
    end

    if not crop_is_safe(crop, source_w, source_h) then
        set_status("rejected")
        return
    end

    local effective = crop.x > 0 or crop.y > 0 or crop.w < source_w or crop.h < source_h
    if not effective then
        mp.set_property("user-data/anni-crop-rect", "")
        mp.set_property("user-data/anni-crop-raw", "")
        mp.set_property("video-crop", "")
        mp.set_property("video-align-y", "0")
        set_status("none")
        return
    end

    local raw_rect = string.format("%d:%d:%d:%d",
        even(metadata["lavfi.cropdetect.w"] or source_w),
        even(metadata["lavfi.cropdetect.h"] or source_h),
        even(metadata["lavfi.cropdetect.x"] or 0),
        even(metadata["lavfi.cropdetect.y"] or 0))
    mp.set_property("user-data/anni-crop-raw", raw_rect)

    local rect = string.format("%d:%d:%d:%d", crop.w, crop.h, crop.x, crop.y)
    mp.set_property("user-data/anni-crop-rect", rect)

    if mp.get_property("user-data/anni-smartfill-active") ~= "yes" then
        mp.set_property("video-crop",
            string.format("%dx%d+%d+%d", crop.w, crop.h, crop.x, crop.y))
    end

    set_status("applied")
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

    -- 扫描底部黑边 (从底部向上探测至画面 55% 高度)
    local min_bottom_scan = math.floor(h * 0.55)
    local bottom_y = h - 1
    local bottom_found = false
    for y = h - 1, min_bottom_scan, -1 do
        local active_count = 0
        local row_offset = y * stride
        for x = x_start, x_end, x_step do
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

    -- 2. 左右黑边检测 (Pillarbox/4:3)：在垂直中间 40% 高度区域 [0.30 * h .. 0.70 * h] 进行横向探测
    -- 避开左上角台标与底部居中字幕
    local y_mid_start = math.floor(h * 0.30)
    local y_mid_end = math.floor(h * 0.70)
    local y_step = 8

    local left_x = 0
    local max_left_scan = math.floor(w * 0.40)
    for x = 0, max_left_scan do
        local active_count = 0
        for y = y_mid_start, y_mid_end, y_step do
            local idx = y * stride + x * 4 + 1
            local b, g, r = string.byte(data, idx, idx + 2)
            if b and g and r and (b > thresh or g > thresh or r > thresh) then
                active_count = active_count + 1
                if active_count >= 2 then
                    left_x = x
                    break
                end
            end
        end
        if active_count >= 2 then break end
    end

    local right_x = w - 1
    local min_right_scan = math.floor(w * 0.60)
    for x = w - 1, min_right_scan, -1 do
        local active_count = 0
        for y = y_mid_start, y_mid_end, y_step do
            local idx = y * stride + x * 4 + 1
            local b, g, r = string.byte(data, idx, idx + 2)
            if b and g and r and (b > thresh or g > thresh or r > thresh) then
                active_count = active_count + 1
                if active_count >= 2 then
                    right_x = x
                    break
                end
            end
        end
        if active_count >= 2 then break end
    end

    local is_all_black = (not top_found and not bottom_found)

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

    local source_w = tonumber(mp.get_property_native("width")) or scan.w
    local source_h = tonumber(mp.get_property_native("height")) or scan.h
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

    local is_letterbox = (top_black >= 8 or bottom_black >= 8)
    local is_pillarbox = (left_black >= 8 or right_black >= 8)

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

    if is_letterbox then
        local raw_h = math.max(16, even(source_h - top_black - bottom_black))
        raw.w = source_w
        raw.h = raw_h
        raw.x = 0
        raw.y = top_black

        if requested_mode == "preserve" then
            crop.w = source_w
            crop.h = even(source_h - top_black)
            crop.x = 0
            crop.y = top_black
            mp.set_property("video-align-y", "-1")
        elseif requested_mode == "crop" then
            local eff_crop_h = even(source_h - 2 * top_black)
            if eff_crop_h < source_h * options.min_ratio then
                eff_crop_h = raw_h
            end
            crop.w = source_w
            crop.h = eff_crop_h
            crop.x = 0
            crop.y = top_black
            mp.set_property("video-align-y", "0")
        else
            mp.set_property("video-align-y", "0")
        end
    elseif is_pillarbox then
        local raw_w = math.max(16, even(source_w - left_black - right_black))
        raw.w = raw_w
        raw.h = source_h
        raw.x = left_black
        raw.y = 0

        crop.w = raw_w
        crop.h = source_h
        crop.x = left_black
        crop.y = 0
        mp.set_property("video-align-y", "0")
    else
        mp.set_property("user-data/anni-crop-rect", "")
        mp.set_property("user-data/anni-crop-raw", "")
        mp.set_property("video-crop", "")
        mp.set_property("video-align-y", "0")
        set_status("none")
        return
    end

    if not crop_is_safe(crop, source_w, source_h) then
        set_status("rejected")
        return
    end

    local raw_rect = string.format("%d:%d:%d:%d", raw.w, raw.h, raw.x, raw.y)
    mp.set_property("user-data/anni-crop-raw", raw_rect)

    local rect = string.format("%d:%d:%d:%d", crop.w, crop.h, crop.x, crop.y)
    mp.set_property("user-data/anni-crop-rect", rect)

    if mp.get_property("user-data/anni-smartfill-active") ~= "yes" then
        mp.set_property("video-crop",
            string.format("%dx%d+%d+%d", crop.w, crop.h, crop.x, crop.y))
    end

    set_status("applied")
end

local sample1 = nil

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

    local chosen = nil
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
        set_status("none")
        return
    else
        start_vf_fallback()
        return
    end

    apply_detected_crop(chosen)
end

local function sample_pass_1()
    delay_timer = nil
    if not requested_mode then return end

    local source_w = tonumber(mp.get_property_native("width")) or 0
    if source_w <= 0 then
        sampling_retries = sampling_retries + 1
        if sampling_retries > 10 then
            sampling_retries = 0
            set_status("unavailable")
            return
        end
        delay_timer = mp.add_timeout(0.3, sample_pass_1)
        return
    end
    sampling_retries = 0

    local frame1 = mp.command_native({"screenshot-raw", "video"})
    sample1 = scan_raw_frame(frame1, 24)

    if not sample1 then
        start_vf_fallback()
        return
    end

    set_status("detecting")
    delay_timer = mp.add_timeout(0.25, sample_pass_2)
end

local function begin_sampling()
    sample1 = nil
    sample_pass_1()
end

local function start_crop(mode)
    if mode ~= "preserve" and mode ~= "crop" then
        return
    end
    if mp.get_property_bool("current-tracks/video/image", false) == true then
        set_status("unavailable")
        return
    end

    clear_crop()
    requested_mode = mode
    sampling_retries = 0
    set_status("waiting")
    delay_timer = mp.add_timeout(options.detect_delay, begin_sampling)
end

local function reapply_crop_if_needed()
    if requested_mode and requested_mode ~= "none" and mp.get_property("user-data/anni-smartfill-active") ~= "yes" then
        local rect = mp.get_property("user-data/anni-crop-rect")
        if rect and rect ~= "" then
            local w, h, x, y = rect:match("^(%d+):(%d+):(%d+):(%d+)$")
            if w and h and tonumber(w) > 0 and tonumber(h) > 0 then
                mp.set_property("video-crop", string.format("%sx%s+%s+%s", w, h, x, y))
                if requested_mode == "preserve" then
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
mp.register_event("end-file", clear_crop)
mp.register_event("video-reconfig", reapply_crop_if_needed)
mp.observe_property("user-data/anni-smartfill-active", "string", function(_, val)
    if val ~= "yes" then
        reapply_crop_if_needed()
    end
end)
