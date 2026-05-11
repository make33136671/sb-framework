using FreeSql;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using SB.Framework.Core.Attribute;
using SB.Framework.Core.Authentication.Jwt;
using SB.Framework.Core.Expand;
using SB.Framework.Core.Util;
using SB.Framework.Models;
using System.Security.Claims;

namespace SB.Framework.Services
{
    [AutoDependencyInjection]
    public class ApiAuthService : BaseRepository<Member>
    {
        IFreeSql _fsql;
        ILogger<ApiAuthService> _logger;
        IHttpContextAccessor _httpContextAccessor;
        IMemoryCache _cache;
        JwtToken _jwtToken;

        public ApiAuthService(IFreeSql fsql, ILogger<ApiAuthService> logger,
            IHttpContextAccessor httpContextAccessor, IMemoryCache cache,
            JwtToken jwtToken) : base(fsql)
        {
            _fsql = fsql;
            _logger = logger;
            _httpContextAccessor = httpContextAccessor;
            _cache = cache;
            _jwtToken = jwtToken;
        }

        private int GetCurrentUserId()
        {
            return _httpContextAccessor.HttpContext.User.Id();
        }

        private string GenerateInvitationCode()
        {
            var code = Guid.NewGuid().ToString("N").Substring(0, 8).ToUpper();
            while (Select.Where(c => c.InvitationCode == code).Any())
            {
                code = Guid.NewGuid().ToString("N").Substring(0, 8).ToUpper();
            }
            return code;
        }

        /// <summary>
        /// H5用户登录
        /// </summary>
        public virtual Result Login(string username, string password)
        {
            if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
                return Result.Fail("请输入用户名和密码");

            Member user = Select.Where(c => c.Username == username).First();

            var ctx = _httpContextAccessor.HttpContext;

            if (user == null || !PasswordUtil.VerifyPassword(password, user.Password))
            {
                // 记录失败登录日志
                if (user != null)
                {
                    _fsql.Insert(new LoginLog
                    {
                        UserId = user.Id,
                        UserType = 0,
                        Username = username,
                        IpAddress = ctx?.Connection?.RemoteIpAddress?.ToString(),
                        UserAgent = ctx?.Request?.Headers["User-Agent"].ToString(),
                        Status = 1,
                        CreateTime = AppTime.Now
                    }).ExecuteAffrows();
                }
                return Result.Fail("账号或密码错误");
            }

            if (user.Status != UserStatus.Active)
                return Result.Fail("账号已被封禁");

            user.LastOnlineTime = AppTime.Now;
            UpdateDiy.UpdateColumns(c => c.LastOnlineTime).SetSource(user).ExecuteAffrows();

            var claimList = new List<Claim>()
            {
                new Claim("Id", user.Id.ToString()),
                new Claim("Username", user.Username),
                new Claim("UserType", "Member"),
            };

            string accessToken = _jwtToken.CreateToken("JwtApi", claimList);
            _cache.Set(user.Id.ToString(), accessToken);

            // 写入登录日志
            _fsql.Insert(new LoginLog
            {
                UserId = user.Id,
                UserType = 0,
                Username = user.Username,
                IpAddress = ctx?.Connection?.RemoteIpAddress?.ToString(),
                UserAgent = ctx?.Request?.Headers["User-Agent"].ToString(),
                Status = 0,
                CreateTime = AppTime.Now
            }).ExecuteAffrows();

            return Result.Success("登录成功", new
            {
                accessToken,
                userInfo = new
                {
                    user.Id,
                    user.Username,
                    user.Name,
                    Avatar = user.Avatar.ToAbsoluteUrl(ctx)
                }
            });
        }

        /// <summary>
        /// H5用户注册
        /// </summary>
        public virtual Result Register(string username, string password, string name, string? inviteCode = null)
        {
            if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
                return Result.Fail("请输入用户名和密码");

            var existing = Select.Where(c => c.Username == username).First();
            if (existing != null)
                return Result.Fail("该用户名已被注册");

            Member? inviter = null;
            if (!string.IsNullOrWhiteSpace(inviteCode))
            {
                inviter = Select.Where(c => c.InvitationCode == inviteCode).First();
                if (inviter == null)
                    return Result.Fail("邀请码无效");
                if (inviter.Status != UserStatus.Active)
                    return Result.Fail("邀请人账号异常");
            }

            var user = new Member
            {
                Username = username,
                Password = PasswordUtil.EncryptPassword(password),
                Name = string.IsNullOrEmpty(name) ? username : name,
                Avatar = "/logo.png",
                CreatedTime = AppTime.Now,
                LastOnlineTime = AppTime.Now,
                CreditScore = 100,
                RealNameStatus = RealNameStatus.Unverified,
                InvitationCode = GenerateInvitationCode(),
                ParentId = inviter?.Id,
                AncestryPath = inviter != null
                    ? (string.IsNullOrEmpty(inviter.AncestryPath) ? $"/{inviter.Id}/" : $"{inviter.AncestryPath}{inviter.Id}/")
                    : null,
            };

            user = this.Insert(user);

            var claimList = new List<Claim>()
            {
                new Claim("Id", user.Id.ToString()),
                new Claim("Username", user.Username),
                new Claim("UserType", "Member"),
            };

            string accessToken = _jwtToken.CreateToken("JwtApi", claimList);
            _cache.Set(user.Id.ToString(), accessToken);

            var ctx = _httpContextAccessor.HttpContext;
            return Result.Success("注册成功", new
            {
                accessToken,
                userInfo = new
                {
                    user.Id,
                    user.Username,
                    user.Name,
                    Avatar = user.Avatar.ToAbsoluteUrl(ctx)
                }
            });
        }

        /// <summary>
        /// 忘记密码
        /// </summary>
        public virtual Result ForgotPassword(string username)
        {
            if (string.IsNullOrEmpty(username))
                return Result.Fail("请输入注册邮箱");

            var user = Select.Where(c => c.Username == username).First();
            if (user == null)
                return Result.Fail("该用户不存在");

            // TODO: 发送重置密码邮件
            return Result.Success("重置密码链接已发送到您的邮箱");
        }

        /// <summary>
        /// 用户退出登录
        /// </summary>
        public virtual Result Logout()
        {
            int userId = GetCurrentUserId();
            _cache.Remove(userId.ToString());
            return Result.Success("退出成功");
        }

        /// <summary>
        /// 获取当前用户信息
        /// </summary>
        public virtual Result GetInfo()
        {
            int userId = GetCurrentUserId();
            var user = Select.Where(c => c.Id == userId).First();

            if (user == null)
                return Result.Fail("登录已失效，请重新登录", 401);

            if (string.IsNullOrEmpty(user.InvitationCode))
            {
                user.InvitationCode = GenerateInvitationCode();
                UpdateDiy.UpdateColumns(c => c.InvitationCode).SetSource(user).ExecuteAffrows();
            }

            string? inviterName = null;
            if (user.ParentId.HasValue)
                inviterName = Select.Where(c => c.Id == user.ParentId.Value).First(c => c.Username);

            var directInviteCount = Select.Where(c => c.ParentId == userId).Count();

            var ctx = _httpContextAccessor.HttpContext;
            return Result.Success(new
            {
                user.Id,
                user.Username,
                user.Name,
                Avatar = user.Avatar.ToAbsoluteUrl(ctx),
                user.CreditScore,
                user.RealNameStatus,
                user.InvitationCode,
                HasPaymentPassword = !string.IsNullOrEmpty(user.PaymentPassword),
                user.ParentId,
                InviterName = inviterName,
                DirectInviteCount = directInviteCount,
            });
        }

        public virtual Result UpdateNickname(string name)
        {
            int userId = GetCurrentUserId();
            var user = Select.Where(c => c.Id == userId).First();
            if (user == null) return Result.Fail("用户不存在");

            user.Name = name;
            UpdateDiy.UpdateColumns(c => c.Name).SetSource(user).ExecuteAffrows();
            return Result.Success("修改成功");
        }

        public virtual Result UpdateAvatar(string avatar)
        {
            int userId = GetCurrentUserId();
            var user = Select.Where(c => c.Id == userId).First();
            if (user == null) return Result.Fail("用户不存在");

            user.Avatar = avatar.ToRelativePath();
            UpdateDiy.UpdateColumns(c => c.Avatar).SetSource(user).ExecuteAffrows();
            return Result.Success("修改成功");
        }

        public virtual Result ChangeLoginPassword(string oldPassword, string newPassword)
        {
            int userId = GetCurrentUserId();
            var user = Select.Where(c => c.Id == userId).First();
            if (user == null) return Result.Fail("用户不存在");

            if (!PasswordUtil.VerifyPassword(oldPassword, user.Password))
                return Result.Fail("原密码错误");

            user.Password = PasswordUtil.EncryptPassword(newPassword);
            UpdateDiy.UpdateColumns(c => c.Password).SetSource(user).ExecuteAffrows();
            return Result.Success("密码修改成功");
        }

        public virtual Result ChangePaymentPassword(string oldPassword, string newPassword)
        {
            int userId = GetCurrentUserId();
            var user = Select.Where(c => c.Id == userId).First();
            if (user == null) return Result.Fail("用户不存在");

            if (!string.IsNullOrEmpty(user.PaymentPassword) && !PasswordUtil.VerifyPassword(oldPassword, user.PaymentPassword))
                return Result.Fail("原支付密码错误");

            user.PaymentPassword = PasswordUtil.EncryptPassword(newPassword);
            UpdateDiy.UpdateColumns(c => c.PaymentPassword).SetSource(user).ExecuteAffrows();
            return Result.Success("支付密码修改成功");
        }

        /// <summary>
        /// 提交实名认证
        /// </summary>
        public virtual Result SubmitRealName(string realName, string idNumber, string idFrontImage, string idBackImage)
        {
            int userId = GetCurrentUserId();
            var user = Select.Where(c => c.Id == userId).First();
            if (user == null) return Result.Fail("用户不存在");

            if (user.RealNameStatus == RealNameStatus.Verified)
                return Result.Fail("您已完成实名认证");
            if (user.RealNameStatus == RealNameStatus.Reviewing)
                return Result.Fail("您的认证正在审核中，请耐心等待");

            if (string.IsNullOrEmpty(realName) || string.IsNullOrEmpty(idNumber)
                || string.IsNullOrEmpty(idFrontImage) || string.IsNullOrEmpty(idBackImage))
                return Result.Fail("请完善所有认证信息");

            var record = new MemberRealName
            {
                MemberId = userId,
                RealName = realName,
                IdNumber = idNumber,
                IdFrontImage = idFrontImage,
                IdBackImage = idBackImage,
                Status = RealNameStatus.Reviewing,
            };

            _fsql.Insert(record).ExecuteAffrows();

            user.RealNameStatus = RealNameStatus.Reviewing;
            UpdateDiy.UpdateColumns(c => c.RealNameStatus).SetSource(user).ExecuteAffrows();

            return Result.Success("认证信息已提交，请等待审核");
        }

        /// <summary>
        /// 获取实名认证信息
        /// </summary>
        public virtual Result GetRealNameInfo()
        {
            int userId = GetCurrentUserId();
            var user = Select.Where(c => c.Id == userId).First();
            if (user == null) return Result.Fail("用户不存在");

            var record = _fsql.Select<MemberRealName>()
                .Where(c => c.MemberId == userId)
                .OrderByDescending(c => c.Id)
                .First();

            if (record == null)
                return Result.Success(new { Status = (int)RealNameStatus.Unverified });

            var ctx = _httpContextAccessor.HttpContext;
            return Result.Success(new
            {
                Status = (int)record.Status,
                record.RealName,
                record.IdNumber,
                IdFrontImage = record.IdFrontImage.ToAbsoluteUrl(ctx),
                IdBackImage = record.IdBackImage.ToAbsoluteUrl(ctx),
                record.RejectReason,
                record.SubmitTime,
                record.ReviewTime,
            });
        }
    }
}
