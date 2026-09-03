using FreeSql.DataAnnotations;

namespace SB.Framework.Models
{
    /// <summary>
    /// 部署作业 — 合并原 UploadRecord(上传跟踪)+ DeployRecord(部署快照备份)。
    ///
    /// 状态机:Uploaded → Deploying → Deployed | Failed,Deployed → RolledBack。
    /// 回滚动作产生新行,RollbackOfJobId 指向被回滚的 job,保留完整审计链。
    /// 文件统一存 deploy_archive/,Status 决定它是 staging / archive / rolled-back 文件。
    /// </summary>
    [Table(Name = "SB_DeployJob")]
    public class DeployJob : IEntity
    {
        [Column(Name = "Id", IsPrimary = true, IsIdentity = true)]
        public int Id { get; set; }

        /// <summary>部署目标:api 或 DeployConfig 中的 key(admin / h5 / 等)</summary>
        [Column(Name = "Target", StringLength = 50)]
        public string Target { get; set; } = "";

        /// <summary>目标显示名:"API程序" / "Admin管理后台" 等</summary>
        [Column(Name = "TargetName", StringLength = 100)]
        public string TargetName { get; set; } = "";

        /// <summary>原始文件名</summary>
        [Column(Name = "FileName", StringLength = 200)]
        public string FileName { get; set; } = "";

        /// <summary>文件大小(字节)</summary>
        [Column(Name = "FileSize")]
        public long FileSize { get; set; }

        /// <summary>持久化文件相对路径(相对 deploy_archive 根)</summary>
        [Column(Name = "StoragePath", StringLength = 500)]
        public string StoragePath { get; set; } = "";

        /// <summary>Uploaded / Deploying / Deployed / Failed / RolledBack</summary>
        [Column(Name = "Status", StringLength = 20)]
        public string Status { get; set; } = DeployJobStatus.Uploaded;

        /// <summary>失败原因(Status=Failed 时写入)</summary>
        [Column(Name = "ErrorMessage", StringLength = 1000, IsNullable = true)]
        public string? ErrorMessage { get; set; }

        /// <summary>上传人</summary>
        [Column(Name = "UploadedBy", StringLength = 100)]
        public string UploadedBy { get; set; } = "";

        /// <summary>上传时间</summary>
        [Column(Name = "UploadedAt", InsertValueSql = "now()")]
        public DateTime UploadedAt { get; set; }

        /// <summary>触发部署的人(可能 != 上传人)</summary>
        [Column(Name = "DeployedBy", StringLength = 100, IsNullable = true)]
        public string? DeployedBy { get; set; }

        /// <summary>部署开始时间(Deploying 转移那一刻)</summary>
        [Column(Name = "DeployStartedAt", IsNullable = true)]
        public DateTime? DeployStartedAt { get; set; }

        /// <summary>部署结束时间(Deployed/Failed 那一刻)</summary>
        [Column(Name = "DeployFinishedAt", IsNullable = true)]
        public DateTime? DeployFinishedAt { get; set; }

        /// <summary>本次部署覆盖了哪个 Deployed job(回滚目标)</summary>
        [Column(Name = "PreviousJobId", IsNullable = true)]
        public int? PreviousJobId { get; set; }

        /// <summary>本 job 是哪个 job 的回滚动作(非空 = 这是回滚产生的 job)</summary>
        [Column(Name = "RollbackOfJobId", IsNullable = true)]
        public int? RollbackOfJobId { get; set; }

        /// <summary>回滚操作人(被这个 job 回滚掉的时候写入,即转 RolledBack 的瞬间)</summary>
        [Column(Name = "RolledBackBy", StringLength = 100, IsNullable = true)]
        public string? RolledBackBy { get; set; }

        /// <summary>被回滚时间(转 RolledBack 的瞬间)</summary>
        [Column(Name = "RolledBackAt", IsNullable = true)]
        public DateTime? RolledBackAt { get; set; }

        /// <summary>备注(可选,人工填)</summary>
        [Column(Name = "Remark", StringLength = 500, IsNullable = true)]
        public string? Remark { get; set; }
    }

    public static class DeployJobStatus
    {
        /// <summary>已上传,等待触发部署</summary>
        public const string Uploaded = "Uploaded";

        /// <summary>正在部署</summary>
        public const string Deploying = "Deploying";

        /// <summary>部署成功,作为目标当前活动版本</summary>
        public const string Deployed = "Deployed";

        /// <summary>部署失败,文件仍在,可重试</summary>
        public const string Failed = "Failed";

        /// <summary>已被回滚 — 曾经 Deployed,后被另一个 job(RollbackOfJobId 指向本行)取代</summary>
        public const string RolledBack = "RolledBack";
    }
}
