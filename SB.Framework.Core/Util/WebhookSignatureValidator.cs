using System.Security.Cryptography;
using System.Text;

namespace SB.Framework.Core.Util;

/// <summary>
/// Webhook 签名校验工具：HMAC-SHA256（Telegram Bot webhook secret_token、Web App initData 等场景）。
/// 使用常量时间比较防时序攻击。
/// </summary>
public static class WebhookSignatureValidator
{
    /// <summary>
    /// 校验 HMAC-SHA256 签名（payload + secret → 比对 signature）。
    /// </summary>
    /// <param name="payload">原始负载（参与签名的原文，UTF-8）</param>
    /// <param name="signature">待校验的签名（hex 字符串，大小写不敏感）</param>
    /// <param name="secret">共享密钥（UTF-8）</param>
    /// <returns>true=签名匹配</returns>
    public static bool ValidateHmacSha256(string payload, string signature, string secret)
    {
        if (string.IsNullOrEmpty(payload) || string.IsNullOrEmpty(signature) || string.IsNullOrEmpty(secret))
            return false;

        var expected = ComputeHmacSha256Hex(payload, secret);
        return ConstantTimeEquals(expected, signature);
    }

    /// <summary>
    /// 校验 HMAC-SHA256 签名（payload + secret → 比对 signature 字节）。
    /// </summary>
    /// <param name="payloadBytes">原始负载字节</param>
    /// <param name="signatureBytes">待校验的签名字节</param>
    /// <param name="secretBytes">共享密钥字节</param>
    /// <returns>true=签名匹配</returns>
    public static bool ValidateHmacSha256(byte[] payloadBytes, byte[] signatureBytes, byte[] secretBytes)
    {
        if (payloadBytes == null || payloadBytes.Length == 0
            || signatureBytes == null || signatureBytes.Length == 0
            || secretBytes == null || secretBytes.Length == 0)
            return false;

        Span<byte> expected = stackalloc byte[32];
        using var hmac = new HMACSHA256(secretBytes);
        hmac.TryComputeHash(payloadBytes, expected, out _);
        return ConstantTimeEquals(expected, signatureBytes);
    }

    /// <summary>
    /// 计算 HMAC-SHA256 并返回小写 hex 字符串。
    /// </summary>
    public static string ComputeHmacSha256Hex(string payload, string secret)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>
    /// 常量时间字符串比较（防时序攻击）。大小写不敏感（hex 签名场景）。
    /// </summary>
    public static bool ConstantTimeEquals(string a, string b)
    {
        if (a == null || b == null || a.Length != b.Length) return false;
        var diff = 0;
        for (var i = 0; i < a.Length; i++)
            diff |= char.ToLowerInvariant(a[i]) ^ char.ToLowerInvariant(b[i]);
        return diff == 0;
    }

    /// <summary>
    /// 常量时间字节比较（防时序攻击）。
    /// </summary>
    public static bool ConstantTimeEquals(ReadOnlySpan<byte> a, ReadOnlySpan<byte> b)
    {
        if (a.Length != b.Length) return false;
        var diff = 0;
        for (var i = 0; i < a.Length; i++)
            diff |= a[i] ^ b[i];
        return diff == 0;
    }
}
