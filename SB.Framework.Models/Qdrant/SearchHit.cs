namespace SB.Framework.Models.Qdrant;

/// <summary>
/// 一条向量检索结果。Payload 已从 Qdrant Value 还原为 .NET 原生类型。
/// </summary>
public sealed record SearchHit(
    Guid Id,
    float Score,
    IReadOnlyDictionary<string, object?> Payload);
