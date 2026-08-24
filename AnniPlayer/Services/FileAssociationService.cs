using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Microsoft.Win32;

namespace AnniPlayer.Services
{
    public class FileAssociationService
    {
        public static FileAssociationService Instance { get; } = new FileAssociationService();

        public static readonly string[] VideoExtensions = new[]
        {
            ".mp4", ".mkv", ".avi", ".mov", ".wmv", ".flv", ".rmvb", ".ts", ".m2ts", ".webm"
        };

        public static readonly string[] AudioExtensions = new[]
        {
            ".mp3", ".flac", ".aac", ".wav", ".m4a", ".ogg", ".opus"
        };

        private const string ProgIdVideo = "AniPlayer.VideoFile";
        private const string ProgIdAudio = "AniPlayer.AudioFile";
        private const string AppExeName = "AniPlayer.exe";

        private FileAssociationService() { }

        public string GetExecutablePath()
        {
            string? exe = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exe) || exe.EndsWith("dotnet.exe", StringComparison.OrdinalIgnoreCase))
            {
                exe = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, AppExeName);
            }
            if (!File.Exists(exe))
            {
                string demoExe = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "Demo", AppExeName));
                if (File.Exists(demoExe)) return demoExe;
            }
            return exe;
        }

        public bool IsVideoAssociated()
        {
            return CheckExtensionsAssociated(VideoExtensions, ProgIdVideo);
        }

        public bool IsAudioAssociated()
        {
            return CheckExtensionsAssociated(AudioExtensions, ProgIdAudio);
        }

        public string GetDesktopShortcutPath()
        {
            string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            return Path.Combine(desktop, "AniPlayer.lnk");
        }

        public string GetStartMenuShortcutPath()
        {
            string startMenu = Environment.GetFolderPath(Environment.SpecialFolder.Programs);
            return Path.Combine(startMenu, "AniPlayer.lnk");
        }

        public bool IsDesktopShortcutCreated()
        {
            try
            {
                return File.Exists(GetDesktopShortcutPath());
            }
            catch
            {
                return false;
            }
        }

        public bool IsStartMenuShortcutCreated()
        {
            try
            {
                return File.Exists(GetStartMenuShortcutPath());
            }
            catch
            {
                return false;
            }
        }

        public bool SetDesktopShortcut(bool create)
        {
            try
            {
                string path = GetDesktopShortcutPath();
                if (create)
                {
                    CreateShortcutFile(path);
                }
                else
                {
                    if (File.Exists(path)) File.Delete(path);
                }
                NotifyShell();
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[FileAssociationService] SetDesktopShortcut error: {ex.Message}");
                return false;
            }
        }

        public bool SetStartMenuShortcut(bool create)
        {
            try
            {
                string path = GetStartMenuShortcutPath();
                if (create)
                {
                    CreateShortcutFile(path);
                }
                else
                {
                    if (File.Exists(path)) File.Delete(path);
                }
                NotifyShell();
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[FileAssociationService] SetStartMenuShortcut error: {ex.Message}");
                return false;
            }
        }

        private void CreateShortcutFile(string shortcutPath)
        {
            string exePath = GetExecutablePath();
            if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath)) return;

            string? dir = Path.GetDirectoryName(shortcutPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            Type? shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType != null)
            {
                dynamic shell = Activator.CreateInstance(shellType)!;
                dynamic shortcut = shell.CreateShortcut(shortcutPath);
                shortcut.TargetPath = exePath;
                shortcut.WorkingDirectory = Path.GetDirectoryName(exePath) ?? AppDomain.CurrentDomain.BaseDirectory;
                shortcut.Description = "AniPlayer - Modern Hardware-Accelerated Video & Audio Player";
                shortcut.IconLocation = $"{exePath},0";
                shortcut.Save();
            }
        }

        public bool IsFolderContextMenuRegistered()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Classes\Directory\shell\AniPlayer");
                return key != null;
            }
            catch
            {
                return false;
            }
        }

        public void SetFolderContextMenuRegistered(bool register)
        {
            try
            {
                string exePath = GetExecutablePath();
                if (string.IsNullOrEmpty(exePath)) return;

                string menuText = I18nService.Instance["ContextMenuPlayWithAnni"];
                if (string.IsNullOrEmpty(menuText) || menuText == "ContextMenuPlayWithAnni")
                {
                    menuText = "使用 Ani player 播放";
                }

                string[] targetKeys = new[]
                {
                    @"Software\Classes\Directory\shell\AniPlayer",
                    @"Software\Classes\Folder\shell\AniPlayer"
                };

                // Always clean up legacy "AnniPlayer" (double n) keys
                string[] legacyKeys = new[]
                {
                    @"Software\Classes\Directory\shell\AnniPlayer",
                    @"Software\Classes\Directory\Background\shell\AnniPlayer",
                    @"Software\Classes\Folder\shell\AnniPlayer",
                    @"Software\Classes\SystemFileAssociations\video\shell\AnniPlayerPlayDir",
                    @"Software\Classes\AnniPlayer.AudioFile",
                    @"Software\Classes\AnniPlayer.VideoFile",
                    @"Software\Classes\Applications\AnniPlayer.exe",
                    @"Software\AnniPlayer"
                };
                foreach (var lk in legacyKeys)
                {
                    try { Registry.CurrentUser.DeleteSubKeyTree(lk, false); } catch { }
                }

                if (register)
                {
                    // 1. Directory shell & Folder shell (右键文件夹图标)
                    foreach (var path in targetKeys)
                    {
                        using var key = Registry.CurrentUser.CreateSubKey(path);
                        key.SetValue("", menuText);
                        key.SetValue("MUIVerb", menuText);
                        key.SetValue("Icon", $"\"{exePath}\",0");
                        using var cmdKey = key.CreateSubKey("command");
                        cmdKey.SetValue("", $"\"{exePath}\" \"%1\"");
                    }

                    // 2. Directory Background shell (文件夹内部空白处右键)
                    using (var bgKey = Registry.CurrentUser.CreateSubKey(@"Software\Classes\Directory\Background\shell\AniPlayer"))
                    {
                        bgKey.SetValue("", menuText);
                        bgKey.SetValue("MUIVerb", menuText);
                        bgKey.SetValue("Icon", $"\"{exePath}\",0");
                        using var cmdKey = bgKey.CreateSubKey("command");
                        cmdKey.SetValue("", $"\"{exePath}\" \"%V\"");
                    }
                }
                else
                {
                    foreach (var path in targetKeys)
                    {
                        try { Registry.CurrentUser.DeleteSubKeyTree(path, false); } catch { }
                    }
                    try { Registry.CurrentUser.DeleteSubKeyTree(@"Software\Classes\Directory\Background\shell\AniPlayer", false); } catch { }
                }

                NotifyShell();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[FileAssociationService] SetFolderContextMenuRegistered error: {ex.Message}");
            }
        }

        public void SetVideoAssociated(bool associate)
        {
            RegisterApplicationCapabilities();
            if (associate)
            {
                RegisterProgId(ProgIdVideo, "Ani player 视频文件");
                ApplyExtensionAssociations(VideoExtensions, ProgIdVideo);
            }
            else
            {
                RemoveExtensionAssociations(VideoExtensions, ProgIdVideo);
            }
        }

        public void SetAudioAssociated(bool associate)
        {
            RegisterApplicationCapabilities();
            if (associate)
            {
                RegisterProgId(ProgIdAudio, "Ani player 音频文件");
                ApplyExtensionAssociations(AudioExtensions, ProgIdAudio);
            }
            else
            {
                RemoveExtensionAssociations(AudioExtensions, ProgIdAudio);
            }
        }

        private void RegisterApplicationCapabilities()
        {
            try
            {
                string exePath = GetExecutablePath();
                if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath)) return;

                // 1. HKCU\Software\Classes\Applications\AniPlayer.exe
                using (var appKey = Registry.CurrentUser.CreateSubKey($@"Software\Classes\Applications\{AppExeName}"))
                {
                    appKey.SetValue("FriendlyAppName", "Ani player");
                    using (var cmdKey = appKey.CreateSubKey(@"shell\open\command"))
                    {
                        cmdKey.SetValue("", $"\"{exePath}\" \"%1\"");
                    }
                    using (var suppKey = appKey.CreateSubKey("SupportedTypes"))
                    {
                        foreach (var ext in VideoExtensions) suppKey.SetValue(ext, "");
                        foreach (var ext in AudioExtensions) suppKey.SetValue(ext, "");
                    }
                }

                // 2. HKCU\Software\AniPlayer\Capabilities
                using (var capKey = Registry.CurrentUser.CreateSubKey(@"Software\AniPlayer\Capabilities"))
                {
                    capKey.SetValue("ApplicationDescription", "Ani player High Performance Media Player");
                    capKey.SetValue("ApplicationName", "Ani player");
                    using (var faKey = capKey.CreateSubKey("FileAssociations"))
                    {
                        foreach (var ext in VideoExtensions) faKey.SetValue(ext, ProgIdVideo);
                        foreach (var ext in AudioExtensions) faKey.SetValue(ext, ProgIdAudio);
                    }
                }

                // 3. HKCU\Software\RegisteredApplications
                using (var regAppKey = Registry.CurrentUser.CreateSubKey(@"Software\RegisteredApplications"))
                {
                    regAppKey.SetValue("AniPlayer", @"Software\AniPlayer\Capabilities");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[FileAssociationService] RegisterApplicationCapabilities error: {ex.Message}");
            }
        }

        private void RegisterProgId(string progId, string description)
        {
            try
            {
                string exePath = GetExecutablePath();
                if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath)) return;

                using (var hkcuClass = Registry.CurrentUser.CreateSubKey(@"Software\Classes\" + progId))
                {
                    hkcuClass.SetValue("", description);
                    using (var iconKey = hkcuClass.CreateSubKey("DefaultIcon"))
                    {
                        iconKey.SetValue("", $"\"{exePath}\",0");
                    }
                    using (var cmdKey = hkcuClass.CreateSubKey(@"shell\open\command"))
                    {
                        cmdKey.SetValue("", $"\"{exePath}\" \"%1\"");
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[FileAssociationService] RegisterProgId error: {ex.Message}");
            }
        }

        private void ApplyExtensionAssociations(IEnumerable<string> extensions, string progId)
        {
            try
            {
                string exePath = GetExecutablePath();
                foreach (var ext in extensions)
                {
                    // A. HKCU\Software\Classes\.mp4
                    using (var extKey = Registry.CurrentUser.CreateSubKey(@"Software\Classes\" + ext))
                    {
                        extKey.SetValue("", progId);
                        using (var owpKey = extKey.CreateSubKey("OpenWithProgids"))
                        {
                            owpKey.SetValue(progId, new byte[0], RegistryValueKind.None);
                        }
                    }

                    // B. HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts\.mp4\OpenWithList
                    using (var extKey = Registry.CurrentUser.CreateSubKey($@"Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts\{ext}\OpenWithList"))
                    {
                        extKey.SetValue("a", AppExeName);
                        extKey.SetValue("MRUList", "a");
                    }

                    // C. HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts\.mp4\OpenWithProgids
                    using (var extKey = Registry.CurrentUser.CreateSubKey($@"Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts\{ext}\OpenWithProgids"))
                    {
                        extKey.SetValue(progId, new byte[0], RegistryValueKind.None);
                    }
                }
                NotifyShell();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[FileAssociationService] ApplyExtensionAssociations error: {ex.Message}");
            }
        }

        private void RemoveExtensionAssociations(IEnumerable<string> extensions, string progId)
        {
            try
            {
                foreach (var ext in extensions)
                {
                    using (var extKey = Registry.CurrentUser.OpenSubKey(@"Software\Classes\" + ext, writable: true))
                    {
                        if (extKey != null)
                        {
                            object? val = extKey.GetValue("");
                            if (val?.ToString() == progId)
                            {
                                extKey.DeleteValue("", false);
                            }
                            using (var owpKey = extKey.OpenSubKey("OpenWithProgids", writable: true))
                            {
                                owpKey?.DeleteValue(progId, false);
                            }
                        }
                    }
                }
                NotifyShell();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[FileAssociationService] RemoveExtensionAssociations error: {ex.Message}");
            }
        }

        private bool CheckExtensionsAssociated(IEnumerable<string> extensions, string progId)
        {
            try
            {
                foreach (var ext in extensions)
                {
                    using (var extKey = Registry.CurrentUser.OpenSubKey(@"Software\Classes\" + ext))
                    {
                        if (extKey != null)
                        {
                            object? val = extKey.GetValue("");
                            if (val?.ToString() == progId) return true;
                        }
                    }
                }
            }
            catch { }
            return false;
        }

        [System.Runtime.InteropServices.DllImport("shell32.dll", CharSet = System.Runtime.InteropServices.CharSet.Auto, SetLastError = true)]
        private static extern void SHChangeNotify(int wEventId, uint uFlags, IntPtr dwItem1, IntPtr dwItem2);

        private void NotifyShell()
        {
            try
            {
                SHChangeNotify(0x08000000, 0, IntPtr.Zero, IntPtr.Zero);
            }
            catch { }
        }
    }
}
