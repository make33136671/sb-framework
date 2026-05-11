namespace SB.Framework.Core.Util
{
    /// <summary>
    /// 密码工具类 - 支持加密和明文两种密码存储模式
    /// </summary>
    public static class PasswordUtil
    {
        /// <summary>
        /// 判断是否采用加密模式
        /// </summary>
        /// <returns>true 表示加密模式，false 表示明文模式</returns>
        public static bool IsEncryptedMode()
        {
            return AppGlobal.AppSettings?.PasswordConfig?.StorageMode == "Encrypted";
        }

        /// <summary>
        /// 加密密码（根据配置决定是否真正加密）
        /// </summary>
        /// <param name="password">原始密码</param>
        /// <returns>加密后的密码或原始密码（根据配置）</returns>
        public static string EncryptPassword(string password)
        {
            if (string.IsNullOrEmpty(password))
                return password;

            if (!IsEncryptedMode())
                return password;

            return BCrypt.Net.BCrypt.HashPassword(password);
        }

        /// <summary>
        /// 验证密码（根据配置决定是否使用BCrypt验证）
        /// 支持从明文密码迁移到加密密码的过渡期
        /// </summary>
        /// <param name="inputPassword">用户输入的密码</param>
        /// <param name="storedPassword">存储在数据库中的密码</param>
        /// <returns>密码是否正确</returns>
        public static bool VerifyPassword(string inputPassword, string storedPassword)
        {
            if (string.IsNullOrEmpty(inputPassword) || string.IsNullOrEmpty(storedPassword))
                return false;

            if (!IsEncryptedMode())
            {
                // 明文模式：直接比较
                return inputPassword == storedPassword;
            }

            // 加密模式：使用BCrypt验证
            try
            {
                return BCrypt.Net.BCrypt.Verify(inputPassword, storedPassword);
            }
            catch (BCrypt.Net.SaltParseException)
            {
                // 处理过渡情况：如果BCrypt验证失败（说明存储的不是BCrypt哈希）
                // 检查是否是明文密码（用于从明文升级到加密的情况）
                if (inputPassword == storedPassword)
                {
                    return true;
                }
                return false;
            }
            catch
            {
                // 其他异常情况，密码验证失败
                return false;
            }
        }
    }
}
