// SB.Core/Util/AppTime.cs
namespace SB.Framework.Core.Util
{
    /// <summary>
    /// 项目级时间工具类，替代 DateTime.Now/Today/UtcNow。
    /// 服务器时区为 UTC，项目时区通过 appsettings.json 的 TimeZone 配置。
    /// </summary>
    public static class AppTime
    {
        private static TimeZoneInfo _zone = TimeZoneInfo.Utc;

        /// <summary>
        /// 启动时初始化项目时区（从 appsettings.json 读取 TimeZone）
        /// </summary>
        public static void Initialize(string? timeZoneId)
        {
            if (!string.IsNullOrEmpty(timeZoneId))
                _zone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        }

        /// <summary>当前项目时区信息</summary>
        public static TimeZoneInfo Zone => _zone;

        #pragma warning disable SB0001
        /// <summary>当前 UTC 时间（存库、比较用）</summary>
        public static DateTime UtcNow => DateTime.UtcNow;
        #pragma warning restore SB0001

        /// <summary>当前项目时区时间（业务逻辑用）</summary>
        public static DateTime Now => TimeZoneInfo.ConvertTimeFromUtc(UtcNow, _zone);

        /// <summary>项目时区的今天零点</summary>
        public static DateTime Today => Now.Date;

        /// <summary>UTC → 项目时区</summary>
        public static DateTime ToLocal(DateTime utc)
            => TimeZoneInfo.ConvertTimeFromUtc(utc, _zone);

        /// <summary>项目时区 → UTC</summary>
        public static DateTime ToUtc(DateTime local)
            => TimeZoneInfo.ConvertTimeToUtc(
                DateTime.SpecifyKind(local, DateTimeKind.Unspecified), _zone);

        /// <summary>UTC DateTime → Unix 时间戳（秒）</summary>
        public static long ToTimestamp(DateTime utc)
            => new DateTimeOffset(utc, TimeSpan.Zero).ToUnixTimeSeconds();

        /// <summary>UTC DateTime → Unix 时间戳（毫秒）</summary>
        public static long ToTimestampMs(DateTime utc)
            => new DateTimeOffset(utc, TimeSpan.Zero).ToUnixTimeMilliseconds();

        /// <summary>Unix 时间戳（秒）→ UTC DateTime</summary>
        public static DateTime FromTimestamp(long timestamp)
            => DateTimeOffset.FromUnixTimeSeconds(timestamp).UtcDateTime;

        /// <summary>Unix 时间戳（毫秒）→ UTC DateTime</summary>
        public static DateTime FromTimestampMs(long timestampMs)
            => DateTimeOffset.FromUnixTimeMilliseconds(timestampMs).UtcDateTime;
    }
}
