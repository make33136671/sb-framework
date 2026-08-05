using System.Text;
using System.Security.Cryptography;

namespace SB.Framework.Core.Util
{
    /// <summary>
    /// 安全加密工具
    /// </summary>
    public class SecurityUtil
    {
        /// <summary>
        /// 字符串转Md5
        /// </summary>
        /// <param name="content">需要加密的字符串</param>
        /// <returns>加密后的字符串</returns>
        public static string StrToMD5(string content)
        {
            using (var md5 = MD5.Create())
            {
                var result = md5.ComputeHash(Encoding.UTF8.GetBytes(content));
                var strResult = BitConverter.ToString(result);
                string result3 = strResult.Replace("-", "");

                return result3;
            }
        }

        /// <summary>
        /// 通用敏感值脱敏：保留首尾各 visibleStart/visibleEnd 位，中间用 maskChar 替换（默认最多 8 个 *）。
        /// 用于日志打印 token / API key / 手机号 / 邮箱等，避免明文泄露。
        /// </summary>
        /// <param name="secret">原始敏感值</param>
        /// <param name="visibleStart">保留前缀位数（默认 4）</param>
        /// <param name="visibleEnd">保留后缀位数（默认 4）</param>
        /// <param name="maskChar">掩码字符（默认 '*'）</param>
        public static string MaskSecret(string? secret, int visibleStart = 4, int visibleEnd = 4, char maskChar = '*')
        {
            if (string.IsNullOrEmpty(secret)) return "";
            if (secret.Length <= visibleStart + visibleEnd)
                return new string(maskChar, secret.Length);

            var start = secret.Substring(0, visibleStart);
            var end = secret.Substring(secret.Length - visibleEnd);
            var maskedLen = Math.Min(secret.Length - visibleStart - visibleEnd, 8);
            return $"{start}{new string(maskChar, maskedLen)}{end}";
        }

        /// <summary>
        /// Bot / 访问令牌脱敏（令牌通常较长，保留前 4 + 后 4）。
        /// </summary>
        public static string MaskToken(string? token) => MaskSecret(token, 4, 4);

        /// <summary>
        /// 手机号脱敏（保留前 3 + 后 4）。
        /// </summary>
        public static string MaskPhone(string? phone) => MaskSecret(phone, 3, 4);
    }
}
