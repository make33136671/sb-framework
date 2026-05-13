namespace SB.Framework.Models.Qdrant;

/// <summary>
/// 一条待写入的向量点。
/// <para>Id 用 Guid;Payload 是任意可被 JSON 化的键值对(string/bool/数值/null/List/Dictionary 嵌套)。</para>
/// </summary>
public sealed record VectorPoint(
    Guid Id,
    ReadOnlyMemory<float> Vector,
    IReadOnlyDictionary<string, object?>? Payload = null);
