namespace SB.Framework.Models.Dto
{
    public class DatabaseConfigDto
    {
        public string DbType { get; set; } = "MySql";
        public string Server { get; set; } = "127.0.0.1";
        public string Port { get; set; } = "3306";
        public string Database { get; set; } = "Saibo";
        public string User { get; set; } = "root";
        public string Password { get; set; }
    }

    public class RedisConfigDto
    {
        public string Connection { get; set; } = "localhost:6379";
        public string InstanceName { get; set; } = "SB:";
    }

    public class DeployProjectDto
    {
        public string Name { get; set; }
        public string Key { get; set; }
        public string Path { get; set; }
    }

    public class SetupRequest
    {
        public DatabaseConfigDto Database { get; set; }

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

        public RedisConfigDto Redis { get; set; }

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
        public string[] CorsOrigins { get; set; }
        public string AdminUsername { get; set; } = "admin";
        public string AdminPassword { get; set; }
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

        /// <summary>
        /// 是否启用 MasterKey（万能密钥）
        /// 启用后安装时生成一次性显示的 GUID，写入 appsettings 的 MasterKey 字段，
        /// 客户端可通过 X-Master-Key 请求头调用任意接口；
        /// 不启用则 MasterKey 写空字符串，X-Master-Key 通道关闭。生产环境建议关闭。
        /// </summary>
        public bool EnableMasterKey { get; set; } = false;
    }
}
