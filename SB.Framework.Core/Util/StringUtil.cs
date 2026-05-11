using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SB.Framework.Core.Util
{
    /// <summary>
    /// 字符串处理工具
    /// </summary>
    public class StringUtil
    {
        /// <summary>
        /// 生成一个时间开头的订单号,后面拼接4位随机数字
        /// </summary>
        /// <param name="randomLength">随机数字长度</param>
        public static string GenerateDateTimeNo(int randomLength = 4)
        {
            return AppTime.Now.ToString("yyyyMMddHHmmssfff") + GenerateNumberNo(randomLength);
        }

        /// <summary>
        /// 生成指定长度的数字英文混合字符串
        /// </summary>
        /// <param name="numberLength">生成长度</param>
        public static string GenerateStringCode(int numberLength = 6)
        {
            string[] strArr = new string[] { "0", "1", "2", "3", "4", "5", "6", "7", "8", "9", "A", "B", "C", "D", "E", "F", "G", "H", "I", "J", "K", "L", "M", "N", "O", "P", "L", "M", "N", "O", "P", "Q", "R", "S", "T", "U", "V", "W", "X", "Y", "Z" };
            StringBuilder sb = new StringBuilder();

            for (int i = 0; i < numberLength; i++)
            {
                sb.Append(strArr[Random.Shared.Next(0, strArr.Length)]);
            }


            return sb.ToString();
        }

        /// <summary>
        /// 生成一个6为纯数字随机数
        /// </summary>
        /// <param name="numberLength">生成长度</param>
        public static string GenerateNumberNo(int numberLength = 6)
        {
            string r = "";

            switch (numberLength)
            {
                default:
                case 1:
                    r = Random.Shared.Next(0, 10).ToString();
                    break;

                case 2:
                    r = Random.Shared.Next(10, 100).ToString();
                    break;

                case 3:
                    r = Random.Shared.Next(100, 1000).ToString();
                    break;

                case 4:
                    r = Random.Shared.Next(1000, 10000).ToString();
                    break;

                case 5:
                    r = Random.Shared.Next(10000, 100000).ToString();
                    break;

                case 6:
                    r = Random.Shared.Next(100000, 1000000).ToString();
                    break;

                case 7:
                    r = Random.Shared.Next(1000000, 10000000).ToString();
                    break;

                case 8:
                    r = Random.Shared.Next(10000000, 100000000).ToString();
                    break;
            }
            return r;
        }

        /// <summary>
        /// 手机号脱敏
        /// </summary>
        /// <param name="str">手机号码</param>
        public static string PhoneDesensitization(string str)
        {
            if (string.IsNullOrEmpty(str))
            {
                return "";
            }
            StringBuilder sb = new StringBuilder();
            if (str.Length < 3)
            {
                sb.Append(str);
                return sb.ToString();
            }
            else
            {
                sb.Append(str.Substring(0, 3));

                if (str.Length >= 7)
                {
                    sb.Append("****");

                    if (str.Length > 7)
                    {
                        sb.Append(str.Substring(7, str.Length - 8));
                    }
                }
            }


            return sb.ToString();
        }
    }
}
