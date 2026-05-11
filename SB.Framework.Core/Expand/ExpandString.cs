namespace SB.Framework.Core.Expand
{
    public static class ExpandString
    {
        /// <summary>
        /// 去除开头的指定字符串 , 方法不会改变字符串本身
        /// </summary>
        /// <param name="str">要操作的字符串</param>
        /// <param name="prefix">要去除的字符</param>
        public static string TrimStartsPrefix(this string str, string prefix)
        {
            if (str != null && str.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return str.Substring(prefix.Length);
            return str;
        }
    }
}