using FreeSql;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SB.Framework.Core;
using SB.Framework.Core.Attribute;
using SB.Framework.Core.License;
using SB.Framework.Core.Util;
using SB.Framework.Models;
using SB.Framework.Models.Dto;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SB.Feature.LicenseManage
{
    /// <summary>
    /// 授权管理服务（服务端：生成授权、管理客户、处理申请）
    /// </summary>
    [AutoDependencyInjection]
    public class LicenseManageService
    {
        private readonly IFreeSql _fsql;
        private readonly ILogger<LicenseManageService> _logger;
        private readonly IHostEnvironment _env;
        private readonly LicenseManageOptions _opt;
        private const string PrivateKeyFile = "license_private.key";
        private const string PublicKeyFile = "license_public.key";

        public LicenseManageService(IFreeSql fsql, ILogger<LicenseManageService> logger, IHostEnvironment env, IOptions<LicenseManageOptions> opt)
        {
            _fsql = fsql;
            _logger = logger;
            _env = env;
            _opt = opt.Value;
        }

        /// <summary>
        /// 密钥文件目录: 优先读 SB_LICENSE_KEY_DIR 环境变量 (和 csproj 里 EmbedLicensePublicKey Target 保持一致),
        /// 其次读 LicenseManageOptions.KeyDirectory, 都未设置时回退到 ContentRootPath. 开发调试场景下通过环境变量/配置
        /// 让签发端和构建嵌入端指向同一处密钥, 避免"签发时用的公钥"和"程序集里硬编码的公钥"不一致导致本机 ApplyKey 签名验证失败.
        /// </summary>
        private string GetKeyDir()
        {
            var env = Environment.GetEnvironmentVariable("SB_LICENSE_KEY_DIR");
            if (!string.IsNullOrWhiteSpace(env)) return env;
            if (!string.IsNullOrWhiteSpace(_opt.KeyDirectory)) return _opt.KeyDirectory;
            return _env.ContentRootPath;
        }

        #region RSA 密钥管理

        /// <summary>
        /// 获取 RSA 公钥 XML（没有则自动生成密钥对）
        /// </summary>
        public virtual string GetPublicKeyXml()
        {
            var publicPath = Path.Combine(GetKeyDir(), PublicKeyFile);
            if (File.Exists(publicPath))
                return File.ReadAllText(publicPath);

            // 自动生成密钥对
            GenerateKeyPair();
            return File.ReadAllText(publicPath);
        }

        /// <summary>
        /// 生成 RSA-2048 密钥对
        /// </summary>
        public virtual (string PublicKeyXml, string PrivateKeyXml) GenerateKeyPair()
        {
            using var rsa = RSA.Create(2048);
            var privateXml = rsa.ToXmlString(true);
            var publicXml = rsa.ToXmlString(false);

            var privatePath = Path.Combine(GetKeyDir(), PrivateKeyFile);
            var publicPath = Path.Combine(GetKeyDir(), PublicKeyFile);

            File.WriteAllText(privatePath, privateXml);
            File.WriteAllText(publicPath, publicXml);

            _logger.LogInformation("已生成新的 RSA 密钥对");
            return (publicXml, privateXml);
        }

        /// <summary>
        /// 检查密钥对是否已存在
        /// </summary>
        public virtual bool HasKeyPair()
        {
            var privatePath = Path.Combine(GetKeyDir(), PrivateKeyFile);
            return File.Exists(privatePath);
        }

        #endregion

        #region 网站客户管理

        /// <summary>
        /// 根据客户端信息查找或创建客户
        /// </summary>
        public virtual LicenseClient FindOrCreateClient(string domain, string? clientKey, string? hardwareId, string? serverIP,
            string? productEn = null, string? productCn = null, string? machineName = null, string? osDescription = null,
            int? processorCount = null, string? macAddresses = null, string? actualDomain = null)
        {
            // 严格双键匹配: HardwareId + ClientKey 都非空且都相等才视为同一客户.
            // 任一缺失或不一致都走新建分支, 避免同机多项目因共享 HardwareId 被错误合并成同一条记录.
            LicenseClient? client = null;
            if (!string.IsNullOrEmpty(hardwareId) && !string.IsNullOrEmpty(clientKey))
            {
                client = _fsql.Select<LicenseClient>()
                    .Where(c => c.HardwareId == hardwareId && c.ClientKey == clientKey)
                    .First();
            }

            if (client != null)
            {
                // 更新最新信息
                bool changed = false;
                if (!string.IsNullOrEmpty(domain) && client.Domain != domain) { client.Domain = domain; changed = true; }
                if (!string.IsNullOrEmpty(serverIP) && client.ServerIP != serverIP) { client.ServerIP = serverIP; changed = true; }
                if (!string.IsNullOrEmpty(clientKey) && client.ClientKey != clientKey) { client.ClientKey = clientKey; changed = true; }
                if (!string.IsNullOrEmpty(hardwareId) && client.HardwareId != hardwareId) { client.HardwareId = hardwareId; changed = true; }
                if (!string.IsNullOrEmpty(productEn) && client.ProductEn != productEn) { client.ProductEn = productEn; changed = true; }
                if (!string.IsNullOrEmpty(productCn) && client.ProductCn != productCn) { client.ProductCn = productCn; changed = true; }
                if (!string.IsNullOrEmpty(machineName) && client.MachineName != machineName) { client.MachineName = machineName; changed = true; }
                if (!string.IsNullOrEmpty(osDescription) && client.OsDescription != osDescription) { client.OsDescription = osDescription; changed = true; }
                if (processorCount.HasValue && client.ProcessorCount != processorCount) { client.ProcessorCount = processorCount; changed = true; }
                if (!string.IsNullOrEmpty(macAddresses) && client.MacAddresses != macAddresses) { client.MacAddresses = macAddresses; changed = true; }
                if (!string.IsNullOrEmpty(actualDomain) && client.LastActualDomain != actualDomain) { client.LastActualDomain = actualDomain; changed = true; }

                if (changed)
                {
                    client.UpdatedTime = AppTime.Now;
                    _fsql.Update<LicenseClient>().SetSource(client).ExecuteAffrows();
                }
                return client;
            }

            // 创建新客户
            client = new LicenseClient
            {
                ProductEn = productEn,
                ProductCn = productCn,
                Domain = domain ?? "",
                LastActualDomain = actualDomain,
                ServerIP = serverIP,
                ClientKey = clientKey,
                HardwareId = hardwareId,
                MachineName = machineName,
                OsDescription = osDescription,
                ProcessorCount = processorCount,
                MacAddresses = macAddresses,
                Status = 0
            };
            var newId = (int)_fsql.Insert(client).ExecuteIdentity();
            client.Id = newId;
            _logger.LogInformation("自动创建网站客户: {ProductCn}({ProductEn}) - {Domain} (HardwareId: {HardwareId}, Id: {Id})", productCn, productEn, domain, hardwareId, newId);
            return client;
        }

        /// <summary>
        /// 客户列表
        /// </summary>
        public virtual Result ClientList(int page, int pageSize, string? keyword)
        {
            var query = _fsql.Select<LicenseClient>()
                .WhereIf(!string.IsNullOrEmpty(keyword), c =>
                    c.ProductCn.Contains(keyword!) || c.ProductEn.Contains(keyword!) || c.Domain.Contains(keyword!))
                .OrderByDescending(c => c.Id);

            var clients = query.Count(out long total).Page(page, pageSize).ToList();

            // 批量查询验证统计
            var clientIds = clients.Select(c => c.Id).ToList();
            var verifyStats = _fsql.Select<LicenseVerifyLog>()
                .Where(v => clientIds.Contains(v.ClientId))
                .GroupBy(v => v.ClientId)
                .ToList(g => new
                {
                    ClientId = g.Key,
                    Count = g.Count(),
                    LastTime = g.Max(g.Value.CreatedTime)
                });

            var statsMap = verifyStats.ToDictionary(s => s.ClientId);
            var data = clients.Select(c =>
            {
                statsMap.TryGetValue(c.Id, out var stat);
                return new
                {
                    c.Id,
                    c.ProductEn,
                    c.ProductCn,
                    c.Domain,
                    c.LastActualDomain,
                    c.ServerIP,
                    c.ClientKey,
                    c.HardwareId,
                    c.MachineName,
                    c.OsDescription,
                    c.ProcessorCount,
                    c.MacAddresses,
                    c.Remark,
                    c.Status,
                    c.IsDeleted,
                    c.CreatedTime,
                    c.UpdatedTime,
                    c.LastHeartbeat,
                    VerifyCount = stat?.Count ?? 0,
                    LastVerifyTime = stat?.LastTime,
                };
            }).ToList();

            return Result.Page(data, total);
        }

        /// <summary>
        /// 客户详情
        /// </summary>
        public virtual Result ClientDetail(int id)
        {
            var client = _fsql.Select<LicenseClient>().Where(c => c.Id == id).First();
            if (client == null) return Result.Fail("客户不存在");
            return Result.Success(client);
        }

        /// <summary>
        /// 编辑客户
        /// </summary>
        public virtual Result ClientEdit(int id, string? remark, int? status)
        {
            var client = _fsql.Select<LicenseClient>().Where(c => c.Id == id).First();
            if (client == null) return Result.Fail("客户不存在");

            if (remark != null) client.Remark = remark;
            if (status.HasValue) client.Status = status.Value;
            client.UpdatedTime = AppTime.Now;

            _fsql.Update<LicenseClient>().SetSource(client).ExecuteAffrows();
            return Result.Success("更新成功");
        }

        /// <summary>
        /// 软删除客户. 历史 LicenseRecord/VerifyLog 保留.
        /// 全局过滤器已注册, 软删后 ClientList 自动过滤不展示, 且无法再签发.
        /// 如需恢复, 管理员手动改库 UPDATE SB_License_Client SET IsDeleted=0 WHERE Id=X.
        /// </summary>
        public virtual Result DeleteClient(int id)
        {
            var c = _fsql.Select<LicenseClient>().Where(x => x.Id == id).First();
            if (c == null) return Result.Fail("客户不存在");

            c.IsDeleted = true;
            c.UpdatedTime = AppTime.Now;
            _fsql.Update<LicenseClient>().SetSource(c).ExecuteAffrows();
            _logger.LogInformation("软删客户 {ClientId} - {ProductEn}", id, c.ProductEn);
            return Result.Success("已删除");
        }

        #endregion

        #region 授权申请管理

        /// <summary>
        /// 创建授权申请
        /// </summary>
        public virtual Result CreateRequest(string domain, string? productEn, string? productCn, string? clientKey, string? hardwareId,
            string? machineName = null, string? osDescription = null, int? processorCount = null, string? macAddresses = null, string? serverIP = null)
        {
            var client = FindOrCreateClient(domain, clientKey, hardwareId, serverIP, productEn, productCn, machineName, osDescription, processorCount, macAddresses);

            // 检查是否有待处理的申请
            var pending = _fsql.Select<LicenseRequest>()
                .Where(c => c.ClientId == client.Id && c.Status == LicenseRequestStatus.Pending)
                .First();
            if (pending != null)
                return Result.Success("已有待处理的授权申请", new { requestId = pending.Id });

            var request = new LicenseRequest
            {
                ClientId = client.Id,
                Domain = domain ?? "",
                ServerIP = serverIP,
                ClientKey = clientKey,
                HardwareId = hardwareId,
                Status = LicenseRequestStatus.Pending
            };
            _fsql.Insert(request).ExecuteAffrows();

            _logger.LogInformation("收到授权申请: {Domain} (ClientId: {ClientId})", domain, client.Id);
            return Result.Success("授权申请已提交");
        }

        /// <summary>
        /// 申请列表
        /// </summary>
        public virtual Result RequestList(int page, int pageSize, int? status)
        {
            var query = _fsql.Select<LicenseRequest>()
                .Include(c => c.Client)
                .WhereIf(status.HasValue, c => (int)c.Status == status!.Value)
                .OrderByDescending(c => c.Id);

            var data = query.Count(out long total).Page(page, pageSize).ToList();
            return Result.Page(data, total);
        }

        /// <summary>
        /// 批准申请并生成授权
        /// </summary>
        public virtual Result ApproveRequest(int requestId, DateTime expiresAt, LicenseVerifyMode verifyMode, List<string>? allowedRoutes)
        {
            var request = _fsql.Select<LicenseRequest>()
                .Include(c => c.Client)
                .Where(c => c.Id == requestId)
                .First();
            if (request == null) return Result.Fail("申请不存在");
            if (request.Status != LicenseRequestStatus.Pending) return Result.Fail("申请已处理");

            // 生成授权 (LicenseVerifyMode 已废弃, 仅保留申请时作兼容. 按申请里带的字段全量填入)
            var generateResult = GenerateLicense(new IssueLicenseRequest
            {
                ClientId = request.ClientId,
                ExpiresAt = expiresAt,
                Domains = !string.IsNullOrWhiteSpace(request.Domain) ? new List<string> { request.Domain } : null,
                ProductEn = request.Client?.ProductEn,
                ClientKey = request.ClientKey,
                HardwareId = request.HardwareId,
                AllowedRoutes = allowedRoutes,
            });

            if (generateResult.Code != 0) return generateResult;

            // 更新申请状态
            var recordId = (generateResult.Data as dynamic)?.recordId;
            request.Status = LicenseRequestStatus.Approved;
            request.RecordId = (int?)recordId;
            request.ProcessedTime = AppTime.Now;
            _fsql.Update<LicenseRequest>().SetSource(request).ExecuteAffrows();

            return Result.Success("申请已批准，授权已生成");
        }

        /// <summary>
        /// 拒绝申请
        /// </summary>
        public virtual Result RejectRequest(int requestId, string? reason)
        {
            var request = _fsql.Select<LicenseRequest>().Where(c => c.Id == requestId).First();
            if (request == null) return Result.Fail("申请不存在");
            if (request.Status != LicenseRequestStatus.Pending) return Result.Fail("申请已处理");

            request.Status = LicenseRequestStatus.Rejected;
            request.RejectReason = reason;
            request.ProcessedTime = AppTime.Now;
            _fsql.Update<LicenseRequest>().SetSource(request).ExecuteAffrows();

            return Result.Success("申请已拒绝");
        }

        #endregion

        #region 授权生成

        /// <summary>
        /// 快速签发：粘贴客户端身份信息直接生成授权
        /// 自动匹配或创建产品记录，生成授权并返回 Key
        /// </summary>
        public virtual Result QuickGenerate(QuickGenerateRequest request)
        {
            if (string.IsNullOrEmpty(request.ClientKey) && string.IsNullOrEmpty(request.HardwareId))
                return Result.Fail("ClientKey 和 HardwareId 至少提供一个");

            // 计算到期时间
            DateTime expiresAt;
            if (request.ExpiresAt.HasValue)
                expiresAt = request.ExpiresAt.Value;
            else if (request.Days.HasValue && request.Days.Value > 0)
                expiresAt = AppTime.UtcNow.AddDays(request.Days.Value);
            else
                return Result.Fail("请指定有效期（天数或到期时间）");

            // 查找或创建客户
            var client = FindOrCreateClient(
                request.Domain ?? request.ProductEn ?? "",
                request.ClientKey,
                request.HardwareId,
                null,
                request.ProductEn,
                request.ProductCn);

            // QuickGenerate 按老语义映射到 IssueLicenseRequest:
            // VerifyMode=ClientKeyOnly/Both 时带 ClientKey, HardwareIdOnly/Both 时带 HardwareId
            var verifyMode = (LicenseVerifyMode)request.VerifyMode;
            var effectiveClientKey = verifyMode == LicenseVerifyMode.HardwareIdOnly ? null : request.ClientKey;
            var effectiveHardwareId = verifyMode == LicenseVerifyMode.ClientKeyOnly ? null : request.HardwareId;

            return GenerateLicense(new IssueLicenseRequest
            {
                ClientId = client.Id,
                ExpiresAt = expiresAt,
                Domains = !string.IsNullOrWhiteSpace(request.Domain) ? new List<string> { request.Domain } : null,
                ProductEn = request.ProductEn,
                ClientKey = effectiveClientKey,
                HardwareId = effectiveHardwareId,
                AllowedRoutes = null,
            });
        }

        /// <summary>
        /// 为指定客户签发授权 (统一签发入口).
        /// 各验证字段为 null/空 时, Payload 对应字段留空, 客户端验证时跳过该项.
        /// VerifyMode 字段废弃 (保留 Payload 字段兼容老 Key 解析, 新 Key 不写).
        /// </summary>
        public virtual Result GenerateLicense(IssueLicenseRequest req)
        {
            // 1. 校验
            if (req.ClientId <= 0) return Result.Fail("ClientId 必填");
            if (req.ExpiresAt <= AppTime.UtcNow) return Result.Fail("到期时间必须在未来");

            var client = _fsql.Select<LicenseClient>().Where(c => c.Id == req.ClientId).First();
            // 全局过滤器已过滤 IsDeleted, null = 不存在或已软删
            if (client == null) return Result.Fail("客户不存在或已删除");

            // 产品名格式校验 (仅非空时校验)
            var productEn = string.IsNullOrWhiteSpace(req.ProductEn) ? null : req.ProductEn.Trim().ToLowerInvariant();
            if (!string.IsNullOrEmpty(productEn)
                && !System.Text.RegularExpressions.Regex.IsMatch(productEn, "^[a-z][a-z0-9]{1,19}$"))
                return Result.Fail("产品名格式错误: 仅小写字母+数字, 字母开头, 2-20 字符");

            // 到期后行为校验: 两个都勾 = 等于无限制, 没有意义
            if (req.AllowAdminAfterExpiry && req.AllowClientApiAfterExpiry)
                return Result.Fail("到期后行为: 管理与客户端不能同时勾选, 否则等于无到期限制");

            // 2. 域名扩展 (B 方案: example.com → + *.example.com)
            var domains = DomainExpander.Expand(req.Domains);

            var clientKey = string.IsNullOrWhiteSpace(req.ClientKey) ? null : req.ClientKey.Trim();
            var hardwareId = string.IsNullOrWhiteSpace(req.HardwareId) ? null : req.HardwareId.Trim();

            // 防"空白授权": 至少绑定一项身份(域名 / ClientKey / HardwareId)。
            // 否则签名 Payload 无任何校验字段, 授权可在任意机器原样使用 —— 复制合法授权即可绕过。
            if (domains.Count == 0 && string.IsNullOrEmpty(clientKey) && string.IsNullOrEmpty(hardwareId))
                return Result.Fail("授权必须绑定至少一项身份: 域名 / ClientKey / HardwareId, 否则授权可被复制到任意机器使用");

            // 3. 读取私钥, 签名 Payload
            var privatePath = Path.Combine(GetKeyDir(), PrivateKeyFile);
            if (!File.Exists(privatePath)) GenerateKeyPair();

            string licenseData;
            using (var rsa = RSA.Create())
            {
                rsa.FromXmlString(File.ReadAllText(privatePath));

                var payload = new LicensePayload
                {
                    ProductEn = productEn,
                    Domains = domains.Count > 0 ? domains : null,
                    // Domain 字段不写 (旧字段, 新 Key 留空)
                    ClientKey = clientKey,
                    HardwareId = hardwareId,
                    // VerifyMode 不写 (废弃, 留默认 2 不影响)
                    IssuedAt = AppTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"),
                    ExpiresAt = req.ExpiresAt.ToString("yyyy-MM-dd HH:mm:ss"),
                    AllowedRoutes = req.AllowedRoutes ?? new List<string>(),
                    AllowAdminAfterExpiry = req.AllowAdminAfterExpiry,
                    AllowClientApiAfterExpiry = req.AllowClientApiAfterExpiry
                };

                var payloadJson = JsonSerializer.Serialize(payload);
                var payloadBytes = Encoding.UTF8.GetBytes(payloadJson);
                var signatureBytes = rsa.SignData(payloadBytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

                licenseData = Convert.ToBase64String(payloadBytes) + "." + Convert.ToBase64String(signatureBytes);
            }

            // 4. 落库 LicenseRecord
            var record = new LicenseRecord
            {
                ClientId = req.ClientId,
                ProductEn = productEn,
                Domains = domains.Count > 0 ? JsonSerializer.Serialize(domains) : null,
                ClientKey = clientKey,
                HardwareId = hardwareId,
                IssuedAt = AppTime.UtcNow,
                ExpiresAt = req.ExpiresAt,
                AllowedRoutes = (req.AllowedRoutes?.Count ?? 0) > 0 ? JsonSerializer.Serialize(req.AllowedRoutes) : null,
                AllowAdminAfterExpiry = req.AllowAdminAfterExpiry,
                AllowClientApiAfterExpiry = req.AllowClientApiAfterExpiry,
                LicenseData = licenseData,
                IsDownloaded = false,
                Status = 0,
                // VerifyMode 实体字段留默认, 不再反映实际验证模式
            };
            _fsql.Insert(record).ExecuteAffrows();

            record = _fsql.Select<LicenseRecord>()
                .Where(r => r.LicenseData == licenseData).OrderByDescending(r => r.Id).First();

            _logger.LogInformation("签发授权: ClientId={ClientId}, ProductEn={ProductEn}, Domains=[{Domains}], ExpiresAt={ExpiresAt}",
                req.ClientId, productEn, string.Join(",", domains), req.ExpiresAt);

            return Result.Success("授权生成成功", new
            {
                recordId = record?.Id,
                licenseKey = licenseData,
                clientId = client.Id,
                productEn,
                productCn = client.ProductCn,
                domains,
                expiresAt = req.ExpiresAt,
                issuedAt = record?.IssuedAt,
            });
        }

        #endregion

        #region 授权查询与下载

        /// <summary>
        /// 查询客户端是否有可用的新授权
        /// </summary>
        public virtual Result CheckLicense(string? domain, string? actualDomain, string? productEn, string? productCn,
            string? clientKey, string? hardwareId,
            string? machineName = null, string? osDescription = null, int? processorCount = null, string? macAddresses = null, string? currentKey = null)
        {
            // 查找或自动注册客户, 顺带更新 LastActualDomain (一眼识别声明域名 vs 实际访问域名)
            var client = FindOrCreateClient(domain ?? "", clientKey, hardwareId, null, productEn, productCn,
                machineName, osDescription, processorCount, macAddresses, actualDomain);

            if (!string.IsNullOrWhiteSpace(actualDomain) && !string.Equals(actualDomain, domain, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogInformation("客户 {ClientId} 声明域名={Domain}, 实际访问={ActualDomain}", client.Id, domain, actualDomain);
            }

            // 检查当前授权是否被吊销
            bool currentRevoked = false;
            if (!string.IsNullOrEmpty(currentKey))
            {
                // 通过 LicenseData 匹配记录
                var currentRecord = _fsql.Select<LicenseRecord>()
                    .Where(c => c.ClientId == client.Id && c.LicenseData == currentKey)
                    .First();
                if (currentRecord != null && currentRecord.Status == 1)
                    currentRevoked = true;
            }

            // 查找最新的有效授权
            var record = _fsql.Select<LicenseRecord>()
                .Where(c => c.ClientId == client.Id && c.Status == 0)
                .OrderByDescending(c => c.Id)
                .First();

            if (record == null)
                return Result.Success(currentRevoked ? "当前授权已被吊销" : "暂无可用授权", new { available = false, currentRevoked });

            // 判断服务端最新授权和客户端当前使用的是否同一个
            var isCurrent = !string.IsNullOrEmpty(currentKey) && record.LicenseData == currentKey;

            // 解析 JSON 字段供前端展示
            var recordDomains = string.IsNullOrEmpty(record.Domains)
                ? new List<string>()
                : (JsonSerializer.Deserialize<List<string>>(record.Domains) ?? new List<string>());
            var recordAllowedRoutes = string.IsNullOrEmpty(record.AllowedRoutes)
                ? new List<string>()
                : (JsonSerializer.Deserialize<List<string>>(record.AllowedRoutes) ?? new List<string>());

            return Result.Success(isCurrent ? "当前已是最新授权" : "有可用授权", new
            {
                available = true,
                isCurrent,
                currentRevoked,
                recordId = record.Id,
                isDownloaded = record.IsDownloaded,
                expiresAt = record.ExpiresAt,
                issuedAt = record.IssuedAt,
                // 授权详情 - 让用户在决定应用前能完整预览
                productEn = record.ProductEn,
                domains = recordDomains,
                clientKey = record.ClientKey,
                hardwareId = record.HardwareId,
                allowedRoutes = recordAllowedRoutes,
                allowAdminAfterExpiry = record.AllowAdminAfterExpiry,
                allowClientApiAfterExpiry = record.AllowClientApiAfterExpiry,
                // 启用的验证项 (Payload 字段非空 = 客户端会强制验证该项)
                requiresProduct = !string.IsNullOrEmpty(record.ProductEn),
                requiresDomain = recordDomains.Count > 0,
                requiresClientKey = !string.IsNullOrEmpty(record.ClientKey),
                requiresHardwareId = !string.IsNullOrEmpty(record.HardwareId)
            });
        }

        /// <summary>
        /// 下载授权文件
        /// </summary>
        public virtual Result DownloadLicense(int recordId)
        {
            var record = _fsql.Select<LicenseRecord>()
                .Where(c => c.Id == recordId && c.Status == 0)
                .First();

            if (record == null)
                return Result.Fail("授权记录不存在或已吊销");

            // 标记已下载
            if (!record.IsDownloaded)
            {
                record.IsDownloaded = true;
                _fsql.Update<LicenseRecord>().SetSource(record).ExecuteAffrows();
            }

            return Result.Success("下载成功", new { licenseData = record.LicenseData });
        }

        /// <summary>
        /// 吊销授权
        /// </summary>
        public virtual Result RevokeLicense(int recordId)
        {
            var record = _fsql.Select<LicenseRecord>().Where(c => c.Id == recordId).First();
            if (record == null) return Result.Fail("授权记录不存在");

            record.Status = 1;
            _fsql.Update<LicenseRecord>().SetSource(record).ExecuteAffrows();

            _logger.LogInformation("吊销授权: RecordId={RecordId}", recordId);
            return Result.Success("授权已吊销");
        }

        #endregion

        #region 授权记录

        /// <summary>
        /// 授权记录列表
        /// </summary>
        public virtual Result RecordList(int page, int pageSize, int? clientId)
        {
            // DisableGlobalFilter("IsDeleted"): 确保 Include Client 能看到已软删的客户 (历史记录完整展示)
            var query = _fsql.Select<LicenseRecord>()
                .DisableGlobalFilter("IsDeleted")
                .Include(c => c.Client)
                .WhereIf(clientId.HasValue, c => c.ClientId == clientId!.Value)
                .OrderByDescending(c => c.Id);

            var data = query.Count(out long total).Page(page, pageSize).ToList();
            return Result.Page(data, total);
        }

        #endregion

        #region 验证日志

        /// <summary>
        /// 记录验证日志 (旧版单一职责, 保留给 LicenseHeartbeatTask 等调用).
        /// </summary>
        public virtual void LogVerification(string domain, string? clientKey, string? hardwareId, string? serverIP, bool licenseValid, string? message)
        {
            var client = FindOrCreateClient(domain, clientKey, hardwareId, serverIP);

            var log = new LicenseVerifyLog
            {
                ClientId = client.Id,
                Domain = domain ?? "",
                ServerIP = serverIP,
                ClientKey = clientKey,
                HardwareId = hardwareId,
                LicenseValid = licenseValid,
                Message = message
            };
            _fsql.Insert(log).ExecuteAffrows();
        }

        /// <summary>
        /// 验证上报 + 吊销查询. 替代 LogVerification 的旧 API 调用入口.
        /// 返回 { revoked: bool } 让客户端决定是否清本地 Key.
        /// </summary>
        public virtual Result Verify(string? domain, string? actualDomain, string? productEn, string? productCn,
            string? clientKey, string? hardwareId, string? currentKey,
            string? serverIP, bool licenseValid, string? message)
        {
            // FindOrCreateClient 带上 productEn/productCn, 确保 LicenseClient.ProductEn 从客户端上报值更新
            // (之前只有 Check 接口传 productEn, 但 Check 不频繁, 导致多数客户 ProductEn=NULL)
            var client = FindOrCreateClient(domain ?? "", clientKey, hardwareId, serverIP,
                productEn: productEn, productCn: productCn, actualDomain: actualDomain);

            // 查吊销: 按 currentKey 对比 LicenseRecord.LicenseData
            bool revoked = false;
            if (!string.IsNullOrWhiteSpace(currentKey))
            {
                var record = _fsql.Select<LicenseRecord>()
                    .Where(r => r.LicenseData == currentKey)
                    .First();
                revoked = record?.Status == 1;
            }

            if (!string.IsNullOrWhiteSpace(actualDomain)
                && !string.Equals(actualDomain, domain, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogInformation("客户 {ClientId} 声明域名={Domain}, 实际访问={ActualDomain}",
                    client.Id, domain, actualDomain);
            }

            // 写 VerifyLog, actualDomain 入库便于管理员追查历史来源
            var logMessage = revoked ? $"[吊销回推] {message}" : message;
            _fsql.Insert(new LicenseVerifyLog
            {
                ClientId = client.Id,
                Domain = domain ?? "",
                ActualDomain = actualDomain,
                ProductEn = productEn,
                ProductCn = productCn,
                ServerIP = serverIP,
                ClientKey = clientKey,
                HardwareId = hardwareId,
                LicenseValid = licenseValid,
                Message = logMessage
            }).ExecuteAffrows();

            return Result.Success("验证上报成功", new { revoked });
        }

        /// <summary>
        /// 验证日志列表
        /// </summary>
        public virtual Result VerifyLogList(int page, int pageSize, int? clientId)
        {
            // DisableGlobalFilter("IsDeleted"): 确保 Include Client 能看到已软删的客户
            var query = _fsql.Select<LicenseVerifyLog>()
                .DisableGlobalFilter("IsDeleted")
                .Include(c => c.Client)
                .WhereIf(clientId.HasValue, c => c.ClientId == clientId!.Value)
                .OrderByDescending(c => c.Id);

            var data = query.Count(out long total).Page(page, pageSize).ToList();
            return Result.Page(data, total);
        }

        #endregion

        #region 心跳处理

        /// <summary>
        /// 处理客户端心跳
        /// </summary>
        public virtual Result ProcessHeartbeat(HeartbeatRequest request)
        {
            if (string.IsNullOrEmpty(request.ClientKey) && string.IsNullOrEmpty(request.HardwareId))
                return Result.Fail("缺少客户端标识");

            // 心跳上报带完整身份字段, 让首次心跳 (在任何 Check/Verify 之前) 也能创建完整 LicenseClient 记录.
            var client = FindOrCreateClient(
                request.Domain ?? "",
                request.ClientKey,
                request.HardwareId,
                request.ServerIP,
                productEn: request.ProductEn,
                productCn: request.ProductCn,
                machineName: request.MachineName,
                osDescription: request.OsDescription,
                processorCount: request.ProcessorCount,
                macAddresses: request.MacAddresses);

            // 查找最新的授权记录
            var record = _fsql.Select<LicenseRecord>()
                .Where(c => c.ClientId == client.Id)
                .OrderByDescending(c => c.Id)
                .First();

            if (record == null)
                return Result.Success(new { status = "unknown", message = "未找到授权记录" });

            if (record.Status == 1)
                return Result.Success(new { status = "revoked", message = "授权已被吊销" });

            if (record.ExpiresAt < AppTime.UtcNow)
                return Result.Success(new { status = "expired", message = "授权已过期" });

            // 有效 — 更新心跳时间
            client.LastHeartbeat = AppTime.UtcNow;
            client.UpdatedTime = AppTime.Now;
            _fsql.Update<LicenseClient>().SetSource(client).ExecuteAffrows();

            return Result.Success(new { status = "active", serverTime = AppTime.UtcNow, message = "ok" });
        }

        #endregion
    }
}
