using System;
using Microsoft.Win32;

namespace AnniPlayer.Services
{
    /// <summary>
    /// 高性能 GPU 硬件检测服务
    /// 毫秒级读取系统显示适配器注册表，精准识别显卡型号与 NVIDIA RTX 超分辨率 (VSR) 硬件支持能力
    /// </summary>
    public static class GpuDetectionService
    {
        private static bool? _isRtxSupported;
        private static string _gpuName = "";
        private static string _driverVersion = "";
        private static readonly object _lock = new();

        /// <summary>
        /// 当前系统显卡是否支持 NVIDIA RTX 视频超分辨率 (VSR)
        /// (支持 RTX 20/30/40/50 系列、TITAN RTX、Quadro RTX 及 RTX 专业显卡)
        /// </summary>
        public static bool IsRtxVsrSupported
        {
            get
            {
                lock (_lock)
                {
                    if (_isRtxSupported.HasValue) return _isRtxSupported.Value;
                    DetectGpu();
                    return _isRtxSupported ?? false;
                }
            }
        }

        /// <summary>
        /// 主显卡或独立显卡名称
        /// </summary>
        public static string GpuName
        {
            get
            {
                lock (_lock)
                {
                    if (string.IsNullOrEmpty(_gpuName)) DetectGpu();
                    return _gpuName;
                }
            }
        }

        /// <summary>
        /// 显卡驱动版本号
        /// </summary>
        public static string DriverVersion
        {
            get
            {
                lock (_lock)
                {
                    if (string.IsNullOrEmpty(_driverVersion)) DetectGpu();
                    return _driverVersion;
                }
            }
        }

        private static void DetectGpu()
        {
            try
            {
                const string classKeyPath = @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}";
                using var classKey = Registry.LocalMachine.OpenSubKey(classKeyPath);
                if (classKey != null)
                {
                    string fallbackGpu = "";
                    string fallbackVer = "";

                    foreach (var subKeyName in classKey.GetSubKeyNames())
                    {
                        if (subKeyName.Length != 4 || !char.IsDigit(subKeyName[0])) continue;
                        using var subKey = classKey.OpenSubKey(subKeyName);
                        if (subKey == null) continue;

                        string? desc = subKey.GetValue("DriverDesc") as string;
                        string? ver = subKey.GetValue("DriverVersion") as string;
                        if (string.IsNullOrWhiteSpace(desc)) continue;

                        if (string.IsNullOrEmpty(fallbackGpu))
                        {
                            fallbackGpu = desc;
                            fallbackVer = ver ?? "";
                        }

                        // 优先匹配 NVIDIA 显卡
                        if (desc.IndexOf("NVIDIA", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            desc.IndexOf("GeForce", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            desc.IndexOf("RTX", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            _gpuName = desc;
                            _driverVersion = ver ?? "";

                            // RTX 20/30/40/50 系列、TITAN RTX、Quadro RTX、RTX A 系列
                            bool hasRtx = desc.IndexOf("RTX", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                          desc.IndexOf("TITAN RTX", StringComparison.OrdinalIgnoreCase) >= 0;

                            _isRtxSupported = hasRtx;
                            return;
                        }
                    }

                    if (!string.IsNullOrEmpty(fallbackGpu))
                    {
                        _gpuName = fallbackGpu;
                        _driverVersion = fallbackVer;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[GpuDetectionService] {ex.Message}");
            }

            _isRtxSupported = false;
            if (string.IsNullOrEmpty(_gpuName)) _gpuName = "Unknown Display Adapter";
        }
    }
}
