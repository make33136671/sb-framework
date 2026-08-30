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
        private static string _backupFilePath = GetBackupFilePath();
        private const int ToleranceMinutes = 5;
        private const int UpdateIntervalMinutes = 10;

        /// <summary>
        /// 备份时间戳路径：优先用户本地应用数据目录，避免顺手党删掉主文件后回拨时钟。
        /// 文件名包含公钥指纹：同一机器上多个 sb-framework 产品（不同公钥/不同 HMAC 密钥）
        /// 各自持有独立备份文件，避免互相覆盖导致校验失败误判篡改。
        /// 取不到应用数据目录（如无 HOME 的 Linux 服务）时回退到产物目录。
        /// </summary>
        private static string GetBackupFilePath(string? publicKey = null)
        {
            var baseDir = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrEmpty(baseDir))
                return Path.Combine(AppContext.BaseDirectory, "license_time.backup.dat");
            var appDir = Path.Combine(baseDir, "SB_Framework");
            try
            {
                Directory.CreateDirectory(appDir);
                var suffix = "";
                if (!string.IsNullOrEmpty(publicKey))
                {
                    var fp = SHA256.HashData(Encoding.UTF8.GetBytes(publicKey));
                    suffix = "." + Convert.ToHexString(fp, 0, 4).ToLowerInvariant();
                }
                return Path.Combine(appDir, $"license_time{suffix}.dat");
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
            _backupFilePath = GetBackupFilePath(publicKey);

            var now = AppTime.UtcNow;

            try
            {
                var (state, lastKnown) = ReadTimeFiles();
                switch (state)
                {
                    case ReadState.Ok when lastKnown.HasValue && lastKnown.Value > now.AddMinutes(ToleranceMinutes):
                        // 两份文件均校验通过且记录了未来时间：真实的时间回拨
                        _regressionDetected = true;
                        Console.Error.WriteLine($"[License] 时间回退检测：记录时间 {lastKnown.Value:O} 超前当前时间 {now:O} 超过 {ToleranceMinutes} 分钟");
                        break;
                    case ReadState.SingleCorrupt:
                        // 单文件损坏（常见于进程退出时非原子写入被截断）：
                        // 以另一份有效文件为准，静默自愈，不视为篡改
                        Console.Error.WriteLine("[License] 时间戳文件部分损坏（已用有效副本自愈）");
                        break;
                    case ReadState.AllCorrupt:
                        // 两份同时校验失败：视为篡改
                        _regressionDetected = true;
                        Console.Error.WriteLine("[License] 时间回退检测：主/备份时间戳文件均校验失败，视为篡改");
                        break;
                }
            }
            catch (Exception ex)
            {
                // 文件读取失败不阻塞启动
                Console.Error.WriteLine($"[License] 时间戳文件读取异常（忽略）: {ex.Message}");
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
        /// 读取主/备份两个时间文件。
        /// 返回读取状态与两份中最新的有效时间。
        /// 单份损坏（如非原子写被截断）以另一份为准；两份同时校验失败才视为篡改。
        /// </summary>
        private static (ReadState State, DateTime? LastKnown) ReadTimeFiles()
        {
            var mainOk = TryRead(_timeFilePath, out var mainTime);
            var backupOk = TryRead(_backupFilePath, out var backupTime);

            DateTime? lastKnown = null;
            if (mainOk && mainTime.HasValue && (lastKnown == null || mainTime > lastKnown)) lastKnown = mainTime;
            if (backupOk && backupTime.HasValue && (lastKnown == null || backupTime > lastKnown)) lastKnown = backupTime;

            var state = (mainOk, backupOk) switch
            {
                (true, true) => ReadState.Ok,
                (false, false) => ReadState.AllCorrupt,
                _ => ReadState.SingleCorrupt,
            };
            return (state, lastKnown);
        }

        private static bool TryRead(string path, out DateTime? time)
        {
            time = null;
            try
            {
                if (!File.Exists(path)) return true; // 文件不存在视为无候选，不算损坏

                var content = File.ReadAllText(path).Trim();
                var parts = content.Split('.');
                if (parts.Length != 2)
                    return false;

                var ticks = long.Parse(parts[0]);
                var storedHmac = parts[1];
                if (storedHmac != ComputeHmac(parts[0]))
                    return false; // HMAC 校验失败

                time = new DateTime(ticks, DateTimeKind.Utc);
                return true;
            }
            catch
            {
                return false; // 解析失败视为损坏
            }
        }

        private enum ReadState
        {
            Ok,
            SingleCorrupt,
            AllCorrupt,
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
                // 原子写：先写临时文件再替换，进程退出/崩溃不会留下截断的坏文件
                var ticks = utcTime.Ticks.ToString();
                var hmac = ComputeHmac(ticks);
                var tmpPath = path + ".tmp";
                File.WriteAllText(tmpPath, $"{ticks}.{hmac}");
                if (File.Exists(path))
                {
                    File.Replace(tmpPath, path, null);
                }
                else
                {
                    File.Move(tmpPath, path);
                }
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
