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
    }
}
