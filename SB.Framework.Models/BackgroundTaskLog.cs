using FreeSql.DataAnnotations;
using System;

namespace SB.Framework.Models
{
    /// <summary>
    /// 系统日志级别
    /// </summary>
    public enum TaskLogLevel
    {
        Error = 0,
        Warning = 1,
        Info = 2
    }

    /// <summary>
    /// 系统日志来源
    /// </summary>
    public enum LogSource
    {
        /// <summary>定时任务</summary>
        Task = 0,
        /// <summary>系统异常</summary>
        Exception = 1,
    }

    /// <summary>
    /// 系统日志
    /// </summary>
    [Table(Name = "SYS_BackgroundTaskLog")]
    public class BackgroundTaskLog : IEntity
    {
        [Column(Name = "Id", IsPrimary = true, IsIdentity = true)]
        public int Id { get; set; }

        [Column(Name = "Source")]
        public LogSource Source { get; set; } = LogSource.Task;

        [Column(Name = "TaskName", StringLength = 200, IsNullable = false)]
        public string TaskName { get; set; }

        [Column(Name = "LogLevel")]
        public TaskLogLevel LogLevel { get; set; }

        [Column(Name = "Message", StringLength = 2000, IsNullable = false)]
        public string Message { get; set; }

        [Column(Name = "Exception", DbType = "text", IsNullable = true)]
        public string? Exception { get; set; }

        [Column(Name = "CreatedTime", DbType = "timestamp", InsertValueSql = "now()", CanUpdate = false)]
        public DateTime CreatedTime { get; set; }

        [Column(Name = "ExecutionRecordId", StringLength = 50, IsNullable = true)]
        public string? ExecutionRecordId { get; set; }
    }
}
