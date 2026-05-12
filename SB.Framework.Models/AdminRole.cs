using FreeSql.DataAnnotations;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace SB.Framework.Models
{
    /// <summary>
    /// 管理员角色
    /// </summary>
    [Table(Name = "SB_AdminRole")]
    public class AdminRole : IEntity, ISoftDelete
    {
        [Column(Name = "Id", IsPrimary = true, IsIdentity = true)]
        public int Id { get; set; }


        /// <summary>
        /// 角色名称
        /// </summary>
        [Required, StringLength(50)]
        [Column(Name = "Name")]
        public string Name { get; set; }

        /// <summary>
        /// 角色标识
        /// </summary>
        [Required, StringLength(50)]
        [Column(Name = "RoleKey")]
        public string RoleKey { get; set; }


        [Navigate(ManyToMany = typeof(AdminRole))]
        public List<Admin> Admins { get; set; }

        /// <summary>
        /// 角色菜单关联
        /// </summary>
        [Navigate(nameof(AdminRoleMenu.RoleId))]
        public List<AdminRoleMenu> RoleMenu { get; set; }

        /// <summary>
        /// 伪删除
        /// </summary>
        [Column(Name = "IsDeleted", IsNullable = false, InsertValueSql = "false")]
        public bool IsDeleted { get; set; }
    }
}