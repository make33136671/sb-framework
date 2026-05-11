using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SB.Framework.Core.Util;

namespace SB.Framework.Core.License
{
    public interface ILicenseService
    {
        /// <summary>
        /// 当前授权状态（含实时过期检测）
        /// </summary>
        LicenseResult CurrentStatus { get; }

        /// <summary>
        /// 验证指定的授权Key（纯签名验证，不校验客户端标识）
        /// </summary>
        LicenseResult Validate(string licenseKey, string? expectedProduct = null);

        /// <summary>
        /// 验证授权Key并校验客户端标识
        /// </summary>
        LicenseResult ValidateWithIdentity(string licenseKey, string? clientKey, string? hardwareId, string? expectedProduct = null);

        /// <summary>
        /// 重新验证当前配置的授权Key（用于运行时刷新）
        /// </summary>
        void Refresh(string? newLicenseKey = null);

        /// <summary>
        /// 授权系统是否已启用
        /// </summary>
        bool IsEnabled { get; }
    }

    public class LicenseService : ILicenseService, IDisposable
    {
        private readonly RSA _rsa;
        private readonly string? _productName;
        private string _licenseKey;
        private LicenseResult _cached;

        public bool IsEnabled => true;

        public LicenseService(string publicKeyXml, string licenseKey, string? productName = null)
        {
            _rsa = RSA.Create();
            _rsa.FromXmlString(publicKeyXml);
            _licenseKey = licenseKey;
            _productName = productName;

            // 验证时带上本机标识
            var clientKey = ClientKeyService.GetClientKey();
            var hardwareId = HardwareIdService.GetHardwareId();
            _cached = ValidateWithIdentity(licenseKey, clientKey, hardwareId, productName);
        }

        public LicenseResult CurrentStatus
        {
            get
            {
                if (_cached.IsValid && _cached.ExpiresAt.HasValue && AppTime.UtcNow > _cached.ExpiresAt.Value)
                {
                    // 过期时保留 Payload 信息（用于路由控制）
                    _cached = new LicenseResult
                    {
                        IsValid = false,
                        IsExpired = true,
                        Message = $"授权已过期，到期时间: {_cached.ExpiresAt.Value:yyyy-MM-dd HH:mm:ss} UTC",
                        ExpiresAt = _cached.ExpiresAt,
                        IssuedAt = _cached.IssuedAt,
                        ProductEn = _cached.ProductEn,
                        ProductCn = _cached.ProductCn,
                        Domain = _cached.Domain,
                        Domains = _cached.Domains,
                        ClientKey = _cached.ClientKey,
                        HardwareId = _cached.HardwareId,
                        VerifyMode = _cached.VerifyMode,
                        AllowedRoutes = _cached.AllowedRoutes,
                        AllowAdminAfterExpiry = _cached.AllowAdminAfterExpiry,
                        AllowClientApiAfterExpiry = _cached.AllowClientApiAfterExpiry
                    };
                }

                return _cached;
            }
        }

        public void Refresh(string? newLicenseKey = null)
        {
            if (newLicenseKey != null)
                _licenseKey = newLicenseKey;

            var clientKey = ClientKeyService.GetClientKey();
            var hardwareId = HardwareIdService.GetHardwareId();
            _cached = ValidateWithIdentity(_licenseKey, clientKey, hardwareId, _productName);
        }

        public LicenseResult Validate(string licenseKey, string? expectedProduct = null)
        {
            return ValidateInternal(licenseKey, expectedProduct, null, null, false);
        }

        public LicenseResult ValidateWithIdentity(string licenseKey, string? clientKey, string? hardwareId, string? expectedProduct = null)
        {
            return ValidateInternal(licenseKey, expectedProduct, clientKey, hardwareId, true);
        }

        private LicenseResult ValidateInternal(string licenseKey, string? expectedProduct, string? clientKey, string? hardwareId, bool checkIdentity)
        {
            if (string.IsNullOrWhiteSpace(licenseKey))
                return LicenseResult.Fail("未配置授权Key");

            try
            {
                var dotIndex = licenseKey.IndexOf('.');
                if (dotIndex <= 0 || dotIndex >= licenseKey.Length - 1)
                    return LicenseResult.Fail("授权Key格式无效");

                var payloadBytes = Convert.FromBase64String(licenseKey[..dotIndex]);
                var signatureBytes = Convert.FromBase64String(licenseKey[(dotIndex + 1)..]);

                if (!_rsa.VerifyData(payloadBytes, signatureBytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1))
                    return LicenseResult.Fail($"授权Key签名验证失败{BuildSignatureFailureDiagnosis(payloadBytes, signatureBytes)}");

                var payload = JsonSerializer.Deserialize<LicensePayload>(payloadBytes);
                if (payload?.ExpiresAt is null)
                    return LicenseResult.Fail("授权Key内容无效");

                if (!DateTime.TryParse(payload.ExpiresAt, null, System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out var expiresAt))
                    return LicenseResult.Fail("授权Key到期时间格式无效");

                DateTime? issuedAt = DateTime.TryParse(payload.IssuedAt, null, System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out var issued) ? issued : null;

                // 产品名称验证
                if (!string.IsNullOrEmpty(expectedProduct)
                    && !string.Equals(payload.ProductEn, expectedProduct, StringComparison.OrdinalIgnoreCase))
                    return LicenseResult.Fail($"授权产品不匹配，期望: {expectedProduct}，实际: {payload.ProductEn}");

                // 客户端标识验证
                if (checkIdentity)
                {
                    var identityResult = VerifyIdentity(payload, clientKey, hardwareId);
                    if (identityResult != null)
                        return identityResult;
                }

                // 过期检查
                if (AppTime.UtcNow > expiresAt)
                    return LicenseResult.Expired(expiresAt, issuedAt, payload);

                return LicenseResult.Ok(
                    $"授权有效，到期时间: {expiresAt:yyyy-MM-dd HH:mm:ss} UTC",
                    expiresAt, issuedAt, payload);
            }
            catch (FormatException)
            {
                return LicenseResult.Fail("授权Key Base64编码无效");
            }
            catch
            {
                return LicenseResult.Fail("授权Key解析失败");
            }
        }

        /// <summary>
        /// 字段非空就验证, 空则跳过. VerifyMode 不再读取.
        /// 管理员签发时哪些字段填了, 哪些就被强制匹配.
        /// </summary>
        private LicenseResult? VerifyIdentity(LicensePayload payload, string? clientKey, string? hardwareId)
        {
            if (!string.IsNullOrEmpty(payload.ClientKey)
                && !string.Equals(payload.ClientKey, clientKey, StringComparison.OrdinalIgnoreCase))
                return LicenseResult.Fail("客户端密钥不匹配");

            if (!string.IsNullOrEmpty(payload.HardwareId)
                && !string.Equals(payload.HardwareId, hardwareId, StringComparison.OrdinalIgnoreCase))
                return LicenseResult.Fail("硬件ID不匹配");

            return null;
        }

        /// <summary>
        /// 签名验证失败时构造诊断信息: 公钥指纹 + Payload 解析结果 + 字节长度.
        /// 公钥指纹用于对比签发端和当前端是否同一把钥匙 (最常见的失败原因).
        /// </summary>
        private string BuildSignatureFailureDiagnosis(byte[] payloadBytes, byte[] signatureBytes)
        {
            var sb = new StringBuilder();
            sb.Append(" [");

            // 本机公钥指纹 (SubjectPublicKeyInfo 的 SHA256 前 16 位 hex)
            try
            {
                var pubInfo = _rsa.ExportSubjectPublicKeyInfo();
                using var sha = SHA256.Create();
                var fingerprint = Convert.ToHexString(sha.ComputeHash(pubInfo))[..16].ToLowerInvariant();
                sb.Append($"本机公钥指纹={fingerprint}, ");
            }
            catch { sb.Append("本机公钥指纹=读取失败, "); }

            sb.Append($"payload={payloadBytes.Length}字节, signature={signatureBytes.Length}字节");

            // 尝试解析 Payload 内容, 能解出来说明 Key 本身结构正常, 仅签名对不上
            try
            {
                var payload = JsonSerializer.Deserialize<LicensePayload>(payloadBytes);
                if (payload != null)
                {
                    sb.Append($", Payload: ProductEn={payload.ProductEn ?? "-"}");
                    if (payload.Domains != null && payload.Domains.Count > 0)
                        sb.Append($", Domains=[{string.Join(",", payload.Domains)}]");
                    else if (!string.IsNullOrEmpty(payload.Domain))
                        sb.Append($", Domain={payload.Domain}");
                    sb.Append($", IssuedAt={payload.IssuedAt ?? "-"}, ExpiresAt={payload.ExpiresAt ?? "-"}");
                }
            }
            catch (Exception ex)
            {
                sb.Append($", Payload解析失败: {ex.Message}");
            }

            sb.Append("]. 可能原因: 1)签发方私钥与当前系统公钥不是同一对 (最常见); 2)Key 被手动修改或粘贴时混入了空白字符; 3)公钥文件 license_public.key 被替换");
            return sb.ToString();
        }

        public void Dispose() => _rsa.Dispose();
    }

    /// <summary>
    /// 未配置公钥 — 授权已启用但缺少公钥，所有请求视为授权无效
    /// </summary>
    public class MissingKeyLicenseService : ILicenseService
    {
        private readonly string _message;
        private readonly string _product;

        public MissingKeyLicenseService(string message)
        {
            _message = message;
            _product = AppGlobal.AppSettings?.ProductEn ?? "";
        }

        public bool IsEnabled => true;

        public LicenseResult CurrentStatus => new()
        {
            IsValid = false,
            Message = _message,
            ProductEn = _product,
            ProductCn = AppGlobal.AppSettings?.ProductCn ?? "",
        };

        public LicenseResult Validate(string licenseKey, string? expectedProduct = null)
            => LicenseResult.Fail(_message);

        public LicenseResult ValidateWithIdentity(string licenseKey, string? clientKey, string? hardwareId, string? expectedProduct = null)
            => LicenseResult.Fail(_message);

        public void Refresh(string? newLicenseKey = null) { }
    }

    /// <summary>
    /// 空实现 — 授权系统未启用时使用
    /// </summary>
    public class NullLicenseService : ILicenseService
    {
        public bool IsEnabled => false;

        public LicenseResult CurrentStatus => LicenseResult.Ok(
            "授权系统未启用", DateTime.MaxValue, null,
            new LicensePayload { ProductEn = AppGlobal.AppSettings?.ProductEn ?? "" });

        public LicenseResult Validate(string licenseKey, string? expectedProduct = null)
            => LicenseResult.Fail("授权系统未配置公钥");

        public LicenseResult ValidateWithIdentity(string licenseKey, string? clientKey, string? hardwareId, string? expectedProduct = null)
            => LicenseResult.Fail("授权系统未配置公钥");

        public void Refresh(string? newLicenseKey = null) { }
    }
}
