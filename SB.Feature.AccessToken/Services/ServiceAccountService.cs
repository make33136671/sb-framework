using FreeSql;
using Microsoft.Extensions.Logging;
using SB.Framework.Models;
using System.Security.Cryptography;

namespace SB.Feature.AccessToken.Services
{
    /// <summary>
    /// 服务账号管理。服务账号 = AccountType=Service 的 SB_Admin,无可用密码、禁止密码登录,
    /// 仅供 API Key 以其身份访问(账号即 token 的"非人类"那一侧,对应原 agent bot)。
    /// </summary>
    public class ServiceAccountService
    {
        private readonly IFreeSql _fsql;
        private readonly ILogger<ServiceAccountService> _logger;

        public ServiceAccountService(IFreeSql fsql, ILogger<ServiceAccountService> logger)
        {
            _fsql = fsql;
            _logger = logger;
        }

        /// <summary>
        /// 按用户名取/建服务账号。已存在则返回(不改其角色);不存在则以指定角色创建。
        /// 密码写入随机不可匹配值(配合 AdminService 的 AccountType=Service 守卫,密码登录恒失败)。
        /// </summary>
        public Admin EnsureServiceAccount(string username, int roleId, string? createdBy = null, string? displayName = null)
        {
            var existing = _fsql.Select<Admin>().Where(a => a.Username == username).First();
            if (existing != null) return existing;

            var account = new Admin
            {
                Username = username,
                // 随机 48 字节 base64,非合法 hash;且 AccountType=Service 在登录处先行拒绝,双保险
                Password = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48)),
                RoleId = roleId,
                Name = displayName ?? username,
                Status = AdminStatus.Active,
                AccountType = AccountType.Service,
            };
            account.Id = (int)_fsql.Insert(account).ExecuteIdentity();
            _logger.LogInformation("created service account: id={Id} username={Username} role={Role} by={By}",
                account.Id, username, roleId, createdBy);
            return account;
        }

        public Admin? GetById(int id) => _fsql.Select<Admin>().Where(a => a.Id == id).First();

        /// <summary>取 super 角色 id(签发 god key / 绑高权账号时校验用)。</summary>
        public int? GetSuperRoleId()
            => _fsql.Select<AdminRole>().Where(r => r.RoleKey == "super").First()?.Id;
    }
}
