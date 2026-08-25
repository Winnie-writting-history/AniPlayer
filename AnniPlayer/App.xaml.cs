using System;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Windows;
using WpfApp = System.Windows.Application;

namespace AnniPlayer
{
    public partial class App : WpfApp
    {
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern short GetKeyState(int vKey);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool AllowSetForegroundWindow(int dwProcessId);
        private const int ASFW_ANY = -1;

        private const int VK_SHIFT = 0x10;
        private const int VK_LSHIFT = 0xA0;
        private const int VK_RSHIFT = 0xA1;

        public static bool CheckShiftState()
        {
            try
            {
                if ((GetAsyncKeyState(VK_SHIFT) & 0x8000) != 0 || (GetAsyncKeyState(VK_SHIFT) & 0x0001) != 0) return true;
                if ((GetAsyncKeyState(VK_LSHIFT) & 0x8000) != 0 || (GetAsyncKeyState(VK_RSHIFT) & 0x8000) != 0) return true;
                if ((GetKeyState(VK_SHIFT) & 0x8000) != 0) return true;
                if ((GetKeyState(VK_LSHIFT) & 0x8000) != 0 || (GetKeyState(VK_RSHIFT) & 0x8000) != 0) return true;
            }
            catch { }
            return false;
        }

        public static bool WasShiftHeldOnLaunch { get; set; }
        private static Mutex? _mutex;
        public static string  UserDir   { get; private set; } = "";
        public static string[] StartArgs { get; private set; } = Array.Empty<string>();

        protected override void OnStartup(StartupEventArgs e)
        {
            try { File.WriteAllText(@"E:\Winnie-history\Anni player\perf_app_startup.txt", string.Join(" | ", Environment.GetCommandLineArgs())); } catch {}
            WasShiftHeldOnLaunch = CheckShiftState();

            StartArgs = e.Args;

            // ── User data directory tree ──────────────────────────────────
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string oldDir = Path.Combine(appData, "AnniPlayer");
            string newDir = Path.Combine(appData, "AniPlayer");

            UserDir = newDir;
            try
            {
                // Smooth migration of existing user configs and playlists
                if (!Directory.Exists(newDir) && Directory.Exists(oldDir))
                {
                    try
                    {
                        MigrateDirectory(oldDir, newDir);
                    }
                    catch { }
                }

                Directory.CreateDirectory(UserDir);
                Directory.CreateDirectory(Path.Combine(UserDir, "logs"));
                Directory.CreateDirectory(Path.Combine(UserDir, "playlists"));
                Directory.CreateDirectory(Path.Combine(UserDir, "subtitles"));
                Directory.CreateDirectory(Path.Combine(UserDir, "snapshots"));

                var cfg = Path.Combine(UserDir, "config.json");
                if (!File.Exists(cfg)) File.WriteAllText(cfg, "{}");
            }
            catch { /* non-fatal */ }

            // ── Global crash handlers ─────────────────────────────────────
            DispatcherUnhandledException += (s, ex) =>
            {
                LogCrash("DispatcherUnhandledException", ex.Exception);
                ex.Handled = true;
            };
            AppDomain.CurrentDomain.UnhandledException += (s, ex) =>
            {
                if (ex.ExceptionObject is Exception exc) LogCrash("UnhandledException", exc);
            };
            TaskScheduler.UnobservedTaskException += (s, ex) =>
            {
                LogCrash("UnobservedTaskException", ex.Exception);
                ex.SetObserved();
            };

            // ── Single-instance (skip for --self-test and --perf-debug) ────────────────────
            bool isSelfTest = e.Args.Length > 0 && (e.Args[0] == "--self-test" || e.Args[0] == "--perf-debug" || e.Args[0] == "--benchmark");
            if (!isSelfTest)
            {
                _mutex = new Mutex(true, @"Local\AniPlayer_SingleInstance_Mutex_v3", out bool isNew);
                if (!isNew)
                {
                    AllowSetForegroundWindow(ASFW_ANY);

                    // Second instance detected — pass args or signal main instance to show window, then Exit!
                    string targetFile = ParseFilePathFromArgs(e.Args);
                    if (string.IsNullOrEmpty(targetFile))
                    {
                        targetFile = "__SHOW_WINDOW__";
                    }
                    else if (WasShiftHeldOnLaunch || CheckShiftState())
                    {
                        targetFile = "__SHIFT_OPEN__|" + targetFile;
                    }
                    SendFileToRunningInstance(targetFile);
                    Environment.Exit(0);
                    return;
                }
            }

            base.OnStartup(e);
            var win = new MainWindow();
            win.Show();
        }

        private static void MigrateDirectory(string sourceDir, string targetDir)
        {
            Directory.CreateDirectory(targetDir);
            foreach (var file in Directory.GetFiles(sourceDir, "*.*", SearchOption.AllDirectories))
            {
                string rel = Path.GetRelativePath(sourceDir, file);
                string dest = Path.Combine(targetDir, rel);
                string? destParent = Path.GetDirectoryName(dest);
                if (!string.IsNullOrEmpty(destParent)) Directory.CreateDirectory(destParent);
                File.Copy(file, dest, true);
            }
        }

        private static void SendFileToRunningInstance(string filePath)
        {
            // 1. Try Pipe
            bool sentByPipe = false;
            try
            {
                using var pipe = new NamedPipeClientStream(".", "AniPlayerPipe", PipeDirection.Out);
                pipe.Connect(500);
                using var sw = new StreamWriter(pipe) { AutoFlush = true };
                sw.WriteLine(filePath);
                sentByPipe = true;
            }
            catch { }

            // 2. Fallback to File Queue
            if (!sentByPipe)
            {
                try
                {
                    string queueFile = Path.Combine(UserDir, "cmd_queue.txt");
                    File.AppendAllText(queueFile, filePath + Environment.NewLine);
                }
                catch { }
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            try { _mutex?.ReleaseMutex(); _mutex?.Dispose(); } catch { }
            base.OnExit(e);
        }

        public static string ParseFilePathFromArgs(string[] args)
        {
            if (args == null || args.Length == 0) return "";
            if (File.Exists(args[0])) return args[0];
            string joined = string.Join(" ", args);
            if (File.Exists(joined)) return joined;
            return args[0];
        }

        public static void LogCrash(string context, Exception ex)
        {
            Services.LogService.LogCrash(context, ex);
        }
    }
}
