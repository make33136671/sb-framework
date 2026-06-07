namespace SB.Feature.LicenseManage;

/// <summary>
/// 授权管理（签发端）强类型配置。
/// 绑定自 appsettings 节 "Features:LicenseManage"。
/// </summary>
public sealed class LicenseManageOptions
{
    /// <summary>
    /// RSA 密钥文件目录。空字符串 = 回退到 ContentRootPath。
    /// 开发调试场景下让签发端与构建嵌入端指向同一处密钥，避免公钥不一致导致本机 ApplyKey 签名验证失败。
    /// （历史上靠 SB_LICENSE_KEY_DIR 环境变量；保留环境变量优先，未配置时再用本选项。）
    /// </summary>
    public string KeyDirectory { get; set; } = "";   // 空 = ContentRoot

    /// <summary>默认有效期（天）。</summary>
    public int DefaultValidDays { get; set; } = 365;
}
