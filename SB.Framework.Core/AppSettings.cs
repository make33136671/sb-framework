namespace SB.Framework.Core
{
    public static class AppGlobal
    {
        public static AppSettings AppSettings { get; set; } = null!;

        /// <summary>
        /// 获取当前环境对应的 appsettings 文件路径。
        /// Development → appsettings.Development.json（如果存在），否则 appsettings.json
        /// Production → appsettings.json
        /// </summary>
        public static string GetAppSettingsPath(string contentRootPath, string environmentName)
        {
            var envPath = System.IO.Path.Combine(contentRootPath, $"appsettings.{environmentName}.json");
            if (System.IO.File.Exists(envPath))
                return envPath;
            return System.IO.Path.Combine(contentRootPath, "appsettings.json");
        }
    }

    public class AppSettings
    {
        /// <summary>
        /// 产品中文名称
        /// </summary>
        public string ProductCn { get; set; } = null!;

        /// <summary>
        /// 产品英文名称（小写字母+数字，用于路径前缀、缓存前缀、License Product）
        /// </summary>
        public string ProductEn { get; set; } = "sb";

        /// <summary>
        /// 主域名（如 example.com），用于授权绑定、CORS 等
        /// </summary>
        public string PrimaryDomain { get; set; } = "";

        /// <summary>
        /// 启用Debug模式
        /// </summary>
        public bool Debug { get; set; } = false;

        /// <summary>
        /// 500 unhandled exception 时,响应体是否包含 ex.Message(只 message,不带堆栈)。
        /// 默认 false(安全):响应只显示通用"服务器内部错误",细节只进日志(防止攻击者借错误信息探测系统内部)。
        /// 调试时可在 appsettings.json 设 true 让 admin UI 直接看到错误细节,无需 ssh 翻日志。
        /// </summary>
        public bool ExposeErrorDetails { get; set; } = false;

        /// <summary>
        /// 运行后台任务
        /// </summary>
        public bool RunBackgroundService { get; set; } = false;

        /// <summary>
        /// 开启Swagger
        /// </summary>
        public bool UseSwagger { get; set; } = false;


        /// <summary>
        /// 缓存配置
        /// </summary>
        public CacheConfig CacheConfig { get; set; } = new();

        /// <summary>
        /// 密码存储配置
        /// </summary>
        public PasswordConfig PasswordConfig { get; set; } = new();

        /// <summary>
        /// 敏感数据加密密钥（银行卡号等），AES-256 要求至少 16 字符
        /// </summary>
        public string EncryptionKey { get; set; } = "";

        /// <summary>
        /// 程序启动端口信息
        /// </summary>
        public WebHost WebHost { get; set; } = null!;

        /// <summary>
        /// Admin Jwt加密授权信息
        /// </summary>
        public JwtAdmin JwtAdmin { get; set; } = null!;

        /// <summary>
        /// Api Jwt加密授权信息
        /// </summary>
        public JwtAPI JwtAPI { get; set; } = null!;

        /// <summary>
        /// 数据库配置信息
        /// </summary>
        public DbConfig DbConfig { get; set; } = null!;

        /// <summary>
        /// 登录频率限制配置
        /// </summary>
        public RateLimitConfig RateLimitConfig { get; set; } = new();


        /// <summary>
        /// 上传相关配置（直传 + 切片统一）
        /// </summary>
        public UploadConfig UploadConfig { get; set; } = new();

        /// <summary>
        /// 定时作业开启
        /// </summary>
        public bool TaskRun { get; set; }

        /// <summary>
        /// 授权许可配置
        /// </summary>
        public LicenseConfig LicenseConfig { get; set; } = new();

        /// <summary>
        /// 云端中心配置：所有云端服务（授权 / 数据库备份接收 / 未来其他）共用一个根地址
        /// 在安装向导里填一次，运行时各功能拼接子路径调用
        /// </summary>
        public CloudConfig CloudConfig { get; set; } = new();

        /// <summary>
        /// 取云端中心地址（纯基地址，不带子路径）。
        /// 所有云端功能（授权 / 数据库备份 / 未来其他）共用此地址，自行拼子路径。
        /// </summary>
        public string GetCloudServerUrl()
            => CloudConfig?.ServerUrl?.TrimEnd('/') ?? "";

        /// <summary>
        /// 取数据库备份云端接收地址：CloudConfig.ServerUrl + "/api/BackupReceive/Upload"
        /// </summary>
        public string GetBackupUploadUrl()
        {
            var cloud = CloudConfig?.ServerUrl?.TrimEnd('/');
            return string.IsNullOrEmpty(cloud) ? "" : $"{cloud}/api/BackupReceive/Upload";
        }

        /// <summary>
        /// 部署配置
        /// </summary>
        public DeployConfig DeployConfig { get; set; } = new();
    }

    public class WebHost
    {
        /// <summary>
        /// 运行地址
        /// </summary>
        public string Host { get; set; } = null!;

        /// <summary>
        /// 运行端口号
        /// </summary>
        public int Port { get; set; }
    }

    public class JwtAdmin
    {
        /// <summary>
        ///
        /// </summary>
        public string Key { get; set; } = null!;
        /// <summary>
        ///
        /// </summary>
        public string Issuer { get; set; } = null!;
        /// <summary>
        ///
        /// </summary>
        public string Audience { get; set; } = null!;
        /// <summary>
        ///
        /// </summary>
        public string Expires { get; set; } = null!;
    }

    public class JwtAPI
    {
        /// <summary>
        /// 
        /// </summary>
        public string Key { get; set; } = null!;
        /// <summary>
        /// 
        /// </summary>
        public string Issuer { get; set; } = null!;
        /// <summary>
        /// 
        /// </summary>
        public string Audience { get; set; } = null!;
        /// <summary>
        /// 
        /// </summary>
        public string Expires { get; set; } = null!;
    }

    public class DbConfig
    {
        /// <summary>
        /// 数据库类型: "MySql" | "PostgreSql"
        /// </summary>
        public string DbType { get; set; } = "MySql";

        /// <summary>
        /// 数据库服务器地址
        /// </summary>
        public string Server { get; set; } = null!;

        /// <summary>
        /// 端口号
        /// </summary>
        public string Port { get; set; } = null!;

        /// <summary>
        /// 数据库名称
        /// </summary>
        public string Database { get; set; } = null!;

        /// <summary>
        /// 数据库用户名
        /// </summary>
        public string User { get; set; } = null!;

        /// <summary>
        /// 数据库密码
        /// </summary>
        public string Password { get; set; } = null!;

        /// <summary>
        /// 自动同步数据库结构,正式环境中慎用
        /// </summary>
        public string AutoSyncStructure { get; set; } = null!;

        /// <summary>
        /// 是否在控制台输出Sql日志
        /// </summary>
        public string MonitorSqlCommandForLog { get; set; } = null!;

        /// <summary>
        /// 慢查询阈值（毫秒），超过此值的查询将记录为Warning日志
        /// </summary>
        public int SlowQueryThresholdMs { get; set; } = 200;
    }


    /// <summary>
    /// 上传相关配置（直传 + 切片统一）
    /// </summary>
    public class UploadConfig
    {
        /// <summary>
        /// 文件大小上限（MB），超过即拒绝
        /// </summary>
        public long MaxFileSizeMB { get; set; } = 2048;

        /// <summary>
        /// 切片阈值（MB）。文件 &gt; 此值走切片上传，&lt;= 此值走直传
        /// </summary>
        public int ChunkThresholdMB { get; set; } = 10;

        /// <summary>
        /// 切片大小（MB）
        /// </summary>
        public int ChunkSizeMB { get; set; } = 1;

        /// <summary>
        /// 切片上传的并发线程数（前端默认值，业务方 props 可覆盖）
        /// </summary>
        public int MaxConcurrent { get; set; } = 4;

        /// <summary>
        /// 上传文件最终落盘目录（相对 wwwroot）
        /// </summary>
        public string UploadPath { get; set; } = "/storage/uploads/";

        /// <summary>
        /// 切片临时目录（相对 wwwroot）
        /// </summary>
        public string ChunkTempPath { get; set; } = "/storage/chunks/";

        /// <summary>
        /// 未完成切片上传的过期小时数
        /// </summary>
        public int ChunkExpireHours { get; set; } = 24;

        /// <summary>
        /// 允许的文件扩展名（小写、含点，如 ".jpg"）；空列表表示不限制
        /// </summary>
        public List<string> AllowedExtensions { get; set; } = new();

        /// <summary>
        /// 调试限速：分片上传接口每片人为延迟（毫秒），&gt; 0 才生效；仅 Development 用
        /// </summary>
        public int DebugThrottleMs { get; set; } = 0;
    }

    /// <summary>
    /// 登录频率限制配置
    /// </summary>
    public class RateLimitConfig
    {
        /// <summary>
        /// 时间窗口大小（秒）
        /// </summary>
        public int WindowSeconds { get; set; } = 60;

        /// <summary>
        /// 窗口内最大请求数
        /// </summary>
        public int PermitLimit { get; set; } = 10;
    }

    /// <summary>
    /// 缓存系统配置
    /// </summary>
    public class CacheConfig
    {
        /// <summary>
        /// 是否启用缓存（false 时将使用 No-Op 缓存实现）
        /// </summary>
        public bool Enabled { get; set; } = false;

        /// <summary>
        /// 缓存提供程序: "Memory" | "Redis"（仅当 Enabled 为 true 时有效）
        /// </summary>
        public string Provider { get; set; } = "Memory";

        /// <summary>
        /// Redis 配置（仅 Provider 为 "Redis" 时生效）
        /// </summary>
        public RedisConfig Redis { get; set; } = new();
    }

    /// <summary>
    /// 密码存储配置
    /// 注意：此配置在系统安装后不应修改，否则会导致密码验证失败
    /// </summary>
    public class PasswordConfig
    {
        /// <summary>
        /// 密码存储模式: "Encrypted"（加密） | "PlainText"（明文）
        /// 默认为加密模式，强烈推荐生产环境使用加密模式
        /// 警告：不兼容修改 - 一旦选定不能修改
        /// </summary>
        public string StorageMode { get; set; } = "Encrypted";
    }

    /// <summary>
    /// 授权许可配置 — 只存运行时授权 Key；授权服务器地址统一走 CloudConfig.ServerUrl
    /// </summary>
    public class LicenseConfig
    {
        /// <summary>
        /// 授权Key（签名后的授权文件内容）
        /// </summary>
        public string Key { get; set; } = "";
    }

    /// <summary>
    /// 云端中心配置：所有云端服务（授权 / 数据库备份接收 / 未来其他）共用此根地址
    /// </summary>
    public class CloudConfig
    {
        /// <summary>
        /// 云端中心根地址，如 https://cloud.saibo.club
        /// 各功能在此地址上拼接子路径：
        ///   授权: {ServerUrl}/api/verify/...
        ///   数据库备份接收: {ServerUrl}/api/BackupReceive/Upload
        /// </summary>
        public string ServerUrl { get; set; } = "";
    }

    /// <summary>
    /// 部署配置
    /// </summary>
    public class DeployConfig
    {
        /// <summary>
        /// 可部署的前端项目列表
        /// </summary>
        public List<DeployProject> Projects { get; set; } = new();
    }

    /// <summary>
    /// 部署项目
    /// </summary>
    public class DeployProject
    {
        /// <summary>
        /// 显示名称，如 "Admin管理后台"
        /// </summary>
        public string Name { get; set; } = null!;

        /// <summary>
        /// 唯一标识，如 "admin"
        /// </summary>
        public string Key { get; set; } = null!;

        /// <summary>
        /// 部署目录绝对路径，如 "/www/wwwroot/sb_admin/"
        /// </summary>
        public string Path { get; set; } = null!;
    }

    /// <summary>
    /// Redis 连接配置
    /// </summary>
    public class RedisConfig
    {
        /// <summary>
        /// Redis 连接字符串
        /// </summary>
        public string Connection { get; set; } = "localhost:6379";

        /// <summary>
        /// 键名前缀，用于多应用共享 Redis 时隔离
        /// </summary>
        public string InstanceName { get; set; } = "SB:";
    }

}

