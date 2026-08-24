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
    requested_mode = nil
    mp.set_property("user-data/anni-crop-rect", "")
    mp.set_property("file-local-options/video-crop", "")
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
        mp.set_property("file-local-options/video-crop", "")
        mp.set_property("video-align-y", "0")
        set_status("none")
        return
    end

    local rect = string.format("%d:%d:%d:%d", crop.w, crop.h, crop.x, crop.y)
    mp.set_property("user-data/anni-crop-rect", rect)
    mp.set_property("file-local-options/video-crop",
        string.format("%dx%d+%d+%d", crop.w, crop.h, crop.x, crop.y))

    set_status("applied")
end

local function begin_sampling()
    delay_timer = nil
    if not requested_mode then return end

    mp.commandv("vf", "pre", string.format("@%s:cropdetect=limit=%s:round=%d:reset=0",
        label, options.detect_limit, options.detect_round))
    detect_timer = mp.add_timeout(options.detect_seconds, finish_detection)
    set_status("detecting")
end

local function start_crop(mode)
    if mode ~= "preserve" and mode ~= "crop" then
        return
    end
    if mp.get_property_native("current-tracks/video/image") ~= false then
        set_status("unavailable")
        return
    end

    clear_crop()
    requested_mode = mode
    set_status("waiting")
    delay_timer = mp.add_timeout(options.detect_delay, begin_sampling)
end

mp.register_script_message("anni-autocrop-start", start_crop)
mp.register_script_message("anni-autocrop-clear", clear_crop)
mp.register_event("end-file", clear_crop)
