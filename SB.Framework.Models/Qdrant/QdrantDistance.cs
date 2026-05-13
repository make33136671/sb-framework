namespace SB.Framework.Models.Qdrant;

/// <summary>
/// 向量相似度度量。镜像 Qdrant 的 Distance,独立定义以便上层模块不必依赖 Qdrant.Client。
/// </summary>
public enum QdrantDistance
{
    Cosine = 0,
    Euclid = 1,
    Dot = 2,
    Manhattan = 3,
}
