using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;

namespace SB.Framework.Core.License
{
    /// <summary>
    /// 客户端身份 Payload — 所有向授权服务器发送的请求 (Request/Check/Verify/Heartbeat) 统一用它构造.
    /// 目的: 保证任何情况发送的字段名/字段值来源一致, 避免出现 "Heartbeat 上报字段少于 Check" 这种不一致,
    /// 进而导致服务端创建不完整客户记录等隐性 bug.
    /// </summary>
    public class ClientIdentityPayload
    {
        /// <summary>客户端密钥 (每项目唯一, 首次启动生成 UUID 存入 client.key)</summary>
        public string ClientKey { get; set; } = "";

        /// <summary>硬件 ID (machine-id + CPU + MAC 等 SHA256)</summary>
        public string HardwareId { get; set; } = "";

        /// <summary>产品英文名 (AppSettings.ProductEn)</summary>
        public string ProductEn { get; set; } = "";

        /// <summary>产品中文名 (AppSettings.ProductCn)</summary>
        public string ProductCn { get; set; } = "";

        /// <summary>声明域名 (AppSettings.PrimaryDomain) — 客户端配置的主域名, 不是当前访问的</summary>
        public string Domain { get; set; } = "";

        /// <summary>机器名</summary>
        public string MachineName { get; set; } = "";

        /// <summary>操作系统描述</summary>
        public string OsDescription { get; set; } = "";

        /// <summary>CPU 核心数</summary>
        public int ProcessorCount { get; set; }

        /// <summary>MAC 地址 (逗号分隔)</summary>
        public string MacAddresses { get; set; } = "";

        /// <summary>客户端所在机器 IPv4 (不是授权中心的, 是客户端自己的)</summary>
        public string ServerIP { get; set; } = "";

        /// <summary>
        /// 采集当前进程 + 本机的客户端身份信息.
        /// <para>basePath 传 IHostEnvironment.ContentRootPath — 和其他授权相关调用保持一致,
        /// 避免 ClientKeyService 静态缓存因不同 basePath 导致的 ClientKey 漂移.</para>
        /// </summary>
        public static ClientIdentityPayload Collect(string? basePath = null)
        {
            var hwInfo = HardwareIdService.GetHardwareInfo();
            return new ClientIdentityPayload
            {
                ClientKey = ClientKeyService.GetClientKey(basePath) ?? "",
                HardwareId = HardwareIdService.GetHardwareId() ?? "",
                ProductEn = AppGlobal.AppSettings?.ProductEn ?? "",
                ProductCn = AppGlobal.AppSettings?.ProductCn ?? "",
                Domain = AppGlobal.AppSettings?.PrimaryDomain ?? "",
                MachineName = hwInfo.MachineName ?? "",
                OsDescription = hwInfo.OSDescription ?? "",
                ProcessorCount = hwInfo.ProcessorCount,
                MacAddresses = string.Join(",", hwInfo.MacAddresses ?? Array.Empty<string>()),
                ServerIP = GetLocalIP() ?? ""
            };
        }

        /// <summary>
        /// 转成 Dictionary (用于 form/query 序列化).
        /// 字段顺序固定, 保证和所有发送点日志可读性一致.
        /// </summary>
        public Dictionary<string, string> ToDictionary()
        {
            return new Dictionary<string, string>
            {
                ["clientKey"] = ClientKey,
                ["hardwareId"] = HardwareId,
                ["productEn"] = ProductEn,
                ["productCn"] = ProductCn,
                ["domain"] = Domain,
                ["machineName"] = MachineName,
                ["osDescription"] = OsDescription,
                ["processorCount"] = ProcessorCount.ToString(),
                ["macAddresses"] = MacAddresses,
                ["serverIP"] = ServerIP,
            };
        }

        /// <summary>
        /// 转成 URL query / form-urlencoded 字符串.
        /// <paramref name="extra"/> 追加接口特有字段 (如 actualDomain / currentKey / licenseValid),
        /// 同 key 时 extra 覆盖身份字段 (允许调用方覆盖如 Domain).
        /// </summary>
        public string ToQueryString(IDictionary<string, string?>? extra = null)
        {
            var map = ToDictionary();
            if (extra != null)
                foreach (var kv in extra)
                    map[kv.Key] = kv.Value ?? "";

            return string.Join("&",
                map.Select(kv => $"{kv.Key}={Uri.EscapeDataString(kv.Value ?? "")}"));
        }

        /// <summary>
        /// 转成 JSON body (用于 [FromBody] 接口, 当前只有 Heartbeat).
        /// <paramref name="extra"/> 追加接口特有字段.
        /// </summary>
        public string ToJson(IDictionary<string, object?>? extra = null)
        {
            var map = new Dictionary<string, object?>
            {
                ["clientKey"] = ClientKey,
                ["hardwareId"] = HardwareId,
                ["productEn"] = ProductEn,
                ["productCn"] = ProductCn,
                ["domain"] = Domain,
                ["machineName"] = MachineName,
                ["osDescription"] = OsDescription,
                ["processorCount"] = ProcessorCount,
                ["macAddresses"] = MacAddresses,
                ["serverIP"] = ServerIP,
            };
            if (extra != null)
                foreach (var kv in extra) map[kv.Key] = kv.Value;

            return JsonSerializer.Serialize(map);
        }

        /// <summary>获取本机 IPv4 地址 (所有发送点统一用这个, 避免重复实现不一致).</summary>
        public static string? GetLocalIP()
        {
            try
            {
                var host = Dns.GetHostEntry(Dns.GetHostName());
                return host.AddressList
                    .FirstOrDefault(ip => ip.AddressFamily == AddressFamily.InterNetwork)
                    ?.ToString();
            }
            catch { return null; }
        }
    }
}
