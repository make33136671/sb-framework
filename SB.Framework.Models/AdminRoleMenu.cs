using FreeSql.DataAnnotations;

namespace SB.Framework.Models
{
    /// <summary>
    /// 管理员角色-菜单关联表
    /// </summary>
    [Table(Name = "SB_AdminRoleMenu")]
    public class AdminRoleMenu
    {
        /// <summary>
        /// 主键
        /// </summary>
        [Column(Name = "Id", IsPrimary = true, IsIdentity = true)]
        public int Id { get; set; }

        /// <summary>
        /// 角色ID
        /// </summary>
        [Column(Name = "RoleId", IsNullable = false)]
        public int RoleId { get; set; }

        /// <summary>
        /// 菜单ID
        /// </summary>
        [Column(Name = "MenuId", IsNullable = false)]
        public int MenuId { get; set; }

        /// <summary>
        /// 角色导航属性
        /// </summary>
        [Navigate(nameof(RoleId))]
        public AdminRole AdminRole { get; set; }

        /// <summary>
        /// 菜单导航属性
        /// </summary>
        [Navigate(nameof(MenuId))]
        public Menu Menu { get; set; }
    }
}