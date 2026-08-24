using System;
using System.IO;
using System.Text.Json;
using System.ComponentModel;

namespace AnniPlayer.Services
{
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

        // 图片幻灯片与背景音频
        public int ImageDurationSec { get; set; } = 5;
        public int BgmMode { get; set; } = 0; // 0: Auto Same Directory, 1: Manual Specified, 2: Disabled
        public string ManualBgmPath { get; set; } = "";
        public bool BgmSyncPlayPause { get; set; } = true;

        // 画面与去黑边
        public int DefaultCropMode { get; set; } = 0; // 0: Off, 1: Subtitle, 2: Full
        public bool DefaultSmartFill { get; set; } = false;
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

        // 低清视频画质锐化增强与抗模糊 (默认关闭)
        public bool VideoSharpening { get; set; } = false;

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
        public string SecPrevMedia { get; set; } = "";
        public string SecNextMedia { get; set; } = "";
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
