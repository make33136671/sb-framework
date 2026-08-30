using FreeSql.DataAnnotations;
using System.ComponentModel.DataAnnotations;

namespace SB.Framework.Models
{
    /// <summary>
    /// 系统设置
    /// </summary>
    [Table(Name = "SB_SystemSetting")]
    public class SystemSetting : IEntity
    {
        [Column(Name = "Id", IsPrimary = true, IsIdentity = true)]
        public int Id { get; set; }

        /// <summary>
        /// 系统设置分类
        /// </summary>
        [Column(Name = "CategoryId")]
        public int CategoryId { get; set; }
        [Navigate(nameof(CategoryId))]
        public SystemSettingCategory? Category { get; set; }

        /// <summary>
        /// 设置系统键
        /// </summary>
        [Required, StringLength(100)]
        [Column(Name = "Key")]
        public string Key { get; set; } = null!;

        /// <summary>
        /// 设置名称
        /// </summary>
        [Required, StringLength(100)]
        [Column(Name = "Name")]
        public string Name { get; set; } = null!;


        /// <summary>
        /// 设置界面显示类型
        /// </summary>
        [Column(Name = "DisplayType")]
        public SystemSettingDisplayType DisplayType { get; set; }

        /// <summary>
        /// 系统设置不能删除
        /// </summary>
        [Column(Name = "CantDelete")]
        public bool CantDelete { get; set; }

        /// <summary>
        /// 设置数据内容
        /// </summary>
        [Column(Name = "DataContent", StringLength = -2)]
        public string? DataContent { get; set; }

        /// <summary>
        /// 设置数据选项
        /// </summary>
        [Column(Name = "DataContentConfig", StringLength = -2)]
        public string DataContentConfig { get; set; } = null!;

        /// <summary>
        /// 显示排序
        /// </summary>
        [Column(Name = "Sort")]
        public int Sort { get; set; }

    }

    /// <summary>
    /// 系统设置定义（用于 GetOrCreate 按需自动注册）
    /// </summary>
    public class SystemSettingDefinition
    {
        /// <summary>设置键（唯一标识）</summary>
        public string Key { get; set; } = "";

        /// <summary>设置显示名称</summary>
        public string Name { get; set; } = "";

        /// <summary>所属分类名称（不存在则自动创建）</summary>
        public string CategoryName { get; set; } = "";

        /// <summary>分类图标（仅新建分类时使用）</summary>
        public string CategoryIcon { get; set; } = "";

        /// <summary>分类排序（仅新建分类时使用）</summary>
        public int CategorySort { get; set; } = 0;

        /// <summary>显示类型</summary>
        public SystemSettingDisplayType DisplayType { get; set; } = SystemSettingDisplayType.Input;

        /// <summary>默认值</summary>
        public string DefaultValue { get; set; } = "";

        /// <summary>选项配置 JSON（如 RadioGroup/Select 的选项）</summary>
        public string DataContentConfig { get; set; } = "";

        /// <summary>是否禁止删除</summary>
        public bool CantDelete { get; set; } = true;

        /// <summary>排序</summary>
        public int Sort { get; set; } = 0;
    }

    public enum SystemSettingDisplayType
    {
        Input = 0,              // 文本输入框
        InputTextArea = 1,      // 多行文本输入框
        RadioGroup = 2,         // 单选按钮组
        Select = 3,             // 下拉选择
        CheckboxGroup = 4,      // 多选框组
        Switch = 5,             // 开关
        ImageUpload = 6,        // 单图上传
        InputNumber = 7,        // 数字输入框
        InputPassword = 8,      // 密码输入框
        DatePicker = 9,         // 日期选择器
        TimePicker = 10,        // 时间选择器
        RangePicker = 11,       // 日期范围选择器
        Rate = 12,              // 评分
        AutoComplete = 13,      // 自动完成
        Mentions = 14,          // 提及输入
        TreeSelect = 15,        // 树形选择
        IconPicker = 16,        // 图标选择器
        MultiImageUpload = 17,  // 多图上传
        FileUpload = 18,        // 文件上传
    }
}
