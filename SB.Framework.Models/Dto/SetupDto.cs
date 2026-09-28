namespace SB.Framework.Models.Dto
{
    public class DatabaseConfigDto
    {
        public string DbType { get; set; } = "MySql";
        public string Server { get; set; } = "127.0.0.1";
        public string Port { get; set; } = "3306";
        public string Database { get; set; } = "Saibo";
        public string User { get; set; } = "root";
        public string Password { get; set; } = null!;
    }

    public class RedisConfigDto
    {
        public string Connection { get; set; } = "localhost:6379";
        public string InstanceName { get; set; } = "SB:";
    }

    public class DeployProjectDto
    {
        public string Name { get; set; } = null!;
        public string Key { get; set; } = null!;
        public string Path { get; set; } = null!;
    }

    /// <summary>
    /// 安装向导「生成 Logo」请求（仅未安装时可调用）。
    /// 按产品名推导品牌规格并渲染 Logo，返回 base64 预览图（与 images/logo.png 一致）。
    /// 用户接受后前端将其放入 Install 的 LogoBase64；也可改传上传图，或留空（留空由安装流程自动生成）。
    /// </summary>
    public class SetupGenerateLogoRequest
    {
        public string? ProductCn { get; set; }
        public string? ProductEn { get; set; }

        /// <summary>单字标（可选，1-3 字符；缺省按产品英文名推导，如 tgvault → TG）</summary>
        public string? Monogram { get; set; }

        /// <summary>渐变起色（可选，#RRGGBB；缺省品牌青 #22D3EE）</summary>
        public string? AccentFrom { get; set; }

        /// <summary>渐变止色（可选，#RRGGBB；缺省品牌蓝 #3B82F6）</summary>
        public string? AccentTo { get; set; }
    }

    public class SetupRequest
    {
        public DatabaseConfigDto Database { get; set; } = null!;

        /// <summary>
        /// 是否抹除现有数据库数据（重新安装时使用）
        /// </summary>
        public bool WipeDatabase { get; set; } = false;

        /// <summary>
        /// 是否启用缓存（默认 false）
        /// </summary>
        public bool EnableCache { get; set; } = false;

        /// <summary>
        /// 缓存驱动（仅当 EnableCache 为 true 时有效）
        /// </summary>
        public string CacheProvider { get; set; } = "Memory";

        public RedisConfigDto Redis { get; set; } = null!;

        /// <summary>
        /// 密码存储模式: "Encrypted"（加密） | "PlainText"（明文）
        /// 默认为加密模式，不兼容修改
        /// </summary>
        public string PasswordStorageMode { get; set; } = "Encrypted";

        public string ProductCn { get; set; } = "管理系统";

        /// <summary>
        /// 产品英文名称（小写字母+数字，2-20字符，字母开头）
        /// </summary>
        public string ProductEn { get; set; } = "sb";

        /// <summary>
        /// 主域名（如 example.com）
        /// </summary>
        public string? PrimaryDomain { get; set; }

        /// <summary>
        /// 缓存 Key 前缀（默认基于产品英文名，可自定义）
        /// </summary>
        public string CachePrefix { get; set; } = "sb:";

        /// <summary>
        /// Logo 图片 Base64（安装时上传，格式 data:image/png;base64,xxx）
        /// </summary>
        public string? LogoBase64 { get; set; }

        public string TimeZone { get; set; } = "Asia/Shanghai";
        public bool CorsAllowAll { get; set; } = true;
        public string[] CorsOrigins { get; set; } = null!;
        public string AdminUsername { get; set; } = "admin";
        public string AdminPassword { get; set; } = null!;
        public string AdminDisplayName { get; set; } = "超级管理员";

        /// <summary>
        /// 云端中心根地址（如 https://cloud.saibo.club），纯基地址不带子路径。
        /// 所有云端服务（授权 / 数据库备份 / 未来其他）共用此地址，运行时各功能自行拼接子路径。
        /// </summary>
        public string? CloudServerUrl { get; set; }

        /// <summary>
        /// 部署项目列表（可选）
        /// </summary>
        public List<DeployProjectDto> DeployProjects { get; set; } = new();
    }
}

