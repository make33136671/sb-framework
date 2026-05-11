using SB.Framework.Core.Util;

namespace SB.Framework.Core.Expand
{
    public static class ExpandDateTime
    {
        /// <summary>
        /// 时间转换Unix时间戳（秒）
        /// </summary>
        public static int GetTimestamp(this DateTime datetime)
        {
            return (int)AppTime.ToTimestamp(datetime);
        }

        /// <summary>
        /// 时间转换毫秒时间戳
        /// </summary>
        public static long GetTimestampMillisecond(this DateTime datetime)
        {
            return AppTime.ToTimestampMs(datetime);
        }

        /// <summary>
        /// Unix时间戳转DateTime（自动处理秒/毫秒）
        /// </summary>
        public static DateTime GetDatetime(this long timestamp)
        {
            if (timestamp > 1_000_000_000_000)
                return AppTime.FromTimestampMs(timestamp);

            return AppTime.FromTimestamp(timestamp);
        }

        /// <summary>
        /// Unix时间戳（秒）转DateTime
        /// </summary>
        public static DateTime GetDatetime(this int timestamp)
        {
            return AppTime.FromTimestamp(timestamp);
        }
    }
}
