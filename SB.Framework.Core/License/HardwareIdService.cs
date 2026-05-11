using System;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace SB.Framework.Core.License
{
    /// <summary>
    /// 硬件 ID 采集服务 — 生成基于系统硬件信息的唯一指纹
    /// </summary>
    public static class HardwareIdService
    {
        private static string? _cached;

        /// <summary>
        /// 获取硬件 ID（SHA256 哈希，64 位十六进制字符串）
        /// </summary>
        public static string GetHardwareId()
        {
            if (_cached != null) return _cached;

            var components = new StringBuilder();

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                CollectLinuxInfo(components);
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                CollectWindowsInfo(components);
            }

            // 通用信息（所有平台）
            CollectCommonInfo(components);

            // MAC 地址
            CollectMacAddresses(components);

            // 如果没有采集到任何信息，使用 fallback
            if (components.Length == 0)
            {
                components.Append(Environment.MachineName);
                components.Append('|');
                components.Append(RuntimeInformation.OSDescription);
            }

            using var sha256 = SHA256.Create();
            var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(components.ToString()));
            _cached = Convert.ToHexString(hash).ToLowerInvariant();
            return _cached;
        }

        /// <summary>
        /// 获取硬件信息明细（用于调试和展示）
        /// </summary>
        public static HardwareInfo GetHardwareInfo()
        {
            var info = new HardwareInfo
            {
                MachineName = Environment.MachineName,
                OSDescription = RuntimeInformation.OSDescription,
                ProcessorCount = Environment.ProcessorCount,
                HardwareId = GetHardwareId()
            };

            // Machine ID
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                info.MachineId = ReadFileContent("/etc/machine-id");
                info.CpuModel = ReadCpuModel();
            }

            // MAC 地址
            info.MacAddresses = GetPhysicalMacAddresses();

            return info;
        }

        private static void CollectLinuxInfo(StringBuilder sb)
        {
            // /etc/machine-id — OS 安装唯一标识
            var machineId = ReadFileContent("/etc/machine-id");
            if (!string.IsNullOrEmpty(machineId))
            {
                sb.Append("MID:");
                sb.Append(machineId);
                sb.Append('|');
            }

            // CPU 型号和核心数
            var cpuModel = ReadCpuModel();
            if (!string.IsNullOrEmpty(cpuModel))
            {
                sb.Append("CPU:");
                sb.Append(cpuModel);
                sb.Append('|');
            }
        }

        private static void CollectWindowsInfo(StringBuilder sb)
        {
            // Windows MachineGuid
            try
            {
                var key = Microsoft.Win32.Registry.LocalMachine?
                    .OpenSubKey(@"SOFTWARE\Microsoft\Cryptography");
                var guid = key?.GetValue("MachineGuid")?.ToString();
                if (!string.IsNullOrEmpty(guid))
                {
                    sb.Append("WMID:");
                    sb.Append(guid);
                    sb.Append('|');
                }
            }
            catch { }
        }

        private static void CollectCommonInfo(StringBuilder sb)
        {
            sb.Append("PROC:");
            sb.Append(Environment.ProcessorCount);
            sb.Append('|');
            sb.Append("ARCH:");
            sb.Append(RuntimeInformation.OSArchitecture);
            sb.Append('|');
        }

        private static void CollectMacAddresses(StringBuilder sb)
        {
            var macs = GetPhysicalMacAddresses();
            if (macs.Length > 0)
            {
                sb.Append("MAC:");
                sb.Append(string.Join(",", macs));
                sb.Append('|');
            }
        }

        private static string[] GetPhysicalMacAddresses()
        {
            try
            {
                return NetworkInterface.GetAllNetworkInterfaces()
                    .Where(n => n.NetworkInterfaceType == NetworkInterfaceType.Ethernet
                             || n.NetworkInterfaceType == NetworkInterfaceType.Wireless80211)
                    .Select(n => n.GetPhysicalAddress().ToString())
                    .Where(mac => !string.IsNullOrEmpty(mac) && mac != "000000000000")
                    .Distinct()
                    .OrderBy(mac => mac)
                    .Take(2)
                    .ToArray();
            }
            catch
            {
                return Array.Empty<string>();
            }
        }

        private static string? ReadFileContent(string path)
        {
            try
            {
                if (File.Exists(path))
                    return File.ReadAllText(path).Trim();
            }
            catch { }
            return null;
        }

        private static string? ReadCpuModel()
        {
            try
            {
                if (!File.Exists("/proc/cpuinfo")) return null;

                var lines = File.ReadAllLines("/proc/cpuinfo");
                var modelLine = lines.FirstOrDefault(l =>
                    l.StartsWith("model name", StringComparison.OrdinalIgnoreCase));
                var coresLine = lines.FirstOrDefault(l =>
                    l.StartsWith("cpu cores", StringComparison.OrdinalIgnoreCase));

                var parts = new StringBuilder();
                if (modelLine != null)
                {
                    var value = modelLine.Split(':').LastOrDefault()?.Trim();
                    if (!string.IsNullOrEmpty(value)) parts.Append(value);
                }
                if (coresLine != null)
                {
                    var value = coresLine.Split(':').LastOrDefault()?.Trim();
                    if (!string.IsNullOrEmpty(value))
                    {
                        if (parts.Length > 0) parts.Append('/');
                        parts.Append(value);
                    }
                }

                return parts.Length > 0 ? parts.ToString() : null;
            }
            catch
            {
                return null;
            }
        }
    }

    public class HardwareInfo
    {
        public string MachineName { get; set; } = "";
        public string OSDescription { get; set; } = "";
        public int ProcessorCount { get; set; }
        public string HardwareId { get; set; } = "";
        public string? MachineId { get; set; }
        public string? CpuModel { get; set; }
        public string[] MacAddresses { get; set; } = Array.Empty<string>();
    }
}
