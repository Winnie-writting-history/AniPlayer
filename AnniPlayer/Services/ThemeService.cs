using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Application = System.Windows.Application;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using Point = System.Windows.Point;
using FontFamily = System.Windows.Media.FontFamily;

namespace AnniPlayer.Services
{
    public class ThemeGradientStop
    {
        public string ColorHex { get; set; } = "#FFFFFF";
        public double Offset { get; set; } = 0.0;
    }

    public class ThemeItem
    {
        public string Key { get; set; } = "";
        public string NameZh { get; set; } = "";
        public string NameEn { get; set; } = "";
        
        public string AccentHex { get; set; } = "#00E5FF";
        public string TextHex { get; set; } = "#DDDDDD";
        public string WindowBgHex { get; set; } = "#051024";
        public string TitleBarStartHex { get; set; } = "#D90E2C5A";
        public string TitleBarEndHex { get; set; } = "#E6081B38";
        public string ControlBarEndHex { get; set; } = "#F2051024";
        public string MenuBgHex { get; set; } = "#F0051024";
        public string DrawerBgHex { get; set; } = "#E6051024";
        public string BorderHex { get; set; } = "#403377FF";

        public string ButtonHoverHex { get; set; } = "#3A4B70";
        public string ButtonHoverFgHex { get; set; } = "#FFFFFF";
        public string InactiveButtonHex { get; set; } = "#C8C8C8";
        public string PrimaryBtnStartHex { get; set; } = "#0072FF";
        public string PrimaryBtnEndHex { get; set; } = "#00F2FE";

        public string SliderProgressHex { get; set; } = "";
        public string SliderTrackHex { get; set; } = "";
        public string MenuSeparatorHex { get; set; } = "";

        // ── 音频悬浮横幅与黑胶唱片主题属性 (Audio Banner & Vinyl Disc) ──
        public string AudioBannerBgHex { get; set; } = "";
        public string AudioBannerBorderHex { get; set; } = "";
        public string AudioDiscBorderHex { get; set; } = "";
        public string AudioAccentHex { get; set; } = "";
        public string AudioTextHex { get; set; } = "";
        public string AudioSubTextHex { get; set; } = "";

        public List<ThemeGradientStop>? TitleBarGradientStops { get; set; }
        public List<ThemeGradientStop>? ControlBarGradientStops { get; set; }

        // ── 尺寸与圆角 (Dimensions, CornerRadius & Borders) ──
        public double ButtonCornerRadius { get; set; } = 6.0;
        public double PanelCornerRadius { get; set; } = 8.0;
        public double WindowCornerRadius { get; set; } = 10.0;
        public double BorderThickness { get; set; } = 1.0;
        public double ButtonBorderThickness { get; set; } = 1.0;

        public double ChromeButtonSize { get; set; } = 32.0;
        public double ControlBarHeight { get; set; } = 96.0;
        public double TitleBarHeight { get; set; } = 40.0;
        public double PlayButtonSize { get; set; } = 44.0;

        // ── 字体与排版 (Typography & Fonts) ──
        public string FontFamily { get; set; } = "Microsoft YaHei, Segoe UI, sans-serif";
        public double FontSizeBase { get; set; } = 14.0;

        // ── 背景图与背景音乐 (Background Texture & Audio) ──
        public string? BackgroundImage { get; set; }
        public string? ThemeBg { get; set; }
        public string? IdleBg { get; set; }
        public string? TitleBarBg { get; set; }
        public string? ControlBarBg { get; set; }
        public string? LibraryBg { get; set; }
        public string? SettingsBg { get; set; }
        public string? PlaylistBg { get; set; }
        public string? FlareHex { get; set; }
        public double BackgroundOpacity { get; set; } = 1.0;
        public List<string>? BgmPlaylist { get; set; }

        // ── 默认开屏提示自定义 (Custom Idle Screen Prompts & Typography) ──
        [JsonPropertyName("idle_hint_title")]
        public string? IdleHintTitle { get; set; }

        [JsonPropertyName("idle_hint_subtitle")]
        public string? IdleHintSubtitle { get; set; }

        [JsonPropertyName("idle_hint_subtext")]
        public string? IdleHintSubText { get; set; }

        [JsonPropertyName("idle_hint_font_family")]
        public string? IdleHintFontFamily { get; set; }

        [JsonPropertyName("idle_hint_title_size")]
        public double? IdleHintTitleSize { get; set; }

        [JsonPropertyName("idle_hint_subtitle_size")]
        public double? IdleHintSubtitleSize { get; set; }

        [JsonPropertyName("idle_hint_title_hex")]
        public string? IdleHintTitleHex { get; set; }

        [JsonPropertyName("idle_hint_subtitle_hex")]
        public string? IdleHintSubtitleHex { get; set; }

        [JsonPropertyName("idle_hint_bold")]
        public bool? IdleHintBold { get; set; } = false;

        // ── 皮肤背景音乐与待机媒体控制 (Skin BGM & Idle Media Settings) ──
        [JsonPropertyName("bgm_audio")]
        public string? BgmAudio { get; set; }

        [JsonPropertyName("bgm_volume")]
        public double? BgmVolume { get; set; } = 70.0;

        [JsonPropertyName("bgm_speed")]
        public double? BgmSpeed { get; set; } = 1.0;

        [JsonPropertyName("bgm_loop")]
        public bool? BgmLoop { get; set; } = true;

        [JsonPropertyName("bgm_auto_play_on_idle")]
        public bool? BgmAutoPlayOnIdle { get; set; } = true;

        [JsonPropertyName("bgm_pause_on_media_playback")]
        public bool? BgmPauseOnMediaPlayback { get; set; } = true;

        [JsonPropertyName("idle_media_loop")]
        public bool? IdleMediaLoop { get; set; } = true;

        [JsonPropertyName("idle_slideshow_interval_sec")]
        public double? IdleSlideshowIntervalSec { get; set; } = 5.0;

        [JsonPropertyName("idle_slideshow_speed")]
        public double? IdleSlideshowSpeed { get; set; } = 1.0;

        [JsonPropertyName("idle_slideshow_transition_fade_ms")]
        public int? IdleSlideshowTransitionFadeMs { get; set; } = 600;

        // ── 皮肤待机视频轮播控制 (Skin Idle Video Carousel & Playback) ──
        [JsonPropertyName("idle_videos")]
        public List<string>? IdleVideos { get; set; } = null;

        [JsonPropertyName("idle_video_speed")]
        public double? IdleVideoSpeed { get; set; } = 1.0;

        [JsonPropertyName("idle_video_brightness")]
        public double? IdleVideoBrightness { get; set; } = 0.0;

        [JsonPropertyName("idle_video_loop")]
        public bool? IdleVideoLoop { get; set; } = true;

        [JsonPropertyName("panel_tint_opacity")]
        public double? PanelTintOpacity { get; set; }

        [JsonPropertyName("panel_opacity")]
        public double? PanelOpacity { get; set; }

        public bool IsCustomSkin { get; set; } = false;
        public string SkinFolderPath { get; set; } = "";
    }

    public class SkinItem
    {
        public string Key { get; set; } = "";
        public string NameZh { get; set; } = "";
        public string NameEn { get; set; } = "";
        public string? SkinFolderPath { get; set; }
        public string? SkinPackagePath { get; set; }
        public string? SkinXamlPath { get; set; }
        public string? SkinXamlContent { get; set; }
        public ThemeItem? ThemeConfig { get; set; }
        public bool HasXaml { get; set; } = false;
        public bool HasJsonParseError { get; set; } = false;
        public bool HasXamlParseError { get; set; } = false;
        public string? LastErrorMessage { get; set; }
    }

    public class ThemeService : INotifyPropertyChanged
    {
        public static ThemeService Instance { get; } = new ThemeService();

        /// <summary>
        /// 智能素材寻址器：优先从当前皮肤目录解析素材；若构建时未将大型音视频媒体复制到 Demo/bin 目录，
        /// 自动回退向源码根目录或父级开发目录寻址，确保免重复复制媒体文件时音画完全正常播放。
        /// </summary>
        public static string? ResolveSkinAssetPath(SkinItem skin, string? relativeOrFileName)
        {
            if (string.IsNullOrWhiteSpace(relativeOrFileName)) return null;
            if (Path.IsPathRooted(relativeOrFileName))
            {
                return File.Exists(relativeOrFileName) ? relativeOrFileName : null;
            }

            string folder = skin.SkinFolderPath ?? "";
            if (!string.IsNullOrEmpty(folder))
            {
                string p1 = Path.Combine(folder, relativeOrFileName);
                if (File.Exists(p1)) return p1;

                string folderName = Path.GetFileName(folder);

                // 尝试回退至项目根目录中的源 skin 目录 (避免编译时复制几百 MB 的媒体素材)
                string p2 = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "skin", folderName, relativeOrFileName);
                if (File.Exists(p2)) return p2;

                string p3 = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "skin", folderName, relativeOrFileName);
                if (File.Exists(p3)) return p3;

                string p4 = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "skin", folderName, relativeOrFileName);
                if (File.Exists(p4)) return p4;

                // 尝试 AppData 目录
                string p5 = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AniPlayer", "skins", folderName, relativeOrFileName);
                if (File.Exists(p5)) return p5;
            }

            return null;
        }

        public string? ResolvedSkinBgmPath
        {
            get
            {
                if (IsSkinActive && Skins.TryGetValue(_activeSkinKey, out var skin) && !string.IsNullOrEmpty(skin.SkinFolderPath))
                {
                    if (skin.ThemeConfig != null && skin.ThemeConfig.BgmAudio != null)
                    {
                        if (string.IsNullOrWhiteSpace(skin.ThemeConfig.BgmAudio) ||
                            skin.ThemeConfig.BgmAudio.Equals("none", StringComparison.OrdinalIgnoreCase))
                        {
                            return null;
                        }

                        return ResolveSkinAssetPath(skin, skin.ThemeConfig.BgmAudio);
                    }

                    // 仅当完全未在 skin.json 中声明 bgm_audio 时，才进行历史候选文件扫描
                    string[] candidates = new[] { "The_Color_of_Rain_(with_Ambiance).wav", "bgm.wav", "bgm.mp3", "bgm.flac", "bgm.ogg" };
                    foreach (var name in candidates)
                    {
                        string? p = ResolveSkinAssetPath(skin, name);
                        if (p != null) return p;
                    }
                }
                return null;
            }
        }

        public List<string> ResolvedSkinIdleVideos
        {
            get
            {
                var list = new List<string>();
                if (IsSkinActive && Skins.TryGetValue(_activeSkinKey, out var skin) && !string.IsNullOrEmpty(skin.SkinFolderPath))
                {
                    if (skin.ThemeConfig?.IdleVideos != null)
                    {
                        // 用户在 skin.json 中显式声明了 idle_videos（例如配置为空列表或注释了所有视频）
                        foreach (var v in skin.ThemeConfig.IdleVideos)
                        {
                            if (string.IsNullOrWhiteSpace(v)) continue;
                            string? p = ResolveSkinAssetPath(skin, v);
                            if (p != null && !list.Contains(p))
                            {
                                list.Add(p);
                            }
                        }
                        return list;
                    }

                    // 仅当 skin.json 中完全未配置 idle_videos 字段时，才扫描默认视频文件
                    var candidateNames = new[] {
                        "Lonely_sorrow_3D_art_scene_202608080356.mp4",
                        "Lonely_sorrow_3D_art_scene_202608080357.mp4",
                        "idle.mp4", "idle_bg.mp4", "video1.mp4", "video2.mp4"
                    };
                    foreach (var name in candidateNames)
                    {
                        string? p = ResolveSkinAssetPath(skin, name);
                        if (p != null && !list.Contains(p)) list.Add(p);
                    }
                }
                return list;
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private ResourceDictionary? _dynamicSkinDictionary;

        private string _activeSkinKey = "none";
        public string ActiveSkinKey
        {
            get => _activeSkinKey;
            set
            {
                if (_activeSkinKey != value)
                {
                    _activeSkinKey = value;
                    ApplyActiveSkinOrTheme();
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ActiveSkinKey)));
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSkinActive)));
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CurrentTheme)));
                }
            }
        }

        public bool IsSkinActive => !string.IsNullOrEmpty(_activeSkinKey) && !_activeSkinKey.Equals("none", StringComparison.OrdinalIgnoreCase);

        private string _currentThemeKey = "default";
        public string CurrentThemeKey
        {
            get => _currentThemeKey;
            set
            {
                if (_currentThemeKey != value)
                {
                    _currentThemeKey = value;
                    if (!IsSkinActive)
                    {
                        ApplyTheme(value);
                    }
                    SaveConfig();
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CurrentThemeKey)));
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CurrentTheme)));
                }
            }
        }

        public ThemeItem CurrentTheme
        {
            get
            {
                if (IsSkinActive && Skins.TryGetValue(_activeSkinKey, out var skin) && skin.ThemeConfig != null)
                {
                    return skin.ThemeConfig;
                }
                return Themes.TryGetValue(_currentThemeKey, out var theme) ? theme : Themes["default"];
            }
        }

        public Dictionary<string, SkinItem> Skins { get; } = new(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, ThemeItem> Themes { get; } = new()
        {
            ["default"] = new ThemeItem
            {
                Key = "default",
                NameZh = "⚡ 极客电竞青",
                NameEn = "⚡ Tech Cyan",
                AccentHex = "#00F0FF",
                TextHex = "#F0F8FF",
                WindowBgHex = "#06152D",
                TitleBarStartHex = "#F00F4887",
                TitleBarEndHex = "#F20A2C5C",
                ControlBarEndHex = "#F5082147",
                MenuBgHex = "#0A2548",
                DrawerBgHex = "#0C2B54",
                MenuSeparatorHex = "#6000F0FF", // 60% 赛博电竞青微光刻线
                BorderHex = "#6000B2FF",
                ButtonHoverHex = "#28528C",
                ButtonHoverFgHex = "#FFFFFF",
                InactiveButtonHex = "#D0E4FF",
                PrimaryBtnStartHex = "#0072FF",
                PrimaryBtnEndHex = "#00F2FE",
                SliderProgressHex = "#00F0FF",
                SliderTrackHex = "#400D2B52",
                AudioBannerBgHex = "#3306152D",
                AudioBannerBorderHex = "#A000F0FF",
                AudioDiscBorderHex = "#8000F0FF",
                AudioAccentHex = "#00F0FF",
                AudioTextHex = "#FFFFFF",
                AudioSubTextHex = "#C0E0FF"
            },
            ["lakeblue"] = new ThemeItem
            {
                Key = "lakeblue",
                NameZh = "🌊 湖蓝清澈",
                NameEn = "🌊 Lake Blue",
                AccentHex = "#B8E5FF",
                TextHex = "#FFFFFF",
                WindowBgHex = "#4FAAF2",
                TitleBarStartHex = "#E656ADF4",
                TitleBarEndHex = "#DC3B9CE6",
                ControlBarEndHex = "#E02A8FD9",
                MenuBgHex = "#3596E2",
                DrawerBgHex = "#3596E2",
                MenuSeparatorHex = "#50FFFFFF",
                BorderHex = "#80B8E5FF",
                ButtonHoverHex = "#30FFFFFF",
                ButtonHoverFgHex = "#FFFFFF",
                InactiveButtonHex = "#FFFFFF",
                PrimaryBtnStartHex = "#2A8FD9",
                PrimaryBtnEndHex = "#56ADF4",
                SliderProgressHex = "#B8E5FF",
                SliderTrackHex = "#45FFFFFF",
                AudioBannerBgHex = "#330C2844",
                AudioBannerBorderHex = "#B056ADF4",
                AudioDiscBorderHex = "#9056ADF4",
                AudioAccentHex = "#56ADF4",
                AudioTextHex = "#FFFFFF",
                AudioSubTextHex = "#D8ECFF",
                TitleBarGradientStops = new List<ThemeGradientStop>
                {
                    new ThemeGradientStop { ColorHex = "#F560B0F5", Offset = 0.0 },
                    new ThemeGradientStop { ColorHex = "#E04FAAF2", Offset = 0.25 },
                    new ThemeGradientStop { ColorHex = "#E856ADF4", Offset = 0.50 },
                    new ThemeGradientStop { ColorHex = "#D82A8FD9", Offset = 0.75 },
                    new ThemeGradientStop { ColorHex = "#E04FAAF2", Offset = 1.0 }
                },
                ControlBarGradientStops = new List<ThemeGradientStop>
                {
                    new ThemeGradientStop { ColorHex = "#E04FAAF2", Offset = 0.0 },
                    new ThemeGradientStop { ColorHex = "#E856ADF4", Offset = 0.30 },
                    new ThemeGradientStop { ColorHex = "#D82A8FD9", Offset = 0.70 },
                    new ThemeGradientStop { ColorHex = "#F560B0F5", Offset = 1.0 }
                }
            },
            ["rosegold"] = new ThemeItem
            {
                Key = "rosegold",
                NameZh = "🌸 玫瑰金典",
                NameEn = "🌸 Rose Gold",
                AccentHex = "#2B0B11",
                TextHex = "#2B0B11",
                WindowBgHex = "#E59E91",
                TitleBarStartHex = "#F5C7BD",
                TitleBarEndHex = "#D88B7C",
                ControlBarEndHex = "#C8796B",
                MenuBgHex = "#E8A598",
                DrawerBgHex = "#E8A598",
                MenuSeparatorHex = "#A8584A", // 高对比度典雅深玫瑰木实色刻线
                BorderHex = "#80B35948",
                ButtonHoverHex = "#35000000",
                ButtonHoverFgHex = "#FFFFFF",
                InactiveButtonHex = "#FFFFFF",
                PrimaryBtnStartHex = "#4A1721",
                PrimaryBtnEndHex = "#7A2838",
                SliderProgressHex = "#4A1721",
                SliderTrackHex = "#45FFFFFF",
                AudioBannerBgHex = "#38240E14",
                AudioBannerBorderHex = "#FFF5C7BD",
                AudioDiscBorderHex = "#D0FFA090",
                AudioAccentHex = "#FFA090",
                AudioTextHex = "#FFFFFF",
                AudioSubTextHex = "#FAD4CD",
                TitleBarGradientStops = new List<ThemeGradientStop>
                {
                    new ThemeGradientStop { ColorHex = "#F8D3CB", Offset = 0.0 },
                    new ThemeGradientStop { ColorHex = "#E8A598", Offset = 0.18 },
                    new ThemeGradientStop { ColorHex = "#F5C7BD", Offset = 0.38 },
                    new ThemeGradientStop { ColorHex = "#D88B7C", Offset = 0.58 },
                    new ThemeGradientStop { ColorHex = "#F3BEB3", Offset = 0.78 },
                    new ThemeGradientStop { ColorHex = "#E8A598", Offset = 1.0 }
                },
                ControlBarGradientStops = new List<ThemeGradientStop>
                {
                    new ThemeGradientStop { ColorHex = "#E8A598", Offset = 0.0 },
                    new ThemeGradientStop { ColorHex = "#F5C7BD", Offset = 0.22 },
                    new ThemeGradientStop { ColorHex = "#D88B7C", Offset = 0.48 },
                    new ThemeGradientStop { ColorHex = "#F8D3CB", Offset = 0.72 },
                    new ThemeGradientStop { ColorHex = "#E8A598", Offset = 1.0 }
                }
            },
            ["champagne"] = new ThemeItem
            {
                Key = "champagne",
                NameZh = "🥂 香槟金典",
                NameEn = "🥂 Champagne Gold",
                AccentHex = "#241801",
                TextHex = "#241801",
                WindowBgHex = "#E6CF9B",
                TitleBarStartHex = "#F7E6B8",
                TitleBarEndHex = "#D8B468",
                ControlBarEndHex = "#C8A252",
                MenuBgHex = "#ECD8AA",
                DrawerBgHex = "#ECD8AA",
                MenuSeparatorHex = "#B08932", // 高对比度古典贵族暖琥珀金实色刻线
                BorderHex = "#80B38F38",
                ButtonHoverHex = "#35000000",
                ButtonHoverFgHex = "#FFFFFF",
                InactiveButtonHex = "#FFFFFF",
                PrimaryBtnStartHex = "#4A3508",
                PrimaryBtnEndHex = "#7A5B18",
                SliderProgressHex = "#4A3508",
                SliderTrackHex = "#45FFFFFF",
                AudioBannerBgHex = "#38241B0E",
                AudioBannerBorderHex = "#FFF7E6B8",
                AudioDiscBorderHex = "#D0F5D77F",
                AudioAccentHex = "#F5D77F",
                AudioTextHex = "#FFFFFF",
                AudioSubTextHex = "#FFF0C8",
                TitleBarGradientStops = new List<ThemeGradientStop>
                {
                    new ThemeGradientStop { ColorHex = "#FFF3D6", Offset = 0.0 },
                    new ThemeGradientStop { ColorHex = "#ECD8AA", Offset = 0.18 },
                    new ThemeGradientStop { ColorHex = "#F7E6B8", Offset = 0.38 },
                    new ThemeGradientStop { ColorHex = "#D8B468", Offset = 0.58 },
                    new ThemeGradientStop { ColorHex = "#FAF0D4", Offset = 0.78 },
                    new ThemeGradientStop { ColorHex = "#ECD8AA", Offset = 1.0 }
                },
                ControlBarGradientStops = new List<ThemeGradientStop>
                {
                    new ThemeGradientStop { ColorHex = "#ECD8AA", Offset = 0.0 },
                    new ThemeGradientStop { ColorHex = "#F7E6B8", Offset = 0.22 },
                    new ThemeGradientStop { ColorHex = "#D8B468", Offset = 0.48 },
                    new ThemeGradientStop { ColorHex = "#FFF3D6", Offset = 0.72 },
                    new ThemeGradientStop { ColorHex = "#ECD8AA", Offset = 1.0 }
                }
            },
            ["silver"] = new ThemeItem
            {
                Key = "silver",
                NameZh = "💎 银色星钻",
                NameEn = "💎 Brushed Silver",
                AccentHex = "#0B1324",
                TextHex = "#0B1324",
                WindowBgHex = "#CBD5E1",
                TitleBarStartHex = "#F1F5F9",
                TitleBarEndHex = "#CBD5E1",
                ControlBarEndHex = "#94A3B8",
                MenuBgHex = "#E2E8F0",
                DrawerBgHex = "#E2E8F0",
                MenuSeparatorHex = "#64748B", // 高对比度冷轧精钢岩板灰实色刻线（清爽分明）
                BorderHex = "#8064748B",
                ButtonHoverHex = "#35000000",
                ButtonHoverFgHex = "#FFFFFF",
                InactiveButtonHex = "#FFFFFF",
                PrimaryBtnStartHex = "#1E293B",
                PrimaryBtnEndHex = "#334155",
                SliderProgressHex = "#1E293B",
                SliderTrackHex = "#45FFFFFF",
                AudioBannerBgHex = "#38121A28",
                AudioBannerBorderHex = "#FFF1F5F9",
                AudioDiscBorderHex = "#D0CBD5E1",
                AudioAccentHex = "#70B8FF",
                AudioTextHex = "#FFFFFF",
                AudioSubTextHex = "#E2E8F0",
                TitleBarGradientStops = new List<ThemeGradientStop>
                {
                    new ThemeGradientStop { ColorHex = "#FFFFFF", Offset = 0.0 },
                    new ThemeGradientStop { ColorHex = "#CBD5E1", Offset = 0.18 },
                    new ThemeGradientStop { ColorHex = "#E2E8F0", Offset = 0.38 },
                    new ThemeGradientStop { ColorHex = "#94A3B8", Offset = 0.58 },
                    new ThemeGradientStop { ColorHex = "#F1F5F9", Offset = 0.78 },
                    new ThemeGradientStop { ColorHex = "#CBD5E1", Offset = 1.0 }
                },
                ControlBarGradientStops = new List<ThemeGradientStop>
                {
                    new ThemeGradientStop { ColorHex = "#CBD5E1", Offset = 0.0 },
                    new ThemeGradientStop { ColorHex = "#E2E8F0", Offset = 0.22 },
                    new ThemeGradientStop { ColorHex = "#94A3B8", Offset = 0.48 },
                    new ThemeGradientStop { ColorHex = "#FFFFFF", Offset = 0.72 },
                    new ThemeGradientStop { ColorHex = "#CBD5E1", Offset = 1.0 }
                }
            },
            ["pureblack"] = new ThemeItem
            {
                Key = "pureblack",
                NameZh = "🖤 纯黑 OLED",
                NameEn = "🖤 Pure Black (OLED)",
                AccentHex = "#00E5FF",
                TextHex = "#EEEEEE",
                WindowBgHex = "#000000",
                TitleBarStartHex = "#D91C1C1C",
                TitleBarEndHex = "#E60E0E0E",
                ControlBarEndHex = "#F2000000",
                MenuBgHex = "#0D0D0D",
                DrawerBgHex = "#0D0D0D",
                MenuSeparatorHex = "#333333", // 钛晶暗黑实感刻线
                BorderHex = "#555555", // 钛晶哑光银白实体边框（清晰可见）
                ButtonHoverHex = "#33FFFFFF",
                ButtonHoverFgHex = "#FFFFFF",
                InactiveButtonHex = "#C8C8C8",
                PrimaryBtnStartHex = "#0072FF",
                PrimaryBtnEndHex = "#00F2FE",
                SliderProgressHex = "#00E5FF",
                SliderTrackHex = "#33FFFFFF",
                AudioBannerBgHex = "#38000000",
                AudioBannerBorderHex = "#9000E5FF",
                AudioDiscBorderHex = "#8000E5FF",
                AudioAccentHex = "#00E5FF",
                AudioTextHex = "#FFFFFF",
                AudioSubTextHex = "#CCCCCC"
            },
            ["emerald"] = new ThemeItem
            {
                Key = "emerald",
                NameZh = "🌲 翡翠深林",
                NameEn = "🌲 Emerald Forest",
                AccentHex = "#10B981",
                TextHex = "#ECFDF5",
                WindowBgHex = "#064E3B",
                TitleBarStartHex = "#E6064E3B",
                TitleBarEndHex = "#F2064E3B",
                ControlBarEndHex = "#F2022C22",
                MenuBgHex = "#064E3B",
                DrawerBgHex = "#064E3B",
                MenuSeparatorHex = "#6010B981", // 60% 苍翠薄荷翡翠微光刻线
                BorderHex = "#4010B981",
                ButtonHoverHex = "#059669",
                ButtonHoverFgHex = "#FFFFFF",
                InactiveButtonHex = "#A7F3D0",
                PrimaryBtnStartHex = "#059669",
                PrimaryBtnEndHex = "#34D399",
                SliderProgressHex = "#10B981",
                SliderTrackHex = "#40042F24",
                AudioBannerBgHex = "#33062B20",
                AudioBannerBorderHex = "#A010B981",
                AudioDiscBorderHex = "#9010B981",
                AudioAccentHex = "#34D399",
                AudioTextHex = "#FFFFFF",
                AudioSubTextHex = "#A7F3D0"
            },
            ["violet"] = new ThemeItem
            {
                Key = "violet",
                NameZh = "🔮 霓虹极光紫",
                NameEn = "🔮 Neon Aurora",
                AccentHex = "#A855F7",
                TextHex = "#FAF5FF",
                WindowBgHex = "#3B0764",
                TitleBarStartHex = "#E6581C87",
                TitleBarEndHex = "#F23B0764",
                ControlBarEndHex = "#F22E0854",
                MenuBgHex = "#3B0764",
                DrawerBgHex = "#3B0764",
                MenuSeparatorHex = "#60A855F7", // 60% 极光魅紫微光刻线
                BorderHex = "#40A855F7",
                ButtonHoverHex = "#7E22CE",
                ButtonHoverFgHex = "#FFFFFF",
                InactiveButtonHex = "#E9D5FF",
                PrimaryBtnStartHex = "#7E22CE",
                PrimaryBtnEndHex = "#C084FC",
                SliderProgressHex = "#A855F7",
                SliderTrackHex = "#40221340",
                AudioBannerBgHex = "#33200638",
                AudioBannerBorderHex = "#A0A855F7",
                AudioDiscBorderHex = "#90A855F7",
                AudioAccentHex = "#C084FC",
                AudioTextHex = "#FFFFFF",
                AudioSubTextHex = "#E9D5FF"
            }
        };

        private ThemeService()
        {
            ScanAndLoadCustomSkins();
            LoadConfig();
        }

        public void ApplyActiveSkinOrTheme()
        {
            bool appliedSuccessfully = false;
            if (IsSkinActive && Skins.TryGetValue(_activeSkinKey, out var skin))
            {
                appliedSuccessfully = ApplySkinInternal(skin);
                if (!appliedSuccessfully)
                {
                    string errDetail = skin.LastErrorMessage ?? "未知解析错误 (Unknown parse error)";
                    System.Diagnostics.Debug.WriteLine($"[ThemeService] Skin '{_activeSkinKey}' failed: {errDetail}. Rolling back to disabled skin state.");

                    // 1. 立即回退到禁用皮肤状态并应用内置主题
                    _activeSkinKey = "none";
                    RemoveDynamicSkin();
                    ApplyTheme(_currentThemeKey);
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ActiveSkinKey)));
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSkinActive)));
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CurrentTheme)));

                    // 2. 弹框提示报错内容
                    ShowSkinErrorDialog(skin, errDetail);
                }
            }

            if (!appliedSuccessfully && (!IsSkinActive || !Skins.ContainsKey(_activeSkinKey)))
            {
                RemoveDynamicSkin();
                ApplyTheme(_currentThemeKey);
            }
            SaveConfig();
        }

        private static void ShowSkinErrorDialog(SkinItem skin, string errorDetail)
        {
            try
            {
                Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
                {
                    string title = I18nService.Instance["SkinLoadErrorTitle"];
                    if (string.IsNullOrEmpty(title) || title == "SkinLoadErrorTitle")
                    {
                        title = (I18nService.Instance.CurrentLanguage == "en-US") ? "Skin Load Failed" : "皮肤加载失败";
                    }
                    string format = I18nService.Instance["SkinLoadErrorMessage"];
                    if (string.IsNullOrEmpty(format) || format == "SkinLoadErrorMessage")
                    {
                        format = (I18nService.Instance.CurrentLanguage == "en-US")
                            ? "Failed to parse skin \"{0}\". Automatically rolled back to disabled skin state.\n\nError details:\n{1}"
                            : "皮肤「{0}」解析失败，已自动回退至禁用皮肤状态。\n\n具体错误原因：\n{1}";
                    }
                    string skinName = (I18nService.Instance.CurrentLanguage == "en-US") ? skin.NameEn : skin.NameZh;
                    if (string.IsNullOrWhiteSpace(skinName)) skinName = skin.Key;
                    string msg = string.Format(format, skinName, errorDetail);

                    System.Windows.MessageBox.Show(msg, title, MessageBoxButton.OK, MessageBoxImage.Warning);
                }));
            }
            catch { }
        }

        private void RemoveDynamicSkin()
        {
            try
            {
                var appResources = Application.Current?.Resources;
                if (appResources != null)
                {
                    if (_dynamicSkinDictionary != null)
                    {
                        appResources.MergedDictionaries.Remove(_dynamicSkinDictionary);
                        _dynamicSkinDictionary = null;
                    }
                    appResources["ThemeTitleBarImageBrush"] = null;
                    appResources["ThemeControlBarImageBrush"] = null;
                    appResources["ThemePanelImageBrush"] = null;
                    appResources["ThemeIdleBgBrush"] = null;
                    appResources["ThemeMenuBgBrush"] = null;
                    appResources["ThemeDrawerBgBrush"] = null;
                    appResources["ThemeWindowBgBrush"] = null;
                    appResources["ThemePanelBgBrush"] = null;
                    appResources["ThemeMenuSeparatorBrush"] = null;
                    appResources["ThemePrimaryBtnBrush"] = null;
                    appResources["ThemeSliderProgressBrush"] = null;
                    appResources["ThemeSliderTrackBrush"] = null;
                    appResources["ThemePanelTintOpacity"] = null;
                    appResources["ThemePanelOpacity"] = null;
                    appResources["ThemeBorderThickness"] = new Thickness(1.0);
                    appResources["ThemeButtonBorderThickness"] = new Thickness(1.0);
                    appResources["ThemePanelCornerRadius"] = new CornerRadius(8.0);
                    appResources["ThemeButtonCornerRadius"] = new CornerRadius(6.0);
                    appResources["ThemeWindowCornerRadius"] = new CornerRadius(10.0);
                    appResources["ThemeOpenBtnBgBrush"] = null;
                    appResources["ThemeOpenBtnBorderBrush"] = null;
                    appResources["ThemeOpenBtnSheenBrush"] = null;
                    appResources["ThemeOpenBtnHoverBgBrush"] = null;
                    appResources["ThemeOpenBtnGlowColor"] = System.Windows.Media.Colors.Transparent;
                    appResources["ThemeMenuSheenBrush"] = null;
                    appResources["ThemeAudioBannerBgBrush"] = null;
                    appResources["ThemeAudioBannerBorderBrush"] = null;
                    appResources["ThemeAudioDiscBorderBrush"] = null;
                    appResources["ThemeAudioAccentBrush"] = null;
                    appResources["ThemeAudioTextBrush"] = null;
                    appResources["ThemeAudioSubTextBrush"] = null;
                }
            }
            catch { }
        }

        private static System.Windows.Media.Brush CreateCompositeMaterialBrush(BitmapImage bmp, Color tintColor, double tintOpacity, double overallOpacity = 1.0, AlignmentY alignmentY = AlignmentY.Center)
        {
            var group = new DrawingGroup();

            double w = bmp.PixelWidth > 0 ? bmp.PixelWidth : 1920;
            double h = bmp.PixelHeight > 0 ? bmp.PixelHeight : 1080;

            // 1. 底层：母版纹理图片（依据位图实际像素精准对齐）
            var imgDrawing = new ImageDrawing(bmp, new Rect(0, 0, w, h));
            group.Children.Add(imgDrawing);

            // 2. 表层：深色压暗半透遮罩（保护文字对比度与清晰度）
            byte alpha = (byte)Math.Clamp((int)(tintOpacity * 255), 0, 255);
            var tintBrush = new SolidColorBrush(Color.FromArgb(alpha, tintColor.R, tintColor.G, tintColor.B));
            tintBrush.Freeze();

            group.Children.Add(new GeometryDrawing(tintBrush, null, new GeometryGroup { Children = { new RectangleGeometry(new Rect(0, 0, w, h)) } }));

            group.Freeze();

            var drawingBrush = new DrawingBrush(group)
            {
                Stretch = Stretch.UniformToFill,
                AlignmentY = alignmentY,
                AlignmentX = AlignmentX.Center,
                Opacity = Math.Clamp(overallOpacity, 0.05, 1.0)
            };
            drawingBrush.Freeze();
            return drawingBrush;
        }

        private bool ApplySkinInternal(SkinItem skin)
        {
            try
            {
                var appResources = Application.Current?.Resources;
                if (appResources == null) return false;

                // 容错门禁 1：如果 skin.json 解析失败，直接判定失败并触发回退
                if (skin.HasJsonParseError)
                {
                    if (string.IsNullOrEmpty(skin.LastErrorMessage))
                    {
                        skin.LastErrorMessage = "skin.json 格式或语法错误，解析失败。";
                    }
                    return false;
                }

                ResourceDictionary? parsedDict = null;

                // 容错门禁 2：如果包含 skin.xaml，必须成功解析
                if (!string.IsNullOrEmpty(skin.SkinXamlContent))
                {
                    try
                    {
                        using var reader = new StringReader(skin.SkinXamlContent);
                        using var xmlReader = System.Xml.XmlReader.Create(reader);
                        parsedDict = (ResourceDictionary)XamlReader.Load(xmlReader);
                    }
                    catch (Exception ex)
                    {
                        skin.HasXamlParseError = true;
                        skin.LastErrorMessage = $"skin.xaml XAML 语法或解析错误: {ex.Message}";
                        return false;
                    }
                }
                else if (!string.IsNullOrEmpty(skin.SkinXamlPath) && File.Exists(skin.SkinXamlPath))
                {
                    try
                    {
                        using var stream = File.OpenRead(skin.SkinXamlPath);
                        parsedDict = (ResourceDictionary)XamlReader.Load(stream);
                    }
                    catch (Exception ex)
                    {
                        skin.HasXamlParseError = true;
                        skin.LastErrorMessage = $"skin.xaml XAML 语法或解析错误: {ex.Message}";
                        return false;
                    }
                }
                else if (skin.HasXaml)
                {
                    skin.HasXamlParseError = true;
                    skin.LastErrorMessage = "skin.xaml 样式表文件缺失或无法读取。";
                    return false;
                }

                if (skin.HasXamlParseError)
                {
                    return false;
                }

                RemoveDynamicSkin();

                if (parsedDict != null)
                {
                    _dynamicSkinDictionary = parsedDict;
                    appResources.MergedDictionaries.Add(_dynamicSkinDictionary);
                }

                // 2. If skin has theme config / skin.json, apply tokens
                if (skin.ThemeConfig != null)
                {
                    ApplyThemeProperties(appResources, skin.ThemeConfig);
                }

                // 3. Resolve and apply background texture image brushes
                ApplySkinImages(appResources, skin);

                return true;
            }
            catch (Exception ex)
            {
                skin.LastErrorMessage = $"应用皮肤发生未预料异常: {ex.Message}";
                return false;
            }
        }

        private void ApplySkinImages(ResourceDictionary appResources, SkinItem skin)
        {
            try
            {
                string? folder = skin.SkinFolderPath;
                if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
                {
                    // Gracefully reset to null so native vector gradients take over
                    appResources["ThemeTitleBarImageBrush"] = null;
                    appResources["ThemeControlBarImageBrush"] = null;
                    appResources["ThemePanelImageBrush"] = null;
                    appResources["ThemeIdleBgBrush"] = null;
                    return;
                }

                // 1. Look for theme_bg.png or master background
                string? themeBgPath = null;
                if (!string.IsNullOrWhiteSpace(skin.ThemeConfig?.ThemeBg))
                {
                    string candidate = Path.IsPathRooted(skin.ThemeConfig.ThemeBg)
                        ? skin.ThemeConfig.ThemeBg
                        : Path.Combine(folder, skin.ThemeConfig.ThemeBg);
                    if (File.Exists(candidate)) themeBgPath = candidate;
                }
                if (themeBgPath == null)
                {
                    string p1 = Path.Combine(folder, "theme_bg.jpg");
                    string p2 = Path.Combine(folder, "theme_bg.png");
                    string p3 = Path.Combine(folder, "bg.jpg");
                    string p4 = Path.Combine(folder, "bg.png");
                    string p5 = Path.Combine(folder, "background.jpg");
                    string p6 = Path.Combine(folder, skin.Key + ".jpg");
                    string p7 = Path.Combine(folder, skin.Key + ".png");
                    if (File.Exists(p1)) themeBgPath = p1;
                    else if (File.Exists(p2)) themeBgPath = p2;
                    else if (File.Exists(p3)) themeBgPath = p3;
                    else if (File.Exists(p4)) themeBgPath = p4;
                    else if (File.Exists(p5)) themeBgPath = p5;
                    else if (File.Exists(p6)) themeBgPath = p6;
                    else if (File.Exists(p7)) themeBgPath = p7;
                }

                if (themeBgPath != null && File.Exists(themeBgPath))
                {
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.UriSource = new Uri(themeBgPath, UriKind.Absolute);
                    bmp.EndInit();
                    bmp.Freeze();

                    // 1. Top Bar ImageBrush (AlignmentY=Top) & Bottom Control Bar ImageBrush (AlignmentY=Bottom)
                    double panelOpacity = 1.0;
                    if (appResources["ThemePanelOpacity"] is double xamlPanelOpacity && xamlPanelOpacity >= 0.0 && xamlPanelOpacity <= 1.0)
                    {
                        panelOpacity = xamlPanelOpacity;
                    }
                    else if (skin.ThemeConfig?.PanelOpacity is double jsonPanelOpacity && jsonPanelOpacity >= 0.0 && jsonPanelOpacity <= 1.0)
                    {
                        panelOpacity = jsonPanelOpacity;
                    }

                    var titleImgBrush = new ImageBrush(bmp)
                    {
                        Stretch = Stretch.UniformToFill,
                        AlignmentY = AlignmentY.Top,
                        AlignmentX = AlignmentX.Center,
                        Opacity = panelOpacity
                    };
                    titleImgBrush.Freeze();
                    appResources["ThemeTitleBarImageBrush"] = titleImgBrush;

                    var ctrlImgBrush = new ImageBrush(bmp)
                    {
                        Stretch = Stretch.UniformToFill,
                        AlignmentY = AlignmentY.Bottom,
                        AlignmentX = AlignmentX.Center,
                        Opacity = panelOpacity
                    };
                    ctrlImgBrush.Freeze();
                    appResources["ThemeControlBarImageBrush"] = ctrlImgBrush;

                    // Panels ImageBrush (AlignmentY=Center)
                    var panelImgBrush = new ImageBrush(bmp)
                    {
                        Stretch = Stretch.UniformToFill,
                        AlignmentY = AlignmentY.Center,
                        AlignmentX = AlignmentX.Center,
                        Opacity = 1.0
                    };
                    panelImgBrush.Freeze();
                    appResources["ThemePanelImageBrush"] = panelImgBrush;

                    // Composite Material Brushes (Texture Image + Tint Protection Layer)
                    Color tintColor = Color.FromRgb(5, 24, 45); // Deep midnight obsidian teal fallback
                    if (appResources["ThemeWindowBgBrush"] is SolidColorBrush sb)
                    {
                        tintColor = sb.Color;
                    }

                    double tintOpacity = 0.70;
                    if (appResources["ThemePanelTintOpacity"] is double xamlTint && xamlTint >= 0.0 && xamlTint <= 1.0)
                    {
                        tintOpacity = xamlTint;
                    }
                    else if (skin.ThemeConfig?.PanelTintOpacity is double jsonTint && jsonTint >= 0.0 && jsonTint <= 1.0)
                    {
                        tintOpacity = jsonTint;
                    }

                    var compositePanelBrush = CreateCompositeMaterialBrush(bmp, tintColor, tintOpacity, panelOpacity, AlignmentY.Center);
                    var compositeMenuBrush = CreateCompositeMaterialBrush(bmp, tintColor, tintOpacity, panelOpacity, AlignmentY.Center);
                    var compositeDrawerBrush = CreateCompositeMaterialBrush(bmp, tintColor, tintOpacity, panelOpacity, AlignmentY.Center);
                    var compositeTitleBrush = CreateCompositeMaterialBrush(bmp, tintColor, tintOpacity, panelOpacity, AlignmentY.Top);
                    var compositeControlBrush = CreateCompositeMaterialBrush(bmp, tintColor, tintOpacity, panelOpacity, AlignmentY.Bottom);

                    // 仅当皮肤自身未在 skin.xaml 中显式定义专属画刷时，才使用母版图片材质画刷作为回退
                    if (_dynamicSkinDictionary == null || !_dynamicSkinDictionary.Contains("ThemeWindowBgBrush"))
                    {
                        appResources["ThemeWindowBgBrush"] = compositePanelBrush;
                    }
                    if (_dynamicSkinDictionary == null || !_dynamicSkinDictionary.Contains("ThemeMenuBgBrush"))
                    {
                        appResources["ThemeMenuBgBrush"] = compositeMenuBrush;
                    }
                    if (_dynamicSkinDictionary == null || !_dynamicSkinDictionary.Contains("ThemeDrawerBgBrush"))
                    {
                        appResources["ThemeDrawerBgBrush"] = compositeDrawerBrush;
                    }
                    if (_dynamicSkinDictionary == null || !_dynamicSkinDictionary.Contains("ThemePanelBgBrush"))
                    {
                        appResources["ThemePanelBgBrush"] = compositePanelBrush;
                    }
                    if (_dynamicSkinDictionary == null || !_dynamicSkinDictionary.Contains("ThemeTitleBarBrush"))
                    {
                        appResources["ThemeTitleBarBrush"] = compositeTitleBrush;
                    }
                    if (_dynamicSkinDictionary == null || !_dynamicSkinDictionary.Contains("ThemeControlBarBrush"))
                    {
                        appResources["ThemeControlBarBrush"] = compositeControlBrush;
                    }

                    string skinSepHex = !string.IsNullOrEmpty(skin.ThemeConfig?.MenuSeparatorHex)
                        ? skin.ThemeConfig.MenuSeparatorHex
                        : "#80F5D77F";
                    var sepColor = (Color)ColorConverter.ConvertFromString(skinSepHex);
                    var sepBrush = new SolidColorBrush(sepColor);
                    sepBrush.Freeze();
                    appResources["ThemeMenuSeparatorBrush"] = sepBrush;
                }
                else
                {
                    // File missing: gracefully ignore and fall back to vector gradient brushes
                    appResources["ThemeTitleBarImageBrush"] = null;
                    appResources["ThemeControlBarImageBrush"] = null;
                    appResources["ThemePanelImageBrush"] = null;
                }

                // 2. Look for idle/opening wallpaper
                string? idleBgPath = null;
                if (!string.IsNullOrWhiteSpace(skin.ThemeConfig?.IdleBg))
                {
                    string candidate = Path.IsPathRooted(skin.ThemeConfig.IdleBg)
                        ? skin.ThemeConfig.IdleBg
                        : Path.Combine(folder, skin.ThemeConfig.IdleBg);
                    if (File.Exists(candidate)) idleBgPath = candidate;
                }
                if (idleBgPath == null)
                {
                    string p1 = Path.Combine(folder, "default.jpg");
                    string p2 = Path.Combine(folder, "default.png");
                    string p3 = Path.Combine(folder, "idle.jpg");
                    string p4 = Path.Combine(folder, "idle_bg.png");
                    string p5 = Path.Combine(folder, "wallpaper.png");
                    string p6 = Path.Combine(folder, "wallpaper.jpg");
                    if (File.Exists(p1)) idleBgPath = p1;
                    else if (File.Exists(p2)) idleBgPath = p2;
                    else if (File.Exists(p3)) idleBgPath = p3;
                    else if (File.Exists(p4)) idleBgPath = p4;
                    else if (File.Exists(p5)) idleBgPath = p5;
                    else if (File.Exists(p6)) idleBgPath = p6;
                }

                if (idleBgPath != null && File.Exists(idleBgPath))
                {
                    var bmpIdle = new BitmapImage();
                    bmpIdle.BeginInit();
                    bmpIdle.CacheOption = BitmapCacheOption.OnLoad;
                    bmpIdle.UriSource = new Uri(idleBgPath, UriKind.Absolute);
                    bmpIdle.EndInit();
                    bmpIdle.Freeze();

                    appResources["ThemeIdleBgBrush"] = new ImageBrush(bmpIdle)
                    {
                        Stretch = Stretch.UniformToFill,
                        AlignmentY = AlignmentY.Center,
                        AlignmentX = AlignmentX.Center
                    };
                }
                else
                {
                    // File missing: gracefully ignore and fall back to default window background
                    appResources["ThemeIdleBgBrush"] = null;
                }
            }
            catch { }
        }

        public void ApplyTheme(string key)
        {
            if (!Themes.TryGetValue(key, out var theme))
            {
                theme = Themes["default"];
                key = "default";
            }

            _currentThemeKey = key;

            try
            {
                RemoveDynamicSkin();

                var appResources = Application.Current?.Resources;
                if (appResources != null)
                {
                    ApplyThemeProperties(appResources, theme);
                }
            }
            catch { }

            SaveConfig();
        }

        private void ApplyThemeProperties(ResourceDictionary appResources, ThemeItem theme)
        {
            try
            {
                var accentColor = (Color)ColorConverter.ConvertFromString(theme.AccentHex);
                var textColor = (Color)ColorConverter.ConvertFromString(theme.TextHex);
                var windowBgColor = (Color)ColorConverter.ConvertFromString(theme.WindowBgHex);
                var menuBgColor = (Color)ColorConverter.ConvertFromString(theme.MenuBgHex);
                var drawerBgColor = (Color)ColorConverter.ConvertFromString(theme.DrawerBgHex);
                var borderColor = (Color)ColorConverter.ConvertFromString(theme.BorderHex);

                var titleStart = (Color)ColorConverter.ConvertFromString(theme.TitleBarStartHex);
                var titleEnd = (Color)ColorConverter.ConvertFromString(theme.TitleBarEndHex);
                var ctrlEnd = (Color)ColorConverter.ConvertFromString(theme.ControlBarEndHex);

                UpdateResourceBrush(appResources, "ThemeAccentBrush", accentColor);
                UpdateResourceBrush(appResources, "ThemeTextBrush", textColor);
                UpdateResourceBrush(appResources, "ThemeBorderBrush", borderColor);

                // ══ Menu, Drawer, Panels, TitleBar & ControlBar Crystal Frosted Glass (40% 半透水晶磨砂玻璃与高光质感) ══
                // 仅对极客电竞青、湖蓝、纯黑OLED、翡翠深林、霓虹极光紫生效；
                // 玫瑰金典、香槟金典、银色星钻及自定皮肤严格保持原样独立隔离！
                if (theme.Key is "default" or "lakeblue" or "pureblack" or "emerald" or "violet")
                {
                    // 1. 40% 半透水晶磨砂玻璃深邃微渐变 (Alpha 0x94 ~ 58% 至 0xA8 ~ 66%，与蓝宝石 40% 半透明完美对齐)
                    var frostedGlassBg = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
                    Color topGlass, bottomGlass, titleStartGlass, ctrlEndGlass;
                    if (theme.Key == "pureblack")
                    {
                        topGlass = Color.FromArgb(0x94, 0x1A, 0x1A, 0x22);
                        bottomGlass = Color.FromArgb(0xA8, 0x0A, 0x0A, 0x0E);
                        titleStartGlass = Color.FromArgb(0x94, 0x18, 0x18, 0x20);
                        ctrlEndGlass = Color.FromArgb(0xA8, 0x06, 0x06, 0x0A);
                    }
                    else if (theme.Key == "default")
                    {
                        topGlass = Color.FromArgb(0x94, 0x0E, 0x40, 0x7E);       // 饱满深邃赛博夜蓝 (RGB 14, 64, 126 - 消除灰度，增添深蓝质感)
                        bottomGlass = Color.FromArgb(0xA8, 0x08, 0x24, 0x4E);    // 幽邃深蓝基底 (RGB 8, 36, 78)
                        titleStartGlass = Color.FromArgb(0x94, 0x10, 0x48, 0x8C); // 顶栏清透深蓝高光 (RGB 16, 72, 140)
                        ctrlEndGlass = Color.FromArgb(0xA8, 0x06, 0x1C, 0x40);    // 底栏深邃深蓝收尾 (RGB 6, 28, 64)
                    }
                    else if (theme.Key == "lakeblue")
                    {
                        topGlass = Color.FromArgb(0x94, 0x56, 0xAD, 0xF4);       // 明亮柔美水波蓝 #56ADF4 (RGB 86, 173, 244)
                        bottomGlass = Color.FromArgb(0xA8, 0x2A, 0x8F, 0xD9);    // 纯澈中层湖蓝 #2A8FD9 (RGB 42, 143, 217)
                        titleStartGlass = Color.FromArgb(0x94, 0x60, 0xB0, 0xF5); // 顶部清澈水光蓝 #60B0F5 (RGB 96, 176, 245)
                        ctrlEndGlass = Color.FromArgb(0xA8, 0x1E, 0x7E, 0xC6);    // 底部深层水影 #1E7EC6 (RGB 30, 126, 198)
                    }
                    else if (theme.Key == "emerald")
                    {
                        topGlass = Color.FromArgb(0x94, 0x08, 0x48, 0x38);
                        bottomGlass = Color.FromArgb(0xA8, 0x04, 0x2A, 0x20);
                        titleStartGlass = Color.FromArgb(0x94, 0x0A, 0x54, 0x42);
                        ctrlEndGlass = Color.FromArgb(0xA8, 0x02, 0x22, 0x1A);
                    }
                    else // violet
                    {
                        topGlass = Color.FromArgb(0x94, 0x3E, 0x08, 0x6E);
                        bottomGlass = Color.FromArgb(0xA8, 0x24, 0x04, 0x42);
                        titleStartGlass = Color.FromArgb(0x94, 0x48, 0x0A, 0x7C);
                        ctrlEndGlass = Color.FromArgb(0xA8, 0x1C, 0x02, 0x36);
                    }
                    frostedGlassBg.GradientStops.Add(new GradientStop(topGlass, 0.0));
                    frostedGlassBg.GradientStops.Add(new GradientStop(bottomGlass, 1.0));
                    frostedGlassBg.Freeze();
                    appResources["ThemeMenuBgBrush"] = frostedGlassBg;
                    appResources["ThemeDrawerBgBrush"] = frostedGlassBg;
                    appResources["ThemePanelBgBrush"] = frostedGlassBg;
                    appResources["ThemeWindowBgBrush"] = frostedGlassBg;

                    // 2. 顶部水晶磨砂玻璃高光反光 (Top Specular Crystal Sheen Highlight)
                    var sheenBrush = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
                    sheenBrush.GradientStops.Add(new GradientStop(Color.FromArgb(0x60, 255, 255, 255), 0.0));
                    sheenBrush.GradientStops.Add(new GradientStop(Color.FromArgb(0x28, accentColor.R, accentColor.G, accentColor.B), 0.40));
                    sheenBrush.GradientStops.Add(new GradientStop(Color.FromArgb(0x00, 0, 0, 0), 1.0));
                    sheenBrush.Freeze();
                    appResources["ThemeMenuSheenBrush"] = sheenBrush;

                    // 3. 顶部标题栏与底部控制栏水晶半透渐变
                    var titleGradient = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
                    titleGradient.GradientStops.Add(new GradientStop(titleStartGlass, 0.0));
                    titleGradient.GradientStops.Add(new GradientStop(topGlass, 1.0));
                    titleGradient.Freeze();
                    appResources["ThemeTitleBarBrush"] = titleGradient;

                    var controlGradient = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
                    controlGradient.GradientStops.Add(new GradientStop(topGlass, 0.0));
                    controlGradient.GradientStops.Add(new GradientStop(ctrlEndGlass, 1.0));
                    controlGradient.Freeze();
                    appResources["ThemeControlBarBrush"] = controlGradient;
                }
                else
                {
                    // 玫瑰金典、香槟金典、银色星钻等维持原有纯色实体配置
                    UpdateResourceBrush(appResources, "ThemeWindowBgBrush", windowBgColor);
                    UpdateResourceBrush(appResources, "ThemePanelBgBrush", menuBgColor);
                    UpdateResourceBrush(appResources, "ThemeMenuBgBrush", menuBgColor);
                    UpdateResourceBrush(appResources, "ThemeDrawerBgBrush", drawerBgColor);
                    appResources["ThemeMenuSheenBrush"] = System.Windows.Media.Brushes.Transparent;

                    // TitleBar Gradient Brush
                    if (theme.TitleBarGradientStops != null && theme.TitleBarGradientStops.Count > 0)
                    {
                        var titleGradient = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0.5) };
                        foreach (var stop in theme.TitleBarGradientStops)
                            titleGradient.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString(stop.ColorHex), stop.Offset));
                        appResources["ThemeTitleBarBrush"] = titleGradient;
                    }
                    else
                    {
                        var titleGradient = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
                        titleGradient.GradientStops.Add(new GradientStop(titleStart, 0));
                        titleGradient.GradientStops.Add(new GradientStop(titleEnd, 1));
                        appResources["ThemeTitleBarBrush"] = titleGradient;
                    }

                    // ControlBar Gradient Brush
                    if (theme.ControlBarGradientStops != null && theme.ControlBarGradientStops.Count > 0)
                    {
                        var controlGradient = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0.5) };
                        foreach (var stop in theme.ControlBarGradientStops)
                            controlGradient.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString(stop.ColorHex), stop.Offset));
                        appResources["ThemeControlBarBrush"] = controlGradient;
                    }
                    else
                    {
                        var controlGradient = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
                        controlGradient.GradientStops.Add(new GradientStop(titleEnd, 0));
                        controlGradient.GradientStops.Add(new GradientStop(ctrlEnd, 1));
                        appResources["ThemeControlBarBrush"] = controlGradient;
                    }
                }

                // Menu Separator Brush (Per-theme custom explicit separator color)
                string sepColorHex = !string.IsNullOrEmpty(theme.MenuSeparatorHex)
                    ? theme.MenuSeparatorHex
                    : (theme.Key == "pureblack" ? "#282828" : "#40000000");
                var separatorColor = (Color)ColorConverter.ConvertFromString(sepColorHex);
                UpdateResourceBrush(appResources, "ThemeMenuSeparatorBrush", separatorColor);

                // SubText
                Color subTextColor;
                if (theme.Key is "rosegold" or "champagne" or "silver")
                {
                    subTextColor = Color.FromArgb(200, textColor.R, textColor.G, textColor.B);
                }
                else
                {
                    subTextColor = Color.FromArgb(220, 185, 205, 235);
                }
                UpdateResourceBrush(appResources, "ThemeSubTextBrush", subTextColor);

                // Button Hover & Inactive Brushes
                var hoverColor = (Color)ColorConverter.ConvertFromString(theme.ButtonHoverHex);
                var hoverFg = (Color)ColorConverter.ConvertFromString(theme.ButtonHoverFgHex);
                var inactiveBtnColor = (Color)ColorConverter.ConvertFromString(theme.InactiveButtonHex);
                UpdateResourceBrush(appResources, "ThemeButtonHoverBrush", hoverColor);
                UpdateResourceBrush(appResources, "ThemeButtonHoverFgBrush", hoverFg);
                UpdateResourceBrush(appResources, "ThemeInactiveButtonBrush", inactiveBtnColor);

                // Standard Button Background Brush (Semi-transparent frost/dark fill)
                Color btnBgColor;
                if (theme.Key is "rosegold" or "champagne" or "silver" or "lakeblue")
                {
                    btnBgColor = Color.FromArgb(0x18, 0x00, 0x00, 0x00);
                }
                else if (theme.Key == "pureblack")
                {
                    btnBgColor = Color.FromArgb(0x24, 0xFF, 0xFF, 0xFF); // 14% 晶莹白霜半透微光填充
                }
                else
                {
                    btnBgColor = Color.FromArgb(0x1F, 0xFF, 0xFF, 0xFF);
                }
                UpdateResourceBrush(appResources, "ThemeButtonBgBrush", btnBgColor);

                // Selected Item Background Brush
                byte opacity = (byte)(theme.Key is "rosegold" or "silver" or "champagne" ? 0x55 : 0x40);
                var selectedItemBgColor = Color.FromArgb(opacity, accentColor.R, accentColor.G, accentColor.B);
                UpdateResourceBrush(appResources, "ThemeSelectedItemBgBrush", selectedItemBgColor);

                // Slider Progress & Track Brushes
                string progressHex = string.IsNullOrEmpty(theme.SliderProgressHex) ? theme.AccentHex : theme.SliderProgressHex;
                string trackHex = string.IsNullOrEmpty(theme.SliderTrackHex) ? "#40FFFFFF" : theme.SliderTrackHex;

                var progressColor = (Color)ColorConverter.ConvertFromString(progressHex);
                var trackColor = (Color)ColorConverter.ConvertFromString(trackHex);

                UpdateResourceBrush(appResources, "ThemeSliderProgressBrush", progressColor);
                UpdateResourceBrush(appResources, "ThemeSliderTrackBrush", trackColor);

                // ScrollBar Thumb Brushes (Default theme fallback)
                UpdateResourceBrush(appResources, "ThemeScrollBarThumbBrush", Color.FromArgb(0x70, textColor.R, textColor.G, textColor.B));
                UpdateResourceBrush(appResources, "ThemeScrollBarThumbHoverBrush", Color.FromArgb(0xC0, textColor.R, textColor.G, textColor.B));
                UpdateResourceBrush(appResources, "ThemeScrollBarThumbPressedBrush", Color.FromArgb(0xFF, textColor.R, textColor.G, textColor.B));

                // Primary Action Button Gradient
                var primaryGradient = new LinearGradientBrush
                {
                    StartPoint = new Point(0, 0),
                    EndPoint = new Point(1, 0)
                };
                primaryGradient.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString(theme.PrimaryBtnStartHex), 0.0));
                primaryGradient.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString(theme.PrimaryBtnEndHex), 1.0));
                appResources["ThemePrimaryBtnBrush"] = primaryGradient;

                // Flare Beam Gradient (Default cyan-gold)
                var flareGradient = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
                flareGradient.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0, 0, 0), 0.0));
                flareGradient.GradientStops.Add(new GradientStop(Color.FromArgb(120, accentColor.R, accentColor.G, accentColor.B), 0.15));
                flareGradient.GradientStops.Add(new GradientStop(Color.FromArgb(255, 255, 255, 255), 0.5));
                flareGradient.GradientStops.Add(new GradientStop(Color.FromArgb(120, accentColor.R, accentColor.G, accentColor.B), 0.85));
                flareGradient.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0, 0, 0), 1.0));
                appResources["ThemeFlareBeamBrush"] = flareGradient;

                // ══ 3D Frosted Glass Open Button Brushes (3D 磨砂玻璃质感打开按钮专属画刷) ══
                var openBtnBg = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
                Color openBgTop, openBgBottom;
                if (theme.Key is "rosegold" or "champagne" or "silver")
                {
                    openBgTop = Color.FromArgb(0x40, windowBgColor.R, windowBgColor.G, windowBgColor.B);
                    openBgBottom = Color.FromArgb(0x65, (byte)Math.Max(0, windowBgColor.R - 20), (byte)Math.Max(0, windowBgColor.G - 20), (byte)Math.Max(0, windowBgColor.B - 20));
                }
                else if (theme.Key == "pureblack")
                {
                    openBgTop = Color.FromArgb(0x40, 0x24, 0x24, 0x2A);
                    openBgBottom = Color.FromArgb(0x65, 0x14, 0x14, 0x18);
                }
                else
                {
                    openBgTop = Color.FromArgb(0x45, (byte)Math.Min(255, windowBgColor.R + 25), (byte)Math.Min(255, windowBgColor.G + 25), (byte)Math.Min(255, windowBgColor.B + 25));
                    openBgBottom = Color.FromArgb(0x65, windowBgColor.R, windowBgColor.G, windowBgColor.B);
                }
                openBtnBg.GradientStops.Add(new GradientStop(openBgTop, 0.0));
                openBtnBg.GradientStops.Add(new GradientStop(openBgBottom, 1.0));
                openBtnBg.Freeze();
                appResources["ThemeOpenBtnBgBrush"] = openBtnBg;

                // 2. Crystal Border Gradient (Light from top)
                var openBtnBorder = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
                openBtnBorder.GradientStops.Add(new GradientStop(Color.FromArgb(0xB5, accentColor.R, accentColor.G, accentColor.B), 0.0));
                openBtnBorder.GradientStops.Add(new GradientStop(Color.FromArgb(0x50, accentColor.R, accentColor.G, accentColor.B), 1.0));
                openBtnBorder.Freeze();
                appResources["ThemeOpenBtnBorderBrush"] = openBtnBorder;

                // 3. Top Specular Sheen Highlight (Frosted glass reflection)
                var openBtnSheen = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
                openBtnSheen.GradientStops.Add(new GradientStop(Color.FromArgb(0x65, 255, 255, 255), 0.0));
                openBtnSheen.GradientStops.Add(new GradientStop(Color.FromArgb(0x20, accentColor.R, accentColor.G, accentColor.B), 0.5));
                openBtnSheen.GradientStops.Add(new GradientStop(Color.FromArgb(0x00, 0, 0, 0), 1.0));
                openBtnSheen.Freeze();
                appResources["ThemeOpenBtnSheenBrush"] = openBtnSheen;

                // 4. Hover background gradient
                var openBtnHover = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
                openBtnHover.GradientStops.Add(new GradientStop(Color.FromArgb(0x75, accentColor.R, accentColor.G, accentColor.B), 0.0));
                openBtnHover.GradientStops.Add(new GradientStop(Color.FromArgb(0x40, accentColor.R, accentColor.G, accentColor.B), 1.0));
                openBtnHover.Freeze();
                appResources["ThemeOpenBtnHoverBgBrush"] = openBtnHover;

                // 5. Glow color
                appResources["ThemeOpenBtnGlowColor"] = accentColor;

                // ══ Audio Floating Banner & Vinyl Disc Theme Brushes (Per-theme custom colors) ══
                string audioBannerBgHex = !string.IsNullOrEmpty(theme.AudioBannerBgHex)
                    ? theme.AudioBannerBgHex
                    : "#3808101C";
                string audioBannerBorderHex = !string.IsNullOrEmpty(theme.AudioBannerBorderHex)
                    ? theme.AudioBannerBorderHex
                    : (!string.IsNullOrEmpty(theme.BorderHex) ? theme.BorderHex : theme.AccentHex);
                string audioDiscBorderHex = !string.IsNullOrEmpty(theme.AudioDiscBorderHex)
                    ? theme.AudioDiscBorderHex
                    : (!string.IsNullOrEmpty(theme.BorderHex) ? theme.BorderHex : theme.AccentHex);
                string audioAccentHex = !string.IsNullOrEmpty(theme.AudioAccentHex)
                    ? theme.AudioAccentHex
                    : theme.AccentHex;
                string audioTextHex = !string.IsNullOrEmpty(theme.AudioTextHex)
                    ? theme.AudioTextHex
                    : "#FFFFFF";
                string audioSubTextHex = !string.IsNullOrEmpty(theme.AudioSubTextHex)
                    ? theme.AudioSubTextHex
                    : "#E0E8F5";

                UpdateResourceBrush(appResources, "ThemeAudioBannerBgBrush", (Color)ColorConverter.ConvertFromString(audioBannerBgHex));
                UpdateResourceBrush(appResources, "ThemeAudioBannerBorderBrush", (Color)ColorConverter.ConvertFromString(audioBannerBorderHex));
                UpdateResourceBrush(appResources, "ThemeAudioDiscBorderBrush", (Color)ColorConverter.ConvertFromString(audioDiscBorderHex));
                UpdateResourceBrush(appResources, "ThemeAudioAccentBrush", (Color)ColorConverter.ConvertFromString(audioAccentHex));
                UpdateResourceBrush(appResources, "ThemeAudioTextBrush", (Color)ColorConverter.ConvertFromString(audioTextHex));
                UpdateResourceBrush(appResources, "ThemeAudioSubTextBrush", (Color)ColorConverter.ConvertFromString(audioSubTextHex));

                // Tokens
                appResources["ThemeButtonCornerRadius"] = new CornerRadius(theme.ButtonCornerRadius > 0 ? theme.ButtonCornerRadius : 6.0);
                appResources["ThemePanelCornerRadius"] = new CornerRadius(theme.PanelCornerRadius > 0 ? theme.PanelCornerRadius : 8.0);
                appResources["ThemeWindowCornerRadius"] = new CornerRadius(theme.WindowCornerRadius > 0 ? theme.WindowCornerRadius : 10.0);
                appResources["ThemeBorderThickness"] = new Thickness(theme.BorderThickness > 0 ? theme.BorderThickness : 1.0);
                appResources["ThemeButtonBorderThickness"] = new Thickness(theme.ButtonBorderThickness > 0 ? theme.ButtonBorderThickness : 1.0);

                if (!string.IsNullOrWhiteSpace(theme.FontFamily))
                {
                    appResources["ThemeFontFamily"] = new FontFamily(theme.FontFamily);
                }
                if (theme.FontSizeBase > 0)
                {
                    appResources["ThemeFontSizeBase"] = theme.FontSizeBase;
                }
            }
            catch { }
        }

        #region ══ SKIN PACKAGE LOADING & EXPORT API ══

        public string GetSkinsDirectory()
        {
            string localDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "skin");
            if (!Directory.Exists(localDir))
            {
                try { Directory.CreateDirectory(localDir); } catch { }
            }
            return localDir;
        }

        public void ScanAndLoadCustomSkins()
        {
            Skins.Clear();

            var searchDirs = new List<string>
            {
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "skin"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "skins"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "skin"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "skins"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "skin"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "skin"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AniPlayer", "skins"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AniPlayer", "skin"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AnniPlayer", "skins"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AnniPlayer", "skin")
            };

            var jsonOptions = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                ReadCommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true
            };

            foreach (var dir in searchDirs)
            {
                try
                {
                    if (!Directory.Exists(dir)) continue;

                    // 1. Scan subdirectories (e.g. skin/default, skin/teal_gold)
                    var subDirs = Directory.GetDirectories(dir);
                    foreach (var sub in subDirs)
                    {
                        LoadSkinFromFolder(sub, jsonOptions);
                    }

                    // 2. Scan single-file packages (.pkg / .zip / .annisp)
                    var pkgFiles = Directory.GetFiles(dir, "*.*", SearchOption.TopDirectoryOnly)
                        .Where(f => f.EndsWith(".pkg", StringComparison.OrdinalIgnoreCase) 
                                 || f.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
                                 || f.EndsWith(".annisp", StringComparison.OrdinalIgnoreCase));

                    foreach (var pkg in pkgFiles)
                    {
                        LoadExternalSkinPackage(pkg, jsonOptions);
                    }
                }
                catch { }
            }
        }

        /// <summary>
        /// 深度容错探测：递归探测给定目录及其子目录（最多探测 3 层），搜寻所有包含 skin.json 或 skin.xaml 的有效皮肤根目录。
        /// 完美支持压缩包内包含多层嵌套文件夹、皮肤合集子文件夹或 GitHub 打包发布格式。
        /// </summary>
        public static List<string> FindSkinDirectories(string rootDir, int maxDepth = 3)
        {
            var results = new List<string>();
            if (string.IsNullOrWhiteSpace(rootDir) || !Directory.Exists(rootDir))
                return results;

            void Scan(string currentDir, int depth)
            {
                if (depth > maxDepth) return;

                bool hasXaml = File.Exists(Path.Combine(currentDir, "skin.xaml"));
                bool hasJson = File.Exists(Path.Combine(currentDir, "skin.json"));

                if (hasXaml || hasJson)
                {
                    results.Add(currentDir);
                    return; // 当前目录已是有效皮肤根目录，无需再向自身深层探测
                }

                try
                {
                    var subDirs = Directory.GetDirectories(currentDir);
                    foreach (var sub in subDirs)
                    {
                        string name = Path.GetFileName(sub);
                        // 忽略非皮肤相关的隐藏/系统构建文件夹
                        if (name.StartsWith(".") ||
                            name.Equals("__MACOSX", StringComparison.OrdinalIgnoreCase) ||
                            name.Equals("bin", StringComparison.OrdinalIgnoreCase) ||
                            name.Equals("obj", StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }
                        Scan(sub, depth + 1);
                    }
                }
                catch { }
            }

            Scan(rootDir, 0);
            return results;
        }

        private void LoadSkinFromFolder(string folderPath, JsonSerializerOptions jsonOptions)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath)) return;

                // 深度探测该目录下所有有效皮肤子目录（支持普通单皮肤目录与多皮肤合集目录）
                var skinDirs = FindSkinDirectories(folderPath, maxDepth: 3);
                foreach (var skinDir in skinDirs)
                {
                    LoadSingleSkinDirectory(skinDir, jsonOptions);
                }
            }
            catch { }
        }

        private SkinItem? LoadSingleSkinDirectory(string folderPath, JsonSerializerOptions jsonOptions, string? fallbackKey = null)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath)) return null;

                string folderName = Path.GetFileName(folderPath);
                string skinKey = !string.IsNullOrEmpty(fallbackKey) ? fallbackKey : folderName.ToLowerInvariant().Replace(" ", "_");

                // Check skin.xaml
                string xamlPath = Path.Combine(folderPath, "skin.xaml");
                bool hasXaml = File.Exists(xamlPath);

                // Check skin.json
                string jsonPath = Path.Combine(folderPath, "skin.json");
                bool hasJson = File.Exists(jsonPath);

                if (!hasXaml && !hasJson) return null;

                var skinItem = new SkinItem
                {
                    Key = skinKey,
                    NameZh = $"🎨 {folderName}",
                    NameEn = $"🎨 {folderName}",
                    SkinFolderPath = folderPath,
                    HasXaml = hasXaml,
                    SkinXamlPath = hasXaml ? xamlPath : null
                };

                if (hasJson)
                {
                    try
                    {
                        string json = File.ReadAllText(jsonPath);
                        var theme = JsonSerializer.Deserialize<ThemeItem>(json, jsonOptions);
                        if (theme != null)
                        {
                            if (!string.IsNullOrWhiteSpace(theme.NameZh)) skinItem.NameZh = theme.NameZh;
                            if (!string.IsNullOrWhiteSpace(theme.NameEn)) skinItem.NameEn = theme.NameEn;
                            if (!string.IsNullOrWhiteSpace(theme.Key)) skinItem.Key = theme.Key;
                            skinItem.ThemeConfig = theme;
                            theme.SkinFolderPath = folderPath;
                            theme.IsCustomSkin = true;
                        }
                        else
                        {
                            skinItem.HasJsonParseError = true;
                            skinItem.LastErrorMessage = "skin.json 解析为空或结构不符合规范。";
                        }
                    }
                    catch (Exception ex)
                    {
                        skinItem.HasJsonParseError = true;
                        skinItem.LastErrorMessage = $"skin.json 语法或解析错误: {ex.Message}";
                    }
                }

                // 避免 Key 冲突时导致无法加载
                if (Skins.TryGetValue(skinItem.Key, out var existing))
                {
                    if (existing.SkinFolderPath != folderPath)
                    {
                        skinItem.Key = $"{skinItem.Key}_{folderName.ToLowerInvariant()}";
                    }
                }

                Skins[skinItem.Key] = skinItem;
                return skinItem;
            }
            catch { }
            return null;
        }

        public bool LoadExternalSkinPackage(string pkgFilePath, JsonSerializerOptions? jsonOptions = null)
        {
            try
            {
                if (!File.Exists(pkgFilePath)) return false;

                jsonOptions ??= new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    ReadCommentHandling = JsonCommentHandling.Skip,
                    AllowTrailingCommas = true
                };

                string pkgKey = Path.GetFileNameWithoutExtension(pkgFilePath).ToLowerInvariant().Replace(" ", "_");
                string cacheDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AniPlayer", "skins_cache", pkgKey);

                bool needsExtract = !Directory.Exists(cacheDir) ||
                                     File.GetLastWriteTimeUtc(pkgFilePath) > Directory.GetLastWriteTimeUtc(cacheDir);

                if (needsExtract)
                {
                    try
                    {
                        if (Directory.Exists(cacheDir))
                        {
                            Directory.Delete(cacheDir, true);
                        }
                        Directory.CreateDirectory(cacheDir);
                        ZipFile.ExtractToDirectory(pkgFilePath, cacheDir, true);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Extracting skin package error: {ex.Message}");
                    }
                }

                if (Directory.Exists(cacheDir))
                {
                    // 深度容错扫描：自动查探压缩包解压后的多层子目录
                    var skinDirs = FindSkinDirectories(cacheDir, maxDepth: 3);
                    bool loadedAny = false;

                    foreach (var skinDir in skinDirs)
                    {
                        var loaded = LoadSingleSkinDirectory(skinDir, jsonOptions, pkgKey);
                        if (loaded != null)
                        {
                            loaded.SkinPackagePath = Path.GetFullPath(pkgFilePath);
                            if (string.IsNullOrWhiteSpace(loaded.NameZh) || loaded.NameZh.StartsWith("🎨"))
                            {
                                string displayName = Path.GetFileName(skinDir);
                                if (skinDirs.Count == 1)
                                {
                                    displayName = Path.GetFileNameWithoutExtension(pkgFilePath);
                                }
                                loaded.NameZh = $"📦 {displayName}";
                                loaded.NameEn = $"📦 {displayName}";
                            }
                            loadedAny = true;
                        }
                    }

                    return loadedAny;
                }
            }
            catch { }
            return false;
        }

        public bool ExportSkinXaml(string targetFilePath, string skinKey = "")
        {
            try
            {
                if (string.IsNullOrWhiteSpace(targetFilePath)) return false;
                string? targetDir = Path.GetDirectoryName(targetFilePath);
                if (string.IsNullOrEmpty(targetDir)) targetDir = AppDomain.CurrentDomain.BaseDirectory;
                if (!Directory.Exists(targetDir)) Directory.CreateDirectory(targetDir);

                string fileName = Path.GetFileName(targetFilePath);
                string xamlDest = Path.Combine(targetDir, fileName.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase) ? fileName : "skin.xaml");
                string jsonDest = Path.Combine(targetDir, "skin.json");

                // 1. If a specific skin is currently active/selected, try to export its real skin.xaml and skin.json
                if (!string.IsNullOrWhiteSpace(skinKey) && Skins.TryGetValue(skinKey, out var skinItem))
                {
                    string srcXaml = !string.IsNullOrEmpty(skinItem.SkinXamlPath) && File.Exists(skinItem.SkinXamlPath)
                        ? skinItem.SkinXamlPath
                        : (!string.IsNullOrEmpty(skinItem.SkinFolderPath) ? Path.Combine(skinItem.SkinFolderPath, "skin.xaml") : "");

                    if (!string.IsNullOrEmpty(srcXaml) && File.Exists(srcXaml))
                    {
                        File.Copy(srcXaml, xamlDest, true);
                    }
                    else
                    {
                        File.WriteAllText(xamlDest, GetDefaultSkinXamlTemplate(), System.Text.Encoding.UTF8);
                    }

                    string srcJson = !string.IsNullOrEmpty(skinItem.SkinFolderPath) ? Path.Combine(skinItem.SkinFolderPath, "skin.json") : "";
                    if (!string.IsNullOrEmpty(srcJson) && File.Exists(srcJson))
                    {
                        File.Copy(srcJson, jsonDest, true);
                    }
                    else
                    {
                        File.WriteAllText(jsonDest, GetDefaultSkinJsonTemplate(), System.Text.Encoding.UTF8);
                    }

                    return true;
                }

                // 2. Try to find any existing valid skin.xaml and skin.json in local skins directory as baseline
                string skinsDir = GetSkinsDirectory();
                if (Directory.Exists(skinsDir))
                {
                    var existingXaml = Directory.GetFiles(skinsDir, "skin.xaml", SearchOption.AllDirectories).FirstOrDefault();
                    if (!string.IsNullOrEmpty(existingXaml) && File.Exists(existingXaml))
                    {
                        File.Copy(existingXaml, xamlDest, true);
                    }
                    else
                    {
                        File.WriteAllText(xamlDest, GetDefaultSkinXamlTemplate(), System.Text.Encoding.UTF8);
                    }

                    var existingJson = Directory.GetFiles(skinsDir, "skin.json", SearchOption.AllDirectories).FirstOrDefault();
                    if (!string.IsNullOrEmpty(existingJson) && File.Exists(existingJson))
                    {
                        File.Copy(existingJson, jsonDest, true);
                    }
                    else
                    {
                        File.WriteAllText(jsonDest, GetDefaultSkinJsonTemplate(), System.Text.Encoding.UTF8);
                    }

                    return true;
                }

                // 3. Fallback: generate complete, well-documented standard skin.xaml and skin.json templates
                File.WriteAllText(xamlDest, GetDefaultSkinXamlTemplate(), System.Text.Encoding.UTF8);
                File.WriteAllText(jsonDest, GetDefaultSkinJsonTemplate(), System.Text.Encoding.UTF8);
                return true;
            }
            catch { return false; }
        }

        public string GetDefaultSkinXamlTemplate()
        {
            return @"<ResourceDictionary xmlns=""http://schemas.microsoft.com/winfx/2006/xaml/presentation""
                    xmlns:x=""http://schemas.microsoft.com/winfx/2006/xaml""
                    xmlns:sys=""clr-namespace:System;assembly=mscorlib"">

    <!-- ============================================================================
         🎨 AniPlayer 自定义皮肤样式表模板 (skin.xaml) / Custom Skin Stylesheet Template
         ============================================================================ -->
    <!-- 说明 / Notes:
         1. 本文件使用 UTF-8 BOM 编码保存，严格与系统内置主题物理隔离。
            (Saved in UTF-8 BOM encoding. Strictly isolated from built-in themes.)
         2. 修改后无需重启程序，在设置面板重新点击该皮肤即可毫秒级热重载生效。
            (Supports instant hot-reloading upon selecting the skin in Settings.) -->

    <!-- ThemeAccentBrush: 全局主强调色 (按钮高亮、进度条、高光图标) / Primary accent color (Buttons, seek progress, icons) -->
    <SolidColorBrush x:Key=""ThemeAccentBrush"" Color=""#00F0FF""/>

    <!-- ThemeTextBrush: 一级主文本与图标前景色 / Primary text & icon foreground -->
    <SolidColorBrush x:Key=""ThemeTextBrush"" Color=""#F0F8FF""/>

    <!-- ThemeSubTextBrush: 二级描述与时间戳文本色 / Secondary description & timestamp text -->
    <SolidColorBrush x:Key=""ThemeSubTextBrush"" Color=""#B0C4DE""/>

    <!-- ThemeBorderBrush: 全局通用边框画刷 / Global border brush -->
    <SolidColorBrush x:Key=""ThemeBorderBrush"" Color=""#6000B2FF""/>

    <!-- ThemeMenuSeparatorBrush: 右键菜单分割线 / Context menu separator divider -->
    <SolidColorBrush x:Key=""ThemeMenuSeparatorBrush"" Color=""#4000F0FF""/>

    <!-- ThemeButtonHoverBrush: 按钮悬停半透明背景 / Button hover semi-transparent background -->
    <SolidColorBrush x:Key=""ThemeButtonHoverBrush"" Color=""#3000F0FF""/>

    <!-- ThemeButtonHoverFgBrush: 按钮悬停前景色 / Button hover foreground text -->
    <SolidColorBrush x:Key=""ThemeButtonHoverFgBrush"" Color=""#FFFFFF""/>

    <!-- ThemeSelectedItemBgBrush: 列表选中项高亮背景 / Playlist selected item highlight background -->
    <SolidColorBrush x:Key=""ThemeSelectedItemBgBrush"" Color=""#4000F0FF""/>

    <!-- ThemeSliderProgressBrush: 播放进度条已播颜色 / Seek slider played progress track -->
    <SolidColorBrush x:Key=""ThemeSliderProgressBrush"" Color=""#00F0FF""/>

    <!-- ThemeSliderTrackBrush: 播放进度条底槽颜色 / Seek slider unplayed track background -->
    <SolidColorBrush x:Key=""ThemeSliderTrackBrush"" Color=""#400D2B52""/>

    <!-- ══ 滚动条画刷 / ScrollBar Brushes ══ -->
    <!-- ThemeScrollBarThumbBrush: 滚动条滑块默认底色 / ScrollBar thumb default brush -->
    <SolidColorBrush x:Key=""ThemeScrollBarThumbBrush"" Color=""#8000F0FF""/>
    <!-- ThemeScrollBarThumbHoverBrush: 滚动条滑块悬停高亮色 / ScrollBar thumb hover brush -->
    <SolidColorBrush x:Key=""ThemeScrollBarThumbHoverBrush"" Color=""#C000F0FF""/>
    <!-- ThemeScrollBarThumbPressedBrush: 滚动条滑块按下高光色 / ScrollBar thumb pressed brush -->
    <SolidColorBrush x:Key=""ThemeScrollBarThumbPressedBrush"" Color=""#FFFFFF""/>

    <!-- ══ 音频悬浮横幅与黑胶专属画刷 / Audio Overlay Banner & Vinyl Disc Brushes ══ -->
    <!-- ThemeAudioBannerBgBrush: 音频浮层背景底色 / Audio banner background base brush -->
    <SolidColorBrush x:Key=""ThemeAudioBannerBgBrush"" Color=""#3806152D""/>
    <!-- ThemeAudioBannerBorderBrush: 音频浮层外边框颜色 / Audio banner outer border brush -->
    <SolidColorBrush x:Key=""ThemeAudioBannerBorderBrush"" Color=""#A000F0FF""/>
    <!-- ThemeAudioDiscBorderBrush: 旋转黑胶唱片高光边缘圈 / Vinyl disc outer edge highlight ring -->
    <SolidColorBrush x:Key=""ThemeAudioDiscBorderBrush"" Color=""#8000F0FF""/>
    <!-- ThemeAudioAccentBrush: 音频模式强调点缀色 / Audio mode accent glow color -->
    <SolidColorBrush x:Key=""ThemeAudioAccentBrush"" Color=""#00F0FF""/>
    <!-- ThemeAudioTextBrush: 歌曲标题主文本色 / Song title primary text color -->
    <SolidColorBrush x:Key=""ThemeAudioTextBrush"" Color=""#FFFFFF""/>
    <!-- ThemeAudioSubTextBrush: 歌手与专辑副文本色 / Artist & album secondary text color -->
    <SolidColorBrush x:Key=""ThemeAudioSubTextBrush"" Color=""#C0E0FF""/>

    <!-- ══ 打开文件 3D 质感按钮专属画刷 / 3D Open Button Material Brushes ══ -->
    <!-- ThemeOpenBtnBgBrush: 打开文件按钮背景立体渐变 / Open button 3D background gradient -->
    <LinearGradientBrush x:Key=""ThemeOpenBtnBgBrush"" StartPoint=""0,0"" EndPoint=""0,1"">
        <GradientStop Color=""#45103565"" Offset=""0.0""/>
        <GradientStop Color=""#65082147"" Offset=""1.0""/>
    </LinearGradientBrush>
    <!-- ThemeOpenBtnBorderBrush: 打开文件按钮双色高光边框 / Open button dual-tone specular border -->
    <LinearGradientBrush x:Key=""ThemeOpenBtnBorderBrush"" StartPoint=""0,0"" EndPoint=""0,1"">
        <GradientStop Color=""#B500F0FF"" Offset=""0.0""/>
        <GradientStop Color=""#5000F0FF"" Offset=""1.0""/>
    </LinearGradientBrush>
    <!-- ThemeOpenBtnSheenBrush: 打开文件按钮顶部高光反射层 / Open button frosted glass sheen highlight -->
    <LinearGradientBrush x:Key=""ThemeOpenBtnSheenBrush"" StartPoint=""0,0"" EndPoint=""0,1"">
        <GradientStop Color=""#65FFFFFF"" Offset=""0.0""/>
        <GradientStop Color=""#2000F0FF"" Offset=""0.5""/>
        <GradientStop Color=""#00000000"" Offset=""1.0""/>
    </LinearGradientBrush>
    <!-- ThemeOpenBtnHoverBgBrush: 打开文件按钮悬停背景渐变 / Open button hover background gradient -->
    <LinearGradientBrush x:Key=""ThemeOpenBtnHoverBgBrush"" StartPoint=""0,0"" EndPoint=""0,1"">
        <GradientStop Color=""#7500F0FF"" Offset=""0.0""/>
        <GradientStop Color=""#4000F0FF"" Offset=""1.0""/>
    </LinearGradientBrush>
    <!-- ThemeOpenBtnGlowColor: 打开文件按钮外发光色 / Open button outer glow color -->
    <Color x:Key=""ThemeOpenBtnGlowColor"">#00F0FF</Color>

    <!-- ══ 几何尺寸与圆角 / Geometry, Corner Radius & Thickness ══ -->
    <!-- ThemeButtonCornerRadius: 按钮圆角半径 / Button corner radius in px -->
    <CornerRadius x:Key=""ThemeButtonCornerRadius"">6</CornerRadius>
    <!-- ThemePanelCornerRadius: 浮层面板圆角半径 / Popup panel corner radius in px -->
    <CornerRadius x:Key=""ThemePanelCornerRadius"">8</CornerRadius>
    <!-- ThemeWindowCornerRadius: 主窗口外框圆角半径 / Main window corner radius in px -->
    <CornerRadius x:Key=""ThemeWindowCornerRadius"">10</CornerRadius>
    <!-- ThemeBorderThickness: 窗口与面板外边框粗细 / Window & panel outer border thickness in px -->
    <Thickness x:Key=""ThemeBorderThickness"">1.5</Thickness>
    <!-- ThemeButtonBorderThickness: 按钮边框粗细 / Button border thickness in px -->
    <Thickness x:Key=""ThemeButtonBorderThickness"">1.0</Thickness>

    <!-- ══ 待机参数 / Idle Video & Rendering Parameters ══ -->
    <!-- ThemeIdleVideoSpeed: 待机视频播放倍速 / Idle video playback speed (0.1 ~ 10.0) -->
    <sys:Double x:Key=""ThemeIdleVideoSpeed"">1.0</sys:Double>
    <!-- ThemeIdleVideoBrightness: 待机视频画面亮度增益 / Idle video brightness (-100 ~ 100) -->
    <sys:Double x:Key=""ThemeIdleVideoBrightness"">0.0</sys:Double>
    <!-- ThemeIdleVideoLoop: 待机视频是否循环 / Whether idle video loops -->
    <sys:Boolean x:Key=""ThemeIdleVideoLoop"">True</sys:Boolean>
    <!-- ThemePanelTintOpacity: 复合材质半透明压暗遮罩透明度 / Composite material tint opacity (0.0~1.0) -->
    <sys:Double x:Key=""ThemePanelTintOpacity"">0.25</sys:Double>

</ResourceDictionary>";
        }

        public string GetDefaultSkinJsonTemplate()
        {
            return @"{
  // ============================================================================
  // 🎨 AniPlayer 自定义皮肤元数据与参数配置表 (skin.json)
  // ============================================================================
  // 说明 / Notes:
  // 1. 本文件使用 UTF-8 BOM 编码保存，支持 // 单行注释与 /* */ 多行注释。
  // 2. 皮肤与系统内置主题严格物理隔离，此处的修改仅针对当前皮肤生效，零全局污染。
  // 3. 修改后无需重启程序，在设置面板重新点击该皮肤即可毫秒级热重载生效。

  // ── 基础元信息 (Basic Metadata) ──────────────────────────────────────────
  ""key"": ""custom_skin"",
  ""nameZh"": ""✨ 我的自定义皮肤"",
  ""nameEn"": ""✨ My Custom Skin"",
  ""author"": ""Skin Creator"",
  ""version"": ""1.0.0"",
  ""description"": ""A custom tailored skin package with premium visual styling for AniPlayer"",

  // ── 核心调色板 (Core Color Palette) ──────────────────────────────────────
  ""accentHex"": ""#00F0FF"",
  ""textHex"": ""#F0F8FF"",
  ""subTextHex"": ""#B0C4DE"",
  ""windowBgHex"": ""#06152D"",
  ""borderHex"": ""#6000B2FF"",

  // ── 界面区域与面板配色 (Surface & Panel Color Scheme) ────────────────────
  ""titleBarStartHex"": ""#F00F4887"",
  ""titleBarEndHex"": ""#F20A2C5C"",
  ""controlBarStartHex"": ""#F20A2C5C"",
  ""controlBarEndHex"": ""#F5082147"",
  ""menuBgHex"": ""#0A2548"",
  ""drawerBgHex"": ""#0C2B54"",
  ""panelBgHex"": ""#0A2548"",
  ""panel_tint_opacity"": 0.25,
  ""menuSeparatorHex"": ""#6000F0FF"",

  // ── 交互控件与按钮状态配色 (Interactive Buttons & Hover States) ───────────
  ""buttonHoverHex"": ""#28528C"",
  ""buttonHoverFgHex"": ""#FFFFFF"",
  ""inactiveButtonHex"": ""#D0E4FF"",
  ""primaryBtnStartHex"": ""#0072FF"",
  ""primaryBtnEndHex"": ""#00F2FE"",

  // ── 进度条与电影级光晕 (Seek Slider & Cinema Flare Beam) ──────────────────
  ""sliderProgressHex"": ""#00F0FF"",
  ""sliderTrackHex"": ""#400D2B52"",
  ""sliderThumbHex"": ""#FFFFFF"",
  ""flareBeamColor"": ""#00F0FF"",

  // ── 几何圆角与边框粗细 (Geometry & Border Dimensions) ────────────────────
  ""buttonCornerRadius"": 6.0,
  ""panelCornerRadius"": 8.0,
  ""windowCornerRadius"": 10.0,
  ""borderThickness"": 1.5,
  ""buttonBorderThickness"": 1.0,

  // ── 排版字体与基础字号 (Typography & Font Sizing) ────────────────────────
  ""fontFamily"": ""Microsoft YaHei, Segoe UI, sans-serif"",
  ""fontSizeBase"": 14.0,

  // ── 背景贴图与母版纹理 (Background Texture Assets) ────────────────────────
  // 主题 16:9 母版纹理贴图文件名 (放入当前皮肤文件夹下)
  ""themeBg"": ""bg.jpg"",
  // 静态待机开机壁纸文件名
  ""idleBg"": ""bg.jpg"",

  // ── 背景音乐 (BGM Audio Engine) ──────────────────────────────────────────
  // 专属背景音乐音频文件名 (放入当前皮肤文件夹下)
  ""bgm_audio"": """",
  ""bgm_volume"": 70,
  ""bgm_speed"": 1.0,
  ""bgm_loop"": true,
  ""bgm_auto_play_on_idle"": true,
  ""bgm_pause_on_media_playback"": true,

  // ── 待机动态视频轮播引擎 (Idle Video Carousel Engine) ────────────────────
  ""idle_media_loop"": true,
  ""idle_slideshow_interval_sec"": 5.0,
  ""idle_slideshow_speed"": 1.0,
  ""idle_videos"": [],
  ""idle_video_speed"": 1.0,
  ""idle_video_brightness"": 0.0,
  ""idle_video_loop"": true,

  // ── 待机标语与文字排版 (Idle Screen Hint Overlay Typography) ─────────────
  ""idle_hint_font_family"": ""Microsoft YaHei, Segoe UI"",
  ""idle_hint_title_size"": 24.0,
  ""idle_hint_subtitle_size"": 15.0,
  ""idle_hint_title_hex"": ""#00F0FF"",
  ""idle_hint_subtitle_hex"": ""#B0C4DE""
}";
        }

        public bool ImportSkinPackageFromFolder(string sourceFolderPath, out string importedSkinKey, out string errorMsg)
        {
            importedSkinKey = "";
            errorMsg = "";

            try
            {
                if (string.IsNullOrWhiteSpace(sourceFolderPath) || !Directory.Exists(sourceFolderPath))
                {
                    errorMsg = "指定的皮肤目录不存在。";
                    return false;
                }

                bool hasXaml = File.Exists(Path.Combine(sourceFolderPath, "skin.xaml"));
                bool hasJson = File.Exists(Path.Combine(sourceFolderPath, "skin.json"));
                if (!hasXaml && !hasJson)
                {
                    errorMsg = "所选文件夹未包含有效的 skin.xaml 或 skin.json 样式定义文件，无法识别为皮肤包。";
                    return false;
                }

                string skinKey = Path.GetFileName(sourceFolderPath).ToLowerInvariant().Replace(" ", "_");
                string targetSkinDir = GetSkinsDirectory();
                string targetFolder = Path.Combine(targetSkinDir, skinKey);

                if (!Directory.Exists(targetFolder)) Directory.CreateDirectory(targetFolder);

                // Copy all files into targetFolder
                foreach (var file in Directory.GetFiles(sourceFolderPath, "*.*", SearchOption.AllDirectories))
                {
                    string rel = Path.GetRelativePath(sourceFolderPath, file);
                    string dest = Path.Combine(targetFolder, rel);
                    Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                    File.Copy(file, dest, true);
                }

                ScanAndLoadCustomSkins();
                importedSkinKey = skinKey;
                ActiveSkinKey = skinKey;
                return true;
            }
            catch (Exception ex)
            {
                errorMsg = $"导入皮肤时发生异常: {ex.Message}";
                return false;
            }
        }

        public bool ImportSkinPackageFromFile(string pkgFilePath, out string importedSkinKey, out string errorMsg)
        {
            importedSkinKey = "";
            errorMsg = "";

            try
            {
                if (string.IsNullOrWhiteSpace(pkgFilePath) || !File.Exists(pkgFilePath))
                {
                    errorMsg = "指定的皮肤包文件不存在。";
                    return false;
                }

                string skinName = Path.GetFileNameWithoutExtension(pkgFilePath).ToLowerInvariant().Replace(" ", "_");
                string ext = Path.GetExtension(pkgFilePath);
                if (string.IsNullOrEmpty(ext)) ext = ".pkg";
                string targetSkinDir = GetSkinsDirectory();
                string targetPkgPath = Path.Combine(targetSkinDir, $"{skinName}{ext}");

                if (!string.Equals(Path.GetFullPath(pkgFilePath), Path.GetFullPath(targetPkgPath), StringComparison.OrdinalIgnoreCase))
                {
                    File.Copy(pkgFilePath, targetPkgPath, true);
                }

                ScanAndLoadCustomSkins();
                importedSkinKey = skinName;
                ActiveSkinKey = skinName;
                return true;
            }
            catch (Exception ex)
            {
                errorMsg = $"读取皮肤包时发生异常: {ex.Message}";
                return false;
            }
        }

        public void OpenSkinsFolder()
        {
            try
            {
                string localDir = GetSkinsDirectory();
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = localDir,
                    UseShellExecute = true
                });
            }
            catch { }
        }

        #endregion

        private static void UpdateResourceBrush(ResourceDictionary resources, string resourceKey, System.Windows.Media.Color color)
        {
            if (resources[resourceKey] is SolidColorBrush existingBrush && !existingBrush.IsFrozen)
            {
                existingBrush.Color = color;
            }
            else
            {
                resources[resourceKey] = new SolidColorBrush(color);
            }
        }

        private string GetConfigPath()
        {
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AniPlayer");
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, "config.json");
        }

        public void LoadConfig()
        {
            try
            {
                var cfg = SettingsService.Instance.Config;
                if (!string.IsNullOrEmpty(cfg.Theme) && Themes.ContainsKey(cfg.Theme))
                {
                    _currentThemeKey = cfg.Theme;
                }
                _activeSkinKey = string.IsNullOrEmpty(cfg.ActiveSkin) ? "none" : cfg.ActiveSkin;
                
                ApplyActiveSkinOrTheme();
            }
            catch { }
        }

        public void SaveConfig()
        {
            try
            {
                var cfg = SettingsService.Instance.Config;
                cfg.Theme = _currentThemeKey;
                cfg.ActiveSkin = _activeSkinKey;
                SettingsService.Instance.Save();
            }
            catch { }
        }
    }
}
