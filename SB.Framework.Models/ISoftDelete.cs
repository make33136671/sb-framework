namespace SB.Framework.Models
{
    /// <summary>
    /// 软删除接口 - 实现此接口的实体将自动应用全局软删除过滤
    /// </summary>
    public interface ISoftDelete
    {
        bool IsDeleted { get; set; }
    }
}
