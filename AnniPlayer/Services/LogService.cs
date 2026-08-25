using System;
using System.IO;

namespace AnniPlayer.Services
{
    public static class LogService
    {
        private static readonly object _lock = new object();

        public static string LogDir
        {
            get
            {
                string baseDir = !string.IsNullOrEmpty(App.UserDir)
                    ? App.UserDir
                    : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AniPlayer");
                return Path.Combine(baseDir, "logs");
            }
        }

        public static string CrashLogPath => Path.Combine(LogDir, "crash.log");
        public static string SystemLogPath => Path.Combine(LogDir, "system.log");

        /// <summary>
        /// 记录致命崩溃与未捕获异常日志（无条件始终记录，用于故障复盘与排查，不受开关限制）
        /// </summary>
        public static void LogCrash(string context, Exception ex)
        {
            try
            {
                Directory.CreateDirectory(LogDir);
                lock (_lock)
                {
                    File.AppendAllText(CrashLogPath,
                        $"\n[{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}] [FATAL-CRASH] [{context}]\n{ex}\n------------------------------------------------------------\n");
                }
            }
            catch { }
        }

        /// <summary>
        /// 记录系统运行与调试流水日志（仅当 config.json 中 EnableSystemLog == true 时才记录）
        /// </summary>
        public static void LogSystem(string tag, string message)
        {
            try
            {
                if (!SettingsService.Instance.Config.EnableSystemLog) return;

                Directory.CreateDirectory(LogDir);
                lock (_lock)
                {
                    File.AppendAllText(SystemLogPath,
                        $"[{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff zzz}] [{tag}] {message}\n");
                }
            }
            catch { }
        }
    }
}
