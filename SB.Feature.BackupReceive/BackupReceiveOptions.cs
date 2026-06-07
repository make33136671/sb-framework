namespace SB.Feature.BackupReceive
{
    /// <summary>
    /// 云端备份接收端强类型配置。
    /// 框架自动绑定到 appsettings 的 "Features:BackupReceive" 节。
    /// </summary>
    public sealed class BackupReceiveOptions
    {
        /// <summary>备份文件落盘根目录（相对 ContentRootPath），按 {productEn}/{ip}/{filename} 分层。</summary>
        public string StoragePath { get; set; } = "wwwroot/storage/remote-backups";

        /// <summary>单个备份文件大小上限（MB）。</summary>
        public int MaxFileSizeMB { get; set; } = 500;

        /// <summary>备份保留天数（超期清理策略用）。</summary>
        public int RetentionDays { get; set; } = 90;
    }
}
