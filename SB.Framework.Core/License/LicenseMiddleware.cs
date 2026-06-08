using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using SB.Framework.Core.Authentication;

namespace SB.Framework.Core.License
{
    /// <summary>
    /// 心跳宽限期检查接口 — 由 LicenseHeartbeatTask 实现，避免 Core 层引用 Services 层
    /// </summary>
    public interface IHeartbeatGracePeriodChecker
    {
        bool IsGracePeriodExceeded();
    }

    public class LicenseMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILicenseService _license;
        private readonly ILogger<LicenseMiddleware> _logger;
        private readonly List<string> _whitelistPaths;
        private readonly IHeartbeatGracePeriodChecker? _heartbeatChecker;
        private readonly bool _heartbeatEnabled;
        private bool _startupLogged;

        public LicenseMiddleware(RequestDelegate next, ILicenseService license, ILogger<LicenseMiddleware> logger)
        {
            _next = next;
            _license = license;
            _logger = logger;

            _heartbeatEnabled = !string.IsNullOrEmpty(AppGlobal.AppSettings?.GetCloudServerUrl());

            // 白名单硬编码，不从配置读取，防止用户篡改
            _whitelistPaths = new List<string>
            {
                "/health",
                "/api/setup/",
                "/api/verify/",
                "/storage/",
                "/admin/Verification/",
                "/admin/Entitlement/",
                "/admin/Setup/",
                "/admin/Auth/Login",
                "/admin/Auth/GetInfo",
                "/admin/Menu/All",
            };
        }

        /// <summary>
        /// 带心跳检查器的构造函数
        /// </summary>
        public LicenseMiddleware(RequestDelegate next, ILicenseService license, ILogger<LicenseMiddleware> logger, IHeartbeatGracePeriodChecker heartbeatChecker)
            : this(next, license, logger)
        {
            _heartbeatChecker = heartbeatChecker;
        }

        public async Task InvokeAsync(HttpContext context, ILicenseVerifyClient? verifyClient = null)
        {
            if (!_startupLogged)
            {
                LogStartupStatus();
                _startupLogged = true;
            }

            var path = context.Request.Path.Value ?? "";

            // 系统白名单路径直接放行
            if (_whitelistPaths.Any(w => path.StartsWith(w, StringComparison.OrdinalIgnoreCase)))
            {
                await _next(context);
                return;
            }

            // FullAccess(god)API Key — 设计上绕过所有限制（含 License）。普通 key / 密码登录不绕过。
            // 取代原静态 X-Master-Key 通道,改由可吊销 / 可审计的 FullAccess key 承担。
            var apiKeyAuth = context.RequestServices.GetService(typeof(IApiKeyAuthenticator)) as IApiKeyAuthenticator;
            if (apiKeyAuth != null)
            {
                var keyResult = apiKeyAuth.Resolve(context, "JwtAdmin");
                if (keyResult.Outcome == ApiKeyOutcome.Authenticated && keyResult.IsFullAccess)
                {
                    await _next(context);
                    return;
                }
            }

            // 时间回退检测
            var timeCheck = TimeRegressionGuard.Check();
            if (timeCheck != null)
            {
                _logger.LogWarning("时间回退检测触发: {Message}", timeCheck);
                context.Response.StatusCode = 403;
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsync(JsonSerializer.Serialize(new
                {
                    code = -1,
                    message = timeCheck,
                    type = "license_invalid",
                    data = (object?)null
                }));
                return;
            }

            var status = _license.CurrentStatus;

            // 授权有效，继续检查心跳
            if (status.IsValid)
            {
                // 域名校验 (Domains 非空才做, 旧 Key 此处为空 = 跳过)
                if (status.Domains.Count > 0)
                {
                    var host = context.Request.Host.Value;
                    if (!DomainMatcher.Match(status.Domains, host))
                    {
                        _logger.LogWarning("请求被拦截 - 域名不匹配: {Host} 不在 [{Domains}]", host, string.Join(",", status.Domains));
                        context.Response.StatusCode = 403;
                        context.Response.ContentType = "application/json";
                        await context.Response.WriteAsync(JsonSerializer.Serialize(new
                        {
                            code = -1,
                            message = $"当前访问域名 {host} 不在授权允许列表",
                            type = "license_invalid",
                            data = (object?)null
                        }));
                        return;
                    }
                }

                // 异步触发吊销检查 (fire-and-forget, 节流 1 小时 by LicenseVerifyClient)
                if (verifyClient != null)
                {
                    _ = verifyClient.TryVerifyAsync(context);
                }

                // 心跳宽限期检查
                if (_heartbeatEnabled && _heartbeatChecker != null && _heartbeatChecker.IsGracePeriodExceeded())
                {
                    _logger.LogWarning("请求被拦截 - 心跳宽限期已超时: {Path}", path);
                    context.Response.StatusCode = 403;
                    context.Response.ContentType = "application/json";
                    await context.Response.WriteAsync(JsonSerializer.Serialize(new
                    {
                        code = -1,
                        message = "心跳检测失败，授权服务器不可达超过宽限期",
                        type = "license_invalid",
                        data = (object?)null
                    }));
                    return;
                }

                await _next(context);
                return;
            }

            // 授权过期 - 按到期后行为策略放行
            if (status.IsExpired)
            {
                // 管理后台路径 + 允许管理 → 放行
                if (status.AllowAdminAfterExpiry && path.StartsWith("/admin/", StringComparison.OrdinalIgnoreCase))
                {
                    await _next(context);
                    return;
                }
                // 客户端 API 路径 + 允许客户端 → 放行
                if (status.AllowClientApiAfterExpiry && path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase))
                {
                    await _next(context);
                    return;
                }
                // 兼容旧 AllowedRoutes 自定义放行清单
                if (status.AllowedRoutes.Count > 0
                    && status.AllowedRoutes.Any(r => path.StartsWith(r, StringComparison.OrdinalIgnoreCase)))
                {
                    await _next(context);
                    return;
                }
            }

            // 拦截请求
            _logger.LogWarning("请求被拦截 - 授权无效: {Path} - {Message}", path, status.Message);
            context.Response.StatusCode = 403;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(JsonSerializer.Serialize(new
            {
                code = -1,
                message = status.Message ?? "系统授权已过期或无效，请联系管理员",
                type = "license_invalid",
                data = (object?)null
            }));
        }

        private void LogStartupStatus()
        {
            var status = _license.CurrentStatus;
            if (status.IsValid)
                _logger.LogInformation("授权验证通过: {Message} (剩余{Days}天)", status.Message, status.RemainingDays);
            else
                _logger.LogWarning("授权验证失败: {Message}", status.Message);
        }

    }
}
