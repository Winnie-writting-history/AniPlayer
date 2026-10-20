using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.ComponentModel;

namespace AnniPlayer.Services
{
    public class PlaybackProgressItem
    {
        public string FilePath { get; set; } = "";
        public double Position { get; set; } = 0.0;
        public double Duration { get; set; } = 0.0;
        public DateTime LastPlayedTime { get; set; } = DateTime.UtcNow;
    }

    public class SettingsModel
    {
        // 常规
        public bool CloseToTray { get; set; } = false;
        public bool AutoResume { get; set; } = true;
        public bool AlwaysOnTop { get; set; } = false;
        public bool RightEdgeHoverPlaylist { get; set; } = true;
        public bool DoubleEscToExit { get; set; } = true;
        public bool ClearPlaylistOnExit { get; set; } = false;
        
        // 系统运行日志记录开关 (默认 false；仅在手动编辑 config.json 将其设为 true 时记录系统运行日志；致命崩溃日志始终不受此开关限制并无条件记录)
        public bool EnableSystemLog { get; set; } = false;
        public bool EnableDebugLog { get => EnableSystemLog; set => EnableSystemLog = value; }

        public string ScreenshotPath { get; set; } = "";
        public int PlaylistSortMode { get; set; } = -1; // -1: Default(Natural), 0: NameAsc, 1: NameDesc, 2: DateAsc, 3: DateDesc

        // 播放与解码
        public string HwDec { get; set; } = "auto"; // "auto" or "no"
        public int LastVolume { get; set; } = 80;
        public bool ClickToActivateOnly { get; set; } = false; // 失焦时点击画面仅激活窗口（防误触播放/暂停）
        public bool AudioNightMode { get; set; } = false; // 夜间人声增强与防爆音动态范围压缩 (Dynamic Audio Normalizer)
        public bool SaveNetworkStream { get; set; } = false;
        public string NetworkStreamSaveDir { get; set; } = "";
        public string NetworkStreamCacheMode { get; set; } = "auto";
        public string LastPlayedFilePath { get; set; } = "";
        public double LastPlayedPosition { get; set; } = 0.0;
        public double LastPlayedDuration { get; set; } = 0.0;

        // 独立媒体播放进度历史记录（默认最多保存 10 个媒体，滚动淘汰旧记录，与右侧播放列表完全解耦）
        public int MaxPlaybackHistoryCount { get; set; } = 10;
        public List<PlaybackProgressItem> PlaybackHistory { get; set; } = new List<PlaybackProgressItem>();

        // 图片幻灯片与背景音频
        public int ImageDurationSec { get; set; } = 5;
        public int BgmMode { get; set; } = 0; // 0: Auto Same Directory, 1: Manual Specified, 2: Disabled
        public string ManualBgmPath { get; set; } = "";
        public bool BgmSyncPlayPause { get; set; } = true;

        // 画面与去黑边
        public int DefaultCropMode { get; set; } = 0; // 0: Off, 1: Subtitle, 2: Full
        public string DefaultSmartFillMode { get; set; } = "none"; // "none", "normal", "feather"
        public bool DefaultSmartFill { get => DefaultSmartFillMode != "none"; set => DefaultSmartFillMode = value ? (DefaultSmartFillMode == "none" ? "normal" : DefaultSmartFillMode) : "none"; }
        public int BaseBrightness { get; set; } = 0; // -50 to 50

        // 语言与外观
        public string Language { get; set; } = "zh-CN";
        public string Theme { get; set; } = "default";
        public string ActiveSkin { get; set; } = "none";

        // 截图设置
        public bool SaveScreenshotToFile { get; set; } = true;
        public bool CopyScreenshotToClipboard { get; set; } = false;
        public bool SaveScreenshotToMediaDir { get; set; } = false;

        // 右键菜单缩放 (1.0 = 100%, 1.25 = 125%, 1.5 = 150%)
        public double ContextMenuScale { get; set; } = 1.0;

        // 低清视频画质锐化增强与超分辨率 (默认关闭)
        public bool VideoSharpening { get; set; } = false;

        // 画质超分辨率模式: "shader" (原本着色器/FFmpeg), "rtx1" (RTX 档位1 轻量), "rtx2" (RTX 档位2 均衡)
        public string VideoSharpenMode { get; set; } = "shader";

        // 超分辨率放大倍数 (默认 2.0 倍)
        public double VideoSharpenScale { get; set; } = 2.0;

        // 文件关联
        public bool AssociateVideos { get; set; } = false;
        public bool AssociateAudios { get; set; } = false;
        public bool AssociateFolder { get; set; } = false;

        // 快捷键设置
        public HotkeyConfig Hotkeys { get; set; } = new HotkeyConfig();
    }

    public class HotkeyConfig
    {
        public string PlayPause { get; set; } = "Space";
        public string SeekForward { get; set; } = "→";
        public string SeekBackward { get; set; } = "←";
        public string SeekForward30 { get; set; } = "Ctrl+→";
        public string SeekBackward30 { get; set; } = "Ctrl+←";
        public string SpeedUp { get; set; } = "=";
        public string SpeedDown { get; set; } = "-";
        public string VolumeUp { get; set; } = "↑";
        public string VolumeDown { get; set; } = "↓";
        public string ToggleMute { get; set; } = "M";
        public string ToggleFullscreen { get; set; } = "F";
        public string PrevMedia { get; set; } = "PageUp";
        public string NextMedia { get; set; } = "PageDown";
        public string Screenshot { get; set; } = "Ctrl+S";
        public string AbLoop { get; set; } = "Ctrl+A";
        public string TogglePip { get; set; } = "Ctrl+P";
        public string AlwaysOnTop { get; set; } = "Ctrl+R";
        public string OpenFile { get; set; } = "Ctrl+O";
        public string OpenFolder { get; set; } = "Ctrl+Shift+O";
        public string OpenUrl { get; set; } = "Ctrl+U";
        public string TogglePlaylist { get; set; } = "P";
        public string ToggleLibrary { get; set; } = "L";
        public string SmartFill { get; set; } = "C";
        public string AutoCrop { get; set; } = "X";
        public string VideoSharpening { get; set; } = "E";
        public string BrightnessDown { get; set; } = "F7";
        public string BrightnessUp { get; set; } = "F8";
        public string BrightnessReset { get; set; } = "F9";
        public string ExportClip { get; set; } = "Ctrl+Shift+S";
        public string ResetAspectRatio { get; set; } = "Ctrl+D";

        // 备用热键 (Secondary Hotkeys)
        public string SecPlayPause { get; set; } = "";
        public string SecSeekForward { get; set; } = "";
        public string SecSeekBackward { get; set; } = "";
        public string SecSeekForward30 { get; set; } = "";
        public string SecSeekBackward30 { get; set; } = "";
        public string SecSpeedUp { get; set; } = "";
        public string SecSpeedDown { get; set; } = "";
        public string SecVolumeUp { get; set; } = "";
        public string SecVolumeDown { get; set; } = "";
        public string SecToggleMute { get; set; } = "";
        public string SecToggleFullscreen { get; set; } = "";
        public string SecPrevMedia { get; set; } = "XButton1";
        public string SecNextMedia { get; set; } = "XButton2";
        public string SecScreenshot { get; set; } = "";
        public string SecAbLoop { get; set; } = "";
        public string SecTogglePip { get; set; } = "";
        public string SecAlwaysOnTop { get; set; } = "";
        public string SecOpenFile { get; set; } = "";
        public string SecOpenFolder { get; set; } = "";
        public string SecOpenUrl { get; set; } = "";
        public string SecTogglePlaylist { get; set; } = "";
        public string SecToggleLibrary { get; set; } = "";
        public string SecSmartFill { get; set; } = "";
        public string SecAutoCrop { get; set; } = "";
        public string SecVideoSharpening { get; set; } = "";
        public string SecExportClip { get; set; } = "";
        public string SecBrightnessDown { get; set; } = "";
        public string SecBrightnessUp { get; set; } = "";
        public string SecBrightnessReset { get; set; } = "";
        public string SecResetAspectRatio { get; set; } = "";
    }

    public class SettingsService : INotifyPropertyChanged
    {
        public static SettingsService Instance { get; } = new SettingsService();

        public event PropertyChangedEventHandler? PropertyChanged;

        private readonly string _configFilePath;
        public SettingsModel Config { get; private set; } = new SettingsModel();

        private SettingsService()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string oldDir = Path.Combine(appData, "AnniPlayer");
            string dir = Path.Combine(appData, "AniPlayer");

            if (!Directory.Exists(dir) && Directory.Exists(oldDir))
            {
                try
                {
                    Directory.CreateDirectory(dir);
                    string oldCfg = Path.Combine(oldDir, "config.json");
                    string newCfg = Path.Combine(dir, "config.json");
                    if (File.Exists(oldCfg) && !File.Exists(newCfg)) File.Copy(oldCfg, newCfg, true);
                }
                catch { }
            }

            Directory.CreateDirectory(dir);
            _configFilePath = Path.Combine(dir, "config.json");

            Load();
        }

        public void Load()
        {
            try
            {
                if (File.Exists(_configFilePath))
                {
                    string json = File.ReadAllText(_configFilePath);
                    if (string.IsNullOrWhiteSpace(json) || json.Trim() == "{}")
                    {
                        Config = new SettingsModel();
                        Save(); // Generate complete formatted default config.json
                        return;
                    }

                    var jsonOptions = new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true,
                        ReadCommentHandling = JsonCommentHandling.Skip,
                        AllowTrailingCommas = true
                    };
                    var model = JsonSerializer.Deserialize<SettingsModel>(json, jsonOptions);
                    if (model != null)
                    {
                        Config = model;
                        if (Config.Hotkeys != null && Config.Hotkeys.ExportClip == "Ctrl+Shift+C")
                        {
                            Config.Hotkeys.ExportClip = "Ctrl+Shift+S";
                            Save();
                        }
                        if (!string.IsNullOrEmpty(Config.ScreenshotPath) && Config.ScreenshotPath.Contains("AnniPlayer", StringComparison.OrdinalIgnoreCase))
                        {
                            Config.ScreenshotPath = Config.ScreenshotPath.Replace("AnniPlayer", "AniPlayer", StringComparison.OrdinalIgnoreCase);
                            Save();
                        }

                        if (Config.PlaybackHistory == null)
                        {
                            Config.PlaybackHistory = new List<PlaybackProgressItem>();
                        }
                        if (Config.PlaybackHistory.Count == 0 && !string.IsNullOrEmpty(Config.LastPlayedFilePath) && Config.LastPlayedPosition > 1.0)
                        {
                            Config.PlaybackHistory.Add(new PlaybackProgressItem
                            {
                                FilePath = Config.LastPlayedFilePath,
                                Position = Config.LastPlayedPosition,
                                Duration = Config.LastPlayedDuration,
                                LastPlayedTime = DateTime.UtcNow
                            });
                        }
                    }
                    else
                    {
                        Config = new SettingsModel();
                        Save();
                    }
                }
                else
                {
                    Config = new SettingsModel();
                    Save(); // Generate complete formatted default config.json
                }
            }
            catch
            {
                Config = new SettingsModel();
                Save();
            }
        }

        private readonly object _historyLock = new object();

        /// <summary>
        /// 更新指定媒体文件的播放进度。
        /// 仅记录播放大于 3 秒且距离片尾大于 5 秒的有效进度；
        /// 采用滚动淘汰机制，最多保留 MaxPlaybackHistoryCount（默认 10 个）媒体记录。
        /// 与右侧播放列表完全解耦独立。
        /// </summary>
        public void UpdatePlaybackProgress(string filePath, double position, double duration)
        {
            if (string.IsNullOrWhiteSpace(filePath)) return;

            lock (_historyLock)
            {
                if (Config.PlaybackHistory == null)
                {
                    Config.PlaybackHistory = new List<PlaybackProgressItem>();
                }

                string normPath;
                try { normPath = Path.GetFullPath(filePath); }
                catch { normPath = filePath; }

                // 移除当前文件的已有记录（不区分大小写）
                Config.PlaybackHistory.RemoveAll(item =>
                {
                    if (string.IsNullOrWhiteSpace(item.FilePath)) return true;
                    try
                    {
                        return string.Equals(Path.GetFullPath(item.FilePath), normPath, StringComparison.OrdinalIgnoreCase);
                    }
                    catch
                    {
                        return string.Equals(item.FilePath, normPath, StringComparison.OrdinalIgnoreCase);
                    }
                });

                // 播放超过 3 秒，总时长大于 6 秒，且未在片尾最后 5 秒内：记入历史
                if (position > 3.0 && duration > 6.0 && position < duration - 5.0)
                {
                    // 插入到列表头部（最近播放）
                    Config.PlaybackHistory.Insert(0, new PlaybackProgressItem
                    {
                        FilePath = normPath,
                        Position = position,
                        Duration = duration,
                        LastPlayedTime = DateTime.UtcNow
                    });

                    // 滚动淘汰超过上限的旧记录（最多保留 MaxPlaybackHistoryCount 条，默认 10 条）
                    int maxCount = Math.Max(1, Config.MaxPlaybackHistoryCount);
                    while (Config.PlaybackHistory.Count > maxCount)
                    {
                        Config.PlaybackHistory.RemoveAt(Config.PlaybackHistory.Count - 1);
                    }

                    // 保持向后兼容单字段
                    Config.LastPlayedFilePath = normPath;
                    Config.LastPlayedPosition = position;
                    Config.LastPlayedDuration = duration;
                }
                else
                {
                    // 若已播完或回退到开头，清除单字段
                    if (string.Equals(Config.LastPlayedFilePath, normPath, StringComparison.OrdinalIgnoreCase))
                    {
                        Config.LastPlayedFilePath = "";
                        Config.LastPlayedPosition = 0.0;
                        Config.LastPlayedDuration = 0.0;
                    }
                }
            }
        }

        /// <summary>
        /// 获取指定文件的历史播放进度（秒）。若无记录或记录小于 1 秒则返回 0。
        /// </summary>
        public double GetPlaybackProgress(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath) || Config.PlaybackHistory == null) return 0.0;

            lock (_historyLock)
            {
                string normPath;
                try { normPath = Path.GetFullPath(filePath); }
                catch { normPath = filePath; }

                var item = Config.PlaybackHistory.FirstOrDefault(i =>
                {
                    if (string.IsNullOrWhiteSpace(i.FilePath)) return false;
                    try
                    {
                        return string.Equals(Path.GetFullPath(i.FilePath), normPath, StringComparison.OrdinalIgnoreCase);
                    }
                    catch
                    {
                        return string.Equals(i.FilePath, normPath, StringComparison.OrdinalIgnoreCase);
                    }
                });

                if (item != null && item.Position > 1.0)
                {
                    return item.Position;
                }

                // 兼容向后单字段
                if (!string.IsNullOrEmpty(Config.LastPlayedFilePath))
                {
                    bool match = false;
                    try { match = string.Equals(Path.GetFullPath(Config.LastPlayedFilePath), normPath, StringComparison.OrdinalIgnoreCase); }
                    catch { match = string.Equals(Config.LastPlayedFilePath, normPath, StringComparison.OrdinalIgnoreCase); }
                    if (match && Config.LastPlayedPosition > 1.0)
                    {
                        return Config.LastPlayedPosition;
                    }
                }
            }
            return 0.0;
        }

        /// <summary>
        /// 清除指定文件的历史播放进度（播完或手动重置时调用）。
        /// </summary>
        public void ClearPlaybackProgress(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath) || Config.PlaybackHistory == null) return;

            lock (_historyLock)
            {
                string normPath;
                try { normPath = Path.GetFullPath(filePath); }
                catch { normPath = filePath; }

                Config.PlaybackHistory.RemoveAll(item =>
                {
                    if (string.IsNullOrWhiteSpace(item.FilePath)) return true;
                    try
                    {
                        return string.Equals(Path.GetFullPath(item.FilePath), normPath, StringComparison.OrdinalIgnoreCase);
                    }
                    catch
                    {
                        return string.Equals(item.FilePath, normPath, StringComparison.OrdinalIgnoreCase);
                    }
                });

                if (string.Equals(Config.LastPlayedFilePath, normPath, StringComparison.OrdinalIgnoreCase))
                {
                    Config.LastPlayedFilePath = "";
                    Config.LastPlayedPosition = 0.0;
                    Config.LastPlayedDuration = 0.0;
                }
            }
        }

        public void Save()
        {
            try
            {
                var options = new JsonSerializerOptions { WriteIndented = true };
                string json = JsonSerializer.Serialize(Config, options);
                File.WriteAllText(_configFilePath, json);
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Config)));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SettingsService] Save failed: {ex.Message}");
            }
        }
    }
}
