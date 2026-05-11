using System.Security.Cryptography;
using System.Text;

namespace SB.Framework.Core.Util
{
    public static class EncryptionUtil
    {
        private static string GetKey()
        {
            var key = AppGlobal.AppSettings?.EncryptionKey;
            if (string.IsNullOrWhiteSpace(key))
                throw new InvalidOperationException("未配置 EncryptionKey，请在 appsettings.json 中设置 EncryptionKey（至少 16 个字符）");
            if (key.Length < 16)
                throw new InvalidOperationException("EncryptionKey 长度不足，至少需要 16 个字符");
            // AES requires 16/24/32 bytes key
            return key.PadRight(32)[..32];
        }

        public static string AesEncrypt(string plainText)
        {
            if (string.IsNullOrEmpty(plainText)) return plainText;

            using var aes = Aes.Create();
            aes.Key = Encoding.UTF8.GetBytes(GetKey());
            aes.GenerateIV();

            using var encryptor = aes.CreateEncryptor();
            var plainBytes = Encoding.UTF8.GetBytes(plainText);
            var cipherBytes = encryptor.TransformFinalBlock(plainBytes, 0, plainBytes.Length);

            // IV + CipherText
            var result = new byte[aes.IV.Length + cipherBytes.Length];
            Buffer.BlockCopy(aes.IV, 0, result, 0, aes.IV.Length);
            Buffer.BlockCopy(cipherBytes, 0, result, aes.IV.Length, cipherBytes.Length);

            return Convert.ToBase64String(result);
        }

        public static string AesDecrypt(string cipherText)
        {
            if (string.IsNullOrEmpty(cipherText)) return cipherText;

            var fullCipher = Convert.FromBase64String(cipherText);

            using var aes = Aes.Create();
            aes.Key = Encoding.UTF8.GetBytes(GetKey());

            var iv = new byte[aes.BlockSize / 8];
            var cipher = new byte[fullCipher.Length - iv.Length];
            Buffer.BlockCopy(fullCipher, 0, iv, 0, iv.Length);
            Buffer.BlockCopy(fullCipher, iv.Length, cipher, 0, cipher.Length);
            aes.IV = iv;

            using var decryptor = aes.CreateDecryptor();
            var plainBytes = decryptor.TransformFinalBlock(cipher, 0, cipher.Length);
            return Encoding.UTF8.GetString(plainBytes);
        }

        public static string MaskCardNumber(string cardNumber)
        {
            if (string.IsNullOrEmpty(cardNumber) || cardNumber.Length < 8)
                return cardNumber;
            return cardNumber[..4] + new string('*', cardNumber.Length - 8) + cardNumber[^4..];
        }
    }
}
