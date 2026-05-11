namespace SB.Framework.Models
{
    /// <summary>
    /// 实体基础接口 — 所有具有 Id 主键的实体均应实现此接口
    /// </summary>
    public interface IEntity
    {
        int Id { get; set; }
    }
}
