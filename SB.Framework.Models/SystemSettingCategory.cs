using FreeSql.DataAnnotations;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace SB.Framework.Models
{
    /// <summary>
    /// 系统设置分类
    /// </summary>
    [Table(Name = "SB_SystemSetting_Category")]
    public class SystemSettingCategory : IEntity
    {
        [Column(Name = "Id", IsPrimary = true, IsIdentity = true)]
        public int Id { get; set; }

        /// <summary>
        /// 设置名称
        /// </summary>
        [Required, StringLength(50)]
        [Column(Name = "Name")]
        public string Name { get; set; } = null!;

        /// <summary>
        /// 图标
        /// </summary>
        [Column(Name = "Icon")]
        public string Icon { get; set; } = null!;

        /// <summary>
        /// 显示排序
        /// </summary>
        [Column(Name = "Sort")]
        public int Sort { get; set; }

        [Navigate(nameof(SystemSetting.CategoryId))]
        public List<SystemSetting> SystemSettingList { get; set; } = null!;

    }

}
