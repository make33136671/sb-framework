using Qdrant.Client.Grpc;
using SB.Framework.Models.Qdrant;

namespace SB.Framework.Services.Qdrant;

/// <summary>
/// Qdrant 读写驱动。屏蔽 gRPC / PointStruct / Value 细节,业务层只用 Guid + float[] + Dictionary。
/// <para>注:Search 的 filter 参数仍直接暴露 Qdrant <see cref="Filter"/>,因为 filter 的表达力极强、自封一遍代价不划算。</para>
/// </summary>
public interface IQdrantDriver
{
    // ===== 健康检查 =====

    /// <summary>探活,失败返回 false 不抛异常。</summary>
    Task<bool> PingAsync(CancellationToken ct = default);

    // ===== Collection 管理 =====

    Task<bool> CollectionExistsAsync(string collection, CancellationToken ct = default);

    /// <summary>不存在则创建;已存在则跳过(不校验维度一致性,由调用方保证)。</summary>
    Task EnsureCollectionAsync(
        string collection,
        ulong vectorSize,
        QdrantDistance distance = QdrantDistance.Cosine,
        CancellationToken ct = default);

    Task DeleteCollectionAsync(string collection, CancellationToken ct = default);

    Task<IReadOnlyList<string>> ListCollectionsAsync(CancellationToken ct = default);

    // ===== Points =====

    /// <summary>批量写入/覆盖向量点。</summary>
    Task UpsertAsync(string collection, IEnumerable<VectorPoint> points, CancellationToken ct = default);

    /// <summary>按 Id 批量删除。</summary>
    Task DeleteAsync(string collection, IEnumerable<Guid> ids, CancellationToken ct = default);

    /// <summary>按 filter 批量删除。</summary>
    Task DeleteAsync(string collection, Filter filter, CancellationToken ct = default);

    // ===== Search =====

    /// <summary>
    /// 向量相似度检索。
    /// </summary>
    /// <param name="queryVector">查询向量,维度必须与 collection 一致。</param>
    /// <param name="limit">返回条数上限。</param>
    /// <param name="filter">可选 payload 过滤(Qdrant 原生 Filter,见 Qdrant.Client.Grpc)。</param>
    /// <param name="scoreThreshold">分数下限,低于则不返回。</param>
    Task<IReadOnlyList<SearchHit>> SearchAsync(
        string collection,
        ReadOnlyMemory<float> queryVector,
        ulong limit = 10,
        Filter? filter = null,
        float? scoreThreshold = null,
        CancellationToken ct = default);
}
