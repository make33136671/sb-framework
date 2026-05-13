namespace SB.Framework.Models.Qdrant;

/// <summary>
/// Qdrant 客户端连接配置。配置段 <c>Sb:Qdrant</c>。
/// </summary>
public sealed class QdrantOptions
{
    public const string SectionName = "Sb:Qdrant";

    /// <summary>主机名或 IP,默认 localhost。</summary>
    public string Host { get; set; } = "localhost";

    /// <summary>gRPC 端口,Qdrant 默认 6334(REST 是 6333,这里走 gRPC)。</summary>
    public int Port { get; set; } = 6334;

    /// <summary>启用 TLS;远端云服务时通常为 true。</summary>
    public bool UseHttps { get; set; }

    /// <summary>Qdrant API Key,本地部署可留空。</summary>
    public string? ApiKey { get; set; }
}
