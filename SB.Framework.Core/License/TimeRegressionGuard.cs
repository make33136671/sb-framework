using System.Security.Cryptography;
using System.Text;
using SB.Framework.Core.Util;

namespace SB.Framework.Core.License
{
    /// <summary>
    /// 系统时间回退检测器。
    /// 通过持久化最后已知时间戳（HMAC 签名保护），检测系统时钟是否被回拨。
    /// </summary>
    public static class TimeRegressionGuard
    {
        private static DateTime _lastKnownTime;
        private static DateTime _lastWriteTime;
        private static bool _initialized;
        private static bool _regressionDetected;
        private static byte[] _hmacKey = Array.Empty<byte>();
        private static readonly string _timeFilePath = Path.Combine(AppContext.BaseDirectory, "license_time.dat");
        private const int ToleranceMinutes = 5;
        private const int UpdateIntervalMinutes = 10;

        /// <summary>
        /// 初始化：加载持久化的时间戳并执行首次检测
        /// </summary>
        public static void Initialize(string? publicKey = null)
        {
            // 用公钥 + 本机 HardwareId 联合派生 HMAC 密钥
            // 即使攻击者提取了公钥，也无法在另一台机器上伪造时间戳文件
            var keyMaterial = (publicKey ?? "sb-license-guard") + "|" + HardwareIdService.GetHardwareId();
            _hmacKey = SHA256.HashData(Encoding.UTF8.GetBytes(keyMaterial));

            var now = AppTime.UtcNow;

            try
            {
                if (File.Exists(_timeFilePath))
                {
                    var storedTime = ReadTimeFile();
                    if (storedTime.HasValue)
                    {
                        if (storedTime.Value > now.AddMinutes(ToleranceMinutes))
                        {
                            _regressionDetected = true;
                        }
                    }
                    else
                    {
                        // 文件被篡改（HMAC 校验失败），视为时间回退
                        _regressionDetected = true;
                    }
                }
            }
            catch
            {
                // 文件读取失败不阻塞启动
            }

            _lastKnownTime = now;
            _lastWriteTime = now;
            _initialized = true;
            WriteTimeFile(now);
        }

        /// <summary>
        /// 检测时间是否回退。返回错误消息表示检测到回退，返回 null 表示正常。
        /// 同时定期持久化当前时间。
        /// </summary>
        public static string? Check()
        {
            if (!_initialized) return null;

            if (_regressionDetected)
                return "检测到系统时间异常，授权验证失败。请恢复正确的系统时间后重启服务。";

            var now = AppTime.UtcNow;

            if (now < _lastKnownTime.AddMinutes(-ToleranceMinutes))
            {
                _regressionDetected = true;
                return "检测到系统时间回退，授权验证失败。请恢复正确的系统时间后重启服务。";
            }

            // 更新内存中的最后已知时间
            if (now > _lastKnownTime)
                _lastKnownTime = now;

            // 定期持久化
            if (now > _lastWriteTime.AddMinutes(UpdateIntervalMinutes))
            {
                WriteTimeFile(now);
                _lastWriteTime = now;
            }

            return null;
        }

        /// <summary>是否已检测到时间回退</summary>
        public static bool IsRegressionDetected => _regressionDetected;

        private static DateTime? ReadTimeFile()
        {
            try
            {
                var content = File.ReadAllText(_timeFilePath).Trim();
                var parts = content.Split('.');
                if (parts.Length != 2) return null;

                var ticks = long.Parse(parts[0]);
                var storedHmac = parts[1];

                // 校验 HMAC
                var expectedHmac = ComputeHmac(parts[0]);
                if (storedHmac != expectedHmac) return null; // 篡改

                return new DateTime(ticks, DateTimeKind.Utc);
            }
            catch
            {
                return null;
            }
        }

        private static void WriteTimeFile(DateTime utcTime)
        {
            try
            {
                var ticks = utcTime.Ticks.ToString();
                var hmac = ComputeHmac(ticks);
                File.WriteAllText(_timeFilePath, $"{ticks}.{hmac}");
            }
            catch
            {
                // 写入失败不影响运行
            }
        }

        private static string ComputeHmac(string data)
        {
            using var hmac = new HMACSHA256(_hmacKey);
            var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(data));
            return Convert.ToHexString(hash).ToLowerInvariant();
        }
    }
}
