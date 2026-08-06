using FreeSql.DataAnnotations;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace SB.Framework.Models
{
    /// <summary>
    /// 管理员
    /// </summary>
    [Table(Name = "SB_Admin")]
    public class Admin : IEntity, ISoftDelete
    {
        [Column(Name = "Id", IsPrimary = true, IsIdentity = true)]
        public int Id { get; set; }

        /// <summary>
        /// 父级Id
        /// </summary>
        [Column(Name = "ParentId", IsNullable = false)]
        public int ParentId { get; set; } = 0;

        /// <summary>
        /// 用户名
        /// </summary>
        [Required, StringLength(50)]
        [Column(Name = "Username", IsNullable = false)]
        public string Username { get; set; }

        /// <summary>
        /// 密码
        /// </summary>
        [Required, StringLength(200)]
        [Column(Name = "Password", IsNullable = false)]
        public string Password { get; set; }

        /// <summary>
        /// 角色Id
        /// </summary>
        [Column(Name = "RoleId")]
        public int RoleId { get; set; }
        [Navigate(nameof(RoleId))]
        public AdminRole AdminRole { get; set; }

        /// <summary>
        /// 名称
        /// </summary>
        [Column(Name = "Name")]
        public string Name { get; set; }

        /// <summary>
        /// 头像
        /// </summary>
        [Column(Name = "Avatar")]
        public string Avatar { get; set; }

        /// <summary>
        /// 管理员状态
        /// </summary>
        [Column(Name = "Status", MapType = typeof(string))]
        public AdminStatus Status { get; set; } = AdminStatus.Active;

        /// <summary>
        /// 创建时间
        /// </summary>
        [Column(Name = "CreatedTime", DbType = "timestamp", InsertValueSql = "now()")]
        public DateTime CreatedTime { get; set; }


        /// <summary>
        /// 最后登录时间
        /// </summary>
        [Column(Name = "LastOnlineTime", DbType = "timestamp", IsNullable = true)]
        public DateTime? LastOnlineTime { get; set; }

        /// <summary>
        /// 验证器秘钥
        /// </summary>
        [Column(Name = "AuthenticatorKey", IsNullable = true)]
        public string AuthenticatorKey { get; set; }

        /// <summary>
        /// 验证器绑定时间
        /// </summary>
        [Column(Name = "AuthenticatorBindTime", DbType = "timestamp", IsNullable = true)]
        public DateTime? AuthenticatorBindTime { get; set; }

        /// <summary>
        /// 客服昵称（对外展示名称，保护真实账号信息）
        /// </summary>
        [Column(Name = "Nickname", StringLength = 50, IsNullable = true)]
        public string Nickname { get; set; }

        /// <summary>
        /// 账号类型：Human=可密码登录的人；Service=仅供 API Key 使用的服务账号
        /// （无可用密码、禁止密码登录）。现有行默认 0=Human，向后兼容。
        /// </summary>
        [Column(Name = "AccountType", MapType = typeof(string), IsNullable = false, InsertValueSql = "'Human'")]
        public AccountType AccountType { get; set; } = AccountType.Human;

        /// <summary>
        /// 伪删除
        /// </summary>
        [Column(Name = "IsDeleted", IsNullable = false, InsertValueSql = "false")]
        public bool IsDeleted { get; set; }


    }

    public enum AdminStatus
    {
        Active = 0,
        Disabled = 1,
    }

    /// <summary>
    /// 账号类型。Human=普通人类管理员（密码登录）；Service=服务账号（仅 API Key 访问，无密码登录）。
    /// </summary>
    public enum AccountType
    {
        Human = 0,
        Service = 1,
    }
}