using FreeSql.DataAnnotations;
using System.ComponentModel.DataAnnotations;

namespace SB.Framework.Models
{
    /// <summary>
    /// 系统菜单
    /// </summary>
    [Table(Name = "SB_Menu")]
    public class Menu : IEntity, ISoftDelete
    {
        [Column(Name = "Id", IsPrimary = true, IsIdentity = true)]
        public int Id { get; set; }

        /// <summary>
        /// 父级Id
        /// </summary>
        [Column(Name = "ParentId")]
        public int ParentId { get; set; }
        [Navigate(nameof(ParentId))]
        public Menu ParentMenu { get; set; }


        /// <summary>
        /// 名称
        /// </summary>
        [Required, StringLength(100)]
        [Column(Name = "Name")]
        public string Name { get; set; }


        /// <summary>
        /// 路由路径
        /// </summary>
        [Required, StringLength(200)]
        [Column(Name = "Path")]
        public string Path { get; set; }


        /// <summary>
        /// 组件路径
        /// </summary>
        [Column(Name = "Component")]
        public string Component { get; set; }

        /// <summary>
        /// 重定向路由
        /// </summary>
        [Column(Name = "Redirect")]
        public string Redirect { get; set; }

        /// <summary>
        /// 显示标题
        /// </summary>
        [Required, StringLength(100)]
        [Column(Name = "Title")]
        public string Title { get; set; }

        /// <summary>
        /// 图标
        /// </summary>
        [Column(Name = "Icon")]
        public string Icon { get; set; }

        /// <summary>
        /// 排序编号
        /// </summary>
        [Column(Name = "OrderNo")]
        public int OrderNo { get; set; }

        /// <summary>
        /// 在菜单中隐藏
        /// </summary>
        [Column(Name = "HideInMenu")]
        public bool HideInMenu { get; set; }

        /// <summary>
        /// 忽略保持存活
        /// </summary>
        [Column(Name = "IgnoreKeepAlive")]
        public bool IgnoreKeepAlive { get; set; }

        /// <summary>
        /// 伪删除
        /// </summary>
        [Column(Name = "IsDeleted", IsNullable = false, InsertValueSql = "0")]
        public bool IsDeleted { get; set; }

    }
}
