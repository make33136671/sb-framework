using System.Security.Cryptography;
using System.Text;
using SB.Framework.Core.Util;

namespace SB.Framework.Core.License
{
    /// <summary>
    /// 系统时间回退检测器。
    /// 通过持久化最后已知时间戳（HMAC 签名保护）检测系统时钟是否被回拨。
    /// 时间戳同时写入主文件与应用数据目录备份，删单个文件无法重置检测状态。
    /// </summary>
    public static class TimeRegressionGuard
    {
        private static DateTime _lastKnownTime;
        private static DateTime _lastWriteTime;
        private static bool _initialized;
        private static bool _regressionDetected;
        private static byte[] _hmacKey = Array.Empty<byte>();
        private static readonly string _timeFilePath = Path.Combine(AppContext.BaseDirectory, "license_time.dat");
        private static readonly string _backupFilePath = GetBackupFilePath();
        private const int ToleranceMinutes = 5;
        private const int UpdateIntervalMinutes = 10;

        /// <summary>
        /// 备份时间戳路径：优先用户本地应用数据目录，避免顺手党删掉主文件后回拨时钟。
        /// 取不到应用数据目录（如无 HOME 的 Linux 服务）时回退到产物目录。
        /// </summary>
        private static string GetBackupFilePath()
        {
            var baseDir = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrEmpty(baseDir))
                return Path.Combine(AppContext.BaseDirectory, "license_time.backup.dat");
            var appDir = Path.Combine(baseDir, "SB_Framework");
            try
            {
                Directory.CreateDirectory(appDir);
                return Path.Combine(appDir, "license_time.dat");
            }
            catch
            {
                return Path.Combine(AppContext.BaseDirectory, "license_time.backup.dat");
            }
        }

        /// <summary>
        /// 初始化：加载主/备份两个持久化时间戳并执行首次检测
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
                var (tampered, lastKnown) = ReadTimeFiles();
                // 任一文件被篡改(HMAC 校验失败 / 解析失败)视为时间回退
                if (tampered)
                    _regressionDetected = true;
                else if (lastKnown.HasValue && lastKnown.Value > now.AddMinutes(ToleranceMinutes))
                    _regressionDetected = true;
            }
            catch
            {
                // 文件读取失败不阻塞启动
            }

            _lastKnownTime = now;
            _lastWriteTime = now;
            _initialized = true;
            WriteTimeFiles(now);
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
                WriteTimeFiles(now);
                _lastWriteTime = now;
            }

            return null;
        }

        /// <summary>是否已检测到时间回退</summary>
        public static bool IsRegressionDetected => _regressionDetected;

        /// <summary>
        /// 读取主/备份两个时间文件，返回 (是否篡改, 两份中最近的有效时间)。
        /// 某文件不存在视为"无候选"（如首次运行），不算篡改；存在但校验失败视为篡改。
        /// </summary>
        private static (bool Tampered, DateTime? LastKnown) ReadTimeFiles()
        {
            bool tampered = false;
            DateTime? lastKnown = null;

            TryRead(_timeFilePath, ref tampered, ref lastKnown);
            TryRead(_backupFilePath, ref tampered, ref lastKnown);

            return (tampered, lastKnown);
        }

        private static void TryRead(string path, ref bool tampered, ref DateTime? lastKnown)
        {
            try
            {
                if (!File.Exists(path)) return;

                var content = File.ReadAllText(path).Trim();
                var parts = content.Split('.');
                if (parts.Length != 2)
                {
                    tampered = true;
                    return;
                }

                var ticks = long.Parse(parts[0]);
                var storedHmac = parts[1];

                // 校验 HMAC
                var expectedHmac = ComputeHmac(parts[0]);
                if (storedHmac != expectedHmac)
                {
                    tampered = true; // 篡改
                    return;
                }

                var time = new DateTime(ticks, DateTimeKind.Utc);
                if (!lastKnown.HasValue || time > lastKnown.Value)
                    lastKnown = time;
            }
            catch
            {
                // 解析失败视为篡改
                tampered = true;
            }
        }

        private static void WriteTimeFiles(DateTime utcTime)
        {
            WriteTimeFile(_timeFilePath, utcTime);
            WriteTimeFile(_backupFilePath, utcTime);
        }

        private static void WriteTimeFile(string path, DateTime utcTime)
        {
            try
            {
                var ticks = utcTime.Ticks.ToString();
                var hmac = ComputeHmac(ticks);
                File.WriteAllText(path, $"{ticks}.{hmac}");
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
