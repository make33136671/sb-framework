using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using SB.Framework.Core.Util;
using SB.Framework.Models;
using SB.Framework.Services;

namespace SB.Framework.WebApi.Controllers
{
    [ApiController, Authorize(AuthenticationSchemes = "JwtAdmin"), Route("[controller]/[action]")]
    public class EmailConfigController : Controller
    {
        private readonly IFreeSql _fsql;
        private readonly ILogger<EmailConfigController> _logger;

        public EmailConfigController(IFreeSql fsql, ILogger<EmailConfigController> logger)
        {
            _fsql = fsql;
            _logger = logger;
        }

        #region SMTP 配置管理

        [HttpGet]
        public async Task<Result> GetConfig()
        {
            var config = await _fsql.Select<EmailConfig>().OrderBy(c => c.Id).FirstAsync();
            if (config == null)
                return Result.Success(new { });

            return Result.Success(new
            {
                config.Id, config.SmtpServer, config.SmtpPort, config.Username,
                Password = string.IsNullOrEmpty(config.Password) ? "" : "******",
                config.FromEmail, config.FromName, config.EnableSsl, config.IsActive,
                config.CreatedAt, config.UpdatedAt
            });
        }

        [HttpPost]
        public async Task<Result> SaveConfig([FromForm] EmailConfigSaveDto dto)
        {
            var config = await _fsql.Select<EmailConfig>().OrderBy(c => c.Id).FirstAsync();

            if (config == null)
            {
                config = new EmailConfig
                {
                    SmtpServer = dto.SmtpServer, SmtpPort = dto.SmtpPort,
                    Username = dto.Username, Password = dto.Password,
                    FromEmail = dto.FromEmail, FromName = dto.FromName,
                    EnableSsl = dto.EnableSsl, IsActive = dto.IsActive,
                    CreatedAt = AppTime.UtcNow, UpdatedAt = AppTime.UtcNow
                };
                await _fsql.Insert(config).ExecuteAffrowsAsync();
            }
            else
            {
                config.SmtpServer = dto.SmtpServer;
                config.SmtpPort = dto.SmtpPort;
                config.Username = dto.Username;
                if (!string.IsNullOrEmpty(dto.Password) && dto.Password != "******")
                    config.Password = dto.Password;
                config.FromEmail = dto.FromEmail;
                config.FromName = dto.FromName;
                config.EnableSsl = dto.EnableSsl;
                config.IsActive = dto.IsActive;
                config.UpdatedAt = AppTime.UtcNow;

                await _fsql.Update<EmailConfig>().SetSource(config).ExecuteAffrowsAsync();
            }

            return Result.Success("保存成功");
        }

        #endregion

        #region 邮件模板管理

        [HttpGet]
        public async Task<Result> GetTemplates(int page = 1, int pageSize = 20)
        {
            var list = await _fsql.Select<EmailTemplate>().OrderBy(t => t.Id).Page(page, pageSize).ToListAsync();
            var total = await _fsql.Select<EmailTemplate>().CountAsync();
            return Result.Success(new { list, total });
        }

        [HttpGet]
        public async Task<Result> GetTemplate(int id)
        {
            var template = await _fsql.Select<EmailTemplate>().Where(t => t.Id == id).FirstAsync();
            if (template == null) return Result.Fail("模板不存在");
            return Result.Success(template);
        }

        [HttpPost]
        public async Task<Result> SaveTemplate([FromForm] EmailTemplateSaveDto dto)
        {
            if (dto.Id == 0)
            {
                var exists = await _fsql.Select<EmailTemplate>().Where(t => t.Code == dto.Code).AnyAsync();
                if (exists) return Result.Fail("模板代码已存在");

                var template = new EmailTemplate
                {
                    Code = dto.Code, Name = dto.Name, Subject = dto.Subject,
                    Body = dto.Body, IsActive = dto.IsActive,
                    CreatedAt = AppTime.UtcNow, UpdatedAt = AppTime.UtcNow
                };
                await _fsql.Insert(template).ExecuteAffrowsAsync();
                return Result.Success("新增成功");
            }
            else
            {
                var template = await _fsql.Select<EmailTemplate>().Where(t => t.Id == dto.Id).FirstAsync();
                if (template == null) return Result.Fail("模板不存在");

                var codeExists = await _fsql.Select<EmailTemplate>().Where(t => t.Code == dto.Code && t.Id != dto.Id).AnyAsync();
                if (codeExists) return Result.Fail("模板代码已被其他模板使用");

                template.Code = dto.Code;
                template.Name = dto.Name;
                template.Subject = dto.Subject;
                template.Body = dto.Body;
                template.IsActive = dto.IsActive;
                template.UpdatedAt = AppTime.UtcNow;

                await _fsql.Update<EmailTemplate>().SetSource(template).ExecuteAffrowsAsync();
                return Result.Success("更新成功");
            }
        }

        #endregion

        #region 邮件日志管理

        [HttpGet]
        public async Task<Result> GetLogs(int page = 1, int pageSize = 20, EmailStatus? status = null)
        {
            var query = _fsql.Select<EmailLog>();
            if (status.HasValue)
                query = query.Where(l => l.Status == status.Value);

            var list = await query.OrderByDescending(l => l.Id).Page(page, pageSize).ToListAsync();
            var total = await query.CountAsync();
            return Result.Success(new { list, total });
        }

        [HttpGet]
        public async Task<Result> GetLog(int id)
        {
            var log = await _fsql.Select<EmailLog>().Where(l => l.Id == id).FirstAsync();
            if (log == null) return Result.Fail("日志不存在");
            return Result.Success(log);
        }

        #endregion

        #region Resend 配置管理

        [HttpGet]
        public async Task<Result> GetResendConfig()
        {
            var apiKey = (await _fsql.Select<SystemSetting>().Where(s => s.Key == "Email_ResendApiKey").FirstAsync())?.DataContent;
            var fromEmail = (await _fsql.Select<SystemSetting>().Where(s => s.Key == "Email_ResendFromEmail").FirstAsync())?.DataContent;
            var fromName = (await _fsql.Select<SystemSetting>().Where(s => s.Key == "Email_ResendFromName").FirstAsync())?.DataContent;

            return Result.Success(new
            {
                ApiKey = string.IsNullOrEmpty(apiKey) ? "" : "******",
                FromEmail = fromEmail ?? "",
                FromName = fromName ?? ""
            });
        }

        [HttpPost]
        public async Task<Result> SaveResendConfig([FromForm] ResendConfigSaveDto dto)
        {
            if (!string.IsNullOrEmpty(dto.ApiKey) && dto.ApiKey != "******")
                await SaveSystemSettingAsync("Email_ResendApiKey", dto.ApiKey);
            await SaveSystemSettingAsync("Email_ResendFromEmail", dto.FromEmail);
            await SaveSystemSettingAsync("Email_ResendFromName", dto.FromName);
            return Result.Success("保存成功");
        }

        [HttpGet]
        public async Task<Result> GetProvider()
        {
            var setting = await _fsql.Select<SystemSetting>().Where(s => s.Key == "Email_Provider").FirstAsync();
            return Result.Success(new { Provider = setting?.DataContent ?? "GoogleSmtp" });
        }

        [HttpPost]
        public async Task<Result> SetProvider([FromForm] string provider)
        {
            await SaveSystemSettingAsync("Email_Provider", provider);
            return Result.Success("设置成功");
        }

        private async Task SaveSystemSettingAsync(string key, string value)
        {
            var setting = await _fsql.Select<SystemSetting>().Where(s => s.Key == key).FirstAsync();
            if (setting == null)
            {
                setting = new SystemSetting
                {
                    Key = key, Name = key, DataContent = value,
                    DisplayType = SystemSettingDisplayType.Input, CantDelete = true
                };
                await _fsql.Insert(setting).ExecuteAffrowsAsync();
            }
            else
            {
                setting.DataContent = value;
                await _fsql.Update<SystemSetting>().SetSource(setting).ExecuteAffrowsAsync();
            }
        }

        #endregion
    }

    public class ResendConfigSaveDto
    {
        public string ApiKey { get; set; } = null!;
        public string FromEmail { get; set; } = null!;
        public string FromName { get; set; } = null!;
    }

    public class EmailConfigSaveDto
    {
        public string SmtpServer { get; set; } = null!;
        public int SmtpPort { get; set; }
        public string Username { get; set; } = null!;
        public string Password { get; set; } = null!;
        public string FromEmail { get; set; } = null!;
        public string FromName { get; set; } = null!;
        public bool EnableSsl { get; set; }
        public bool IsActive { get; set; }
    }

    public class EmailTemplateSaveDto
    {
        public int Id { get; set; }
        public string Code { get; set; } = null!;
        public string Name { get; set; } = null!;
        public string Subject { get; set; } = null!;
        public string Body { get; set; } = null!;
        public bool IsActive { get; set; }
    }
}

