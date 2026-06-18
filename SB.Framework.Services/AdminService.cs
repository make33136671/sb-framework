using FreeSql;
using Google.Authenticator;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using SB.Framework.Core;
using SB.Framework.Core.Attribute;
using SB.Framework.Core.Authentication.Jwt;
using SB.Framework.Core.Expand;
using SB.Framework.Core.FreeSql;
using SB.Framework.Core.Util;
using SB.Framework.Models;
using SB.Framework.Models.Dto;
using System.Data;
using System.Security.Claims;


namespace SB.Framework.Services
{
    [AutoDependencyInjection]
    public class AdminService : BaseCrudService<Admin>, IModuleInitializer
    {
        #region IModuleInitializer

        public string ModuleName => "核心模块";
        public int InitOrder => 0;

        public Type[] GetEntityTypes() => new[]
        {
            typeof(Admin), typeof(AdminRole), typeof(AdminRoleMenu), typeof(Menu),
            typeof(LoginLog),
            // 授权验证实体（核心基础设施，始终需要建表）
            typeof(LicenseClient), typeof(LicenseRecord), typeof(LicenseRequest), typeof(LicenseVerifyLog),
        };

        public List<SystemSettingDefinition> GetSystemSettings() => new()
        {
#if !DISABLE_LICENSE
            // 授权设置:仅在启用授权验证(未定义 DISABLE_LICENSE)时注册,
            // 否则 NoLicense 构建里不应出现「授权设置」分类。
            new SystemSettingDefinition
            {
                Key = "license_warning_hours",
                Name = "授权到期提醒小时数",
                CategoryName = "授权设置",
                CategoryIcon = "ion:key-outline",
                CategorySort = 50,
                DisplayType = SystemSettingDisplayType.InputNumber,
                DefaultValue = "72",
                Sort = 1,
                CantDelete = true,
            },
#endif
            // 数据库备份分类
            // 注：云端中心地址 (CloudConfig.ServerUrl) 在 appsettings.json 里配，安装向导填写
            // 这里只放跟备份策略相关的运行时可调设置
            new SystemSettingDefinition
            {
                Key = "backup_auto_enabled",
                Name = "启用自动备份",
                CategoryName = "数据库备份",
                CategoryIcon = "ion:save-outline",
                CategorySort = 60,
                DisplayType = SystemSettingDisplayType.Switch,
                DefaultValue = "true",
                Sort = 0,
                CantDelete = true,
            },
            new SystemSettingDefinition
            {
                Key = "backup_interval_hours",
                Name = "自动备份间隔小时数",
                CategoryName = "数据库备份",
                CategoryIcon = "ion:save-outline",
                CategorySort = 60,
                DisplayType = SystemSettingDisplayType.InputNumber,
                DefaultValue = "12",
                Sort = 1,
                CantDelete = true,
            },
            new SystemSettingDefinition
            {
                Key = "backup_upload_enabled",
                Name = "启用云端备份上传",
                CategoryName = "数据库备份",
                CategoryIcon = "ion:save-outline",
                CategorySort = 60,
                DisplayType = SystemSettingDisplayType.Switch,
                DefaultValue = "false",
                Sort = 2,
                CantDelete = true,
            },
            new SystemSettingDefinition
            {
                Key = "backup_keep_local_count",
                Name = "本地保留备份份数",
                CategoryName = "数据库备份",
                CategoryIcon = "ion:save-outline",
                CategorySort = 60,
                DisplayType = SystemSettingDisplayType.InputNumber,
                DefaultValue = "14",
                Sort = 3,
                CantDelete = true,
            },
        };

        public List<MenuDefinition> GetMenus() => new()
        {
            // Dashboard
            new MenuDefinition
            {
                Name = "Dashboard", Path = "/dashboard", Component = "LAYOUT",
                Redirect = "/dashboard/analytics", Title = "仪表盘",
                Icon = "ion:grid-outline", OrderNo = 0,
                Children = new()
                {
                    new() { Name = "Analytics", Path = "/dashboard/analytics", Component = "/dashboard/analytics/index", Title = "分析页", Icon = "lucide:area-chart", OrderNo = 1 },
                    new() { Name = "Workspace", Path = "/dashboard/workspace", Component = "/dashboard/workspace/index", Title = "工作台", Icon = "carbon:workspace", OrderNo = 2 },
                }
            },
            // System
            new MenuDefinition
            {
                Name = "System", Path = "/system", Component = "LAYOUT",
                Title = "系统管理", Icon = "ion:settings-outline", OrderNo = 100,
                Children = new()
                {
                    new() { Name = "AdminManagement", Path = "/system/admin", Component = "/admin/List", Title = "管理员管理", Icon = "ion:people-outline", OrderNo = 1 },
                    new() { Name = "AdminEdit", Path = "/system/admin/edit", Component = "/admin/Form", Title = "编辑管理员", OrderNo = 1, HideInMenu = true },
                    new() { Name = "RoleManagement", Path = "/system/role", Component = "/admin/RoleList", Title = "角色管理", Icon = "ion:shield-outline", OrderNo = 2 },
                    new() { Name = "AdminRoleAdd", Path = "/system/role/edit", Component = "/admin/RoleForm", Title = "编辑角色", OrderNo = 2, HideInMenu = true },
                    new() { Name = "MenuManagement", Path = "/system/menu", Component = "/menu/List", Title = "菜单管理", Icon = "ion:menu-outline", OrderNo = 3 },
                    new() { Name = "MenuTree", Path = "/system/menu-tree", Component = "/menu/Tree", Title = "菜单树", Icon = "ion:git-branch-outline", OrderNo = 3, HideInMenu = true },
                    new() { Name = "SystemSetting", Path = "/system/setting", Component = "/system-setting/List", Title = "系统设置", Icon = "ion:construct-outline", OrderNo = 4 },
                    new() { Name = "SystemSettingEdit", Path = "/system/setting/edit", Component = "/system-setting/Form", Title = "编辑设置", OrderNo = 4, HideInMenu = true },
                    new() { Name = "DevTools", Path = "/system/devtools", Component = "", Title = "开发工具", Icon = "ion:code-slash-outline", OrderNo = 10, Children = new()
                    {
                        new() { Name = "BackgroundTask", Path = "/system/devtools/background-task", Component = "/background-task/List", Title = "后台任务", Icon = "ion:timer-outline", OrderNo = 1 },
                        new() { Name = "OpsTools", Path = "/system/devtools/ops", Component = "/ops-tools/index", Title = "运维工具", Icon = "ion:server-outline", OrderNo = 2 },
                        new() { Name = "LoginLog", Path = "/system/devtools/login-log", Component = "/login-log/List", Title = "登录日志", Icon = "ion:log-in-outline", OrderNo = 4 },
#if !DISABLE_LICENSE
                        // 授权信息菜单:仅启用授权验证时注册
                        new() { Name = "LicenseStatus", Path = "/system/devtools/license", Component = "/license/Status", Title = "授权信息", Icon = "ion:key-outline", OrderNo = 6 },
#endif
                        new() { Name = "Deploy", Path = "/system/devtools/deploy", Component = "/deploy/List", Title = "部署管理", Icon = "ion:cloud-upload-outline", OrderNo = 7 },
                    }},
                }
            },
        };

        #endregion

        IFreeSql _fsql;
        IHttpContextAccessor _httpContextAccessor;
        JwtToken _jwtToken;
        MenuService _menuService;
        ICacheService _cacheService;

        public AdminService(IFreeSql fsql, ILogger<AdminService> logger, IHttpContextAccessor httpContextAccessor, JwtToken jwtToken, MenuService menuService, ICacheService cacheService) : base(fsql, logger)
        {
            _fsql = fsql;
            _httpContextAccessor = httpContextAccessor;
            _jwtToken = jwtToken;
            _menuService = menuService;
            _cacheService = cacheService;
        }


        /// <summary>
        /// 登陆管理系统
        /// </summary>
        /// <param name="username">账号</param>
        /// <param name="password">密码</param>
        /// <param name="totpCode">谷歌验证码</param>
        public virtual async Task<Result> LoginAsync(string username, string password, string totpCode = "")
        {
            Admin admin = this.Select.Include(c => c.AdminRole).Where(c => c.Username == username).First();
            var ctx = _httpContextAccessor.HttpContext;

            if (admin == null)
            {
                return Result.Fail("账号密码错误！");
            }

            // 服务账号（仅供 API Key 使用）禁止密码登录 —— 纵深防御:即便其密码为随机不可匹配值，
            // 也直接以"账号密码错误"统一回应，不泄露账号存在性。
            if (admin.AccountType == AccountType.Service)
            {
                return Result.Fail("账号密码错误！");
            }

            if (!PasswordUtil.VerifyPassword(password, admin.Password))
            {
                // 记录失败登录日志
                _fsql.Insert(new LoginLog
                {
                    UserId = admin.Id,
                    UserType = 1,
                    Username = username,
                    IpAddress = ctx?.Connection?.RemoteIpAddress?.ToString(),
                    UserAgent = ctx?.Request?.Headers["User-Agent"].ToString(),
                    Status = 1,
                    CreateTime = AppTime.Now
                }).ExecuteAffrows();
                return Result.Fail("账号密码错误！");
            }

            if (admin.Status != AdminStatus.Active)
            {
                return Result.Fail("您的账号已被封锁！");
            }

            if (admin.AuthenticatorBindTime.HasValue)
            {
                if (string.IsNullOrEmpty(totpCode))
                {
                    return Result.Fail("请输入谷歌验证码", 2);
                }

                TwoFactorAuthenticator tfa = new TwoFactorAuthenticator();
                bool result = tfa.ValidateTwoFactorPIN(admin.AuthenticatorKey, totpCode, 1);
                if (!result)
                {
                    return Result.Fail("谷歌验证码错误！");
                }
            }


            admin.LastOnlineTime = AppTime.Now;

            UpdateDiy.UpdateColumns(c => c.LastOnlineTime).SetSource(admin).ExecuteAffrows();

            var claimList = new List<Claim>()
            {
                new Claim("Id",admin.Id.ToString() ) ,
                new Claim("Username",admin.Username ) ,
                new Claim("UserType","Admin" ) ,
                //new Claim("Role", admin.Role.RoleKey.ToString() )
            };
            ClaimsPrincipal principal = new ClaimsPrincipal();


            // 生成 token
            string accessToken = _jwtToken.CreateToken("JwtAdmin", claimList);

            _httpContextAccessor.HttpContext.Response.Headers["accessToken"] = accessToken;

            // 写入成功登录日志
            _fsql.Insert(new LoginLog
            {
                UserId = admin.Id,
                UserType = 1,
                Username = admin.Username,
                IpAddress = ctx?.Connection?.RemoteIpAddress?.ToString(),
                UserAgent = ctx?.Request?.Headers["User-Agent"].ToString(),
                Status = 0,
                CreateTime = AppTime.Now
            }).ExecuteAffrows();

            // 获取用户完整信息
            var userInfo = GetAdminInfo(admin);

            return Result.Success(new
            {
                accessToken,
                userInfo
            });
        }

        /// <summary>
        /// 退出管理系统
        /// </summary>
        public virtual Result Logout()
        {
            return Result.Success("退出系统成功！");
        }

        public virtual async Task<Result> GetInfoAsync()
        {
            int userId = _httpContextAccessor.HttpContext.User.Id();
            if (userId <= 0) return Result.Fail("授权失效!");

            Admin admin = Select.Include(c => c.AdminRole).Where(c => c.Id == userId).First();


            if (admin == null)
            {
                return Result.Fail("授权失效!");
            }

            string homePath = _menuService.GetDefaultMenupathByUserId(admin.Id);

            return Result.Success("获取管理员信息成功!", new
            {
                userId = admin.Id,
                admin.Id,
                admin.Username,
                RoleKey = admin.AdminRole?.RoleKey,
                RoleName = admin.AdminRole?.Name,
                Name = admin.Name,
                admin.Nickname,
                admin.Avatar,
                parentId = admin.ParentId,
                BindGoogle = admin.AuthenticatorBindTime.HasValue,
                admin.AuthenticatorBindTime,
                homePath = homePath
            });
        }

        /// <summary>
        /// 修改头像
        /// </summary>
        /// <param name="avatar">新头像URL</param>
        public virtual async Task<Result> ChangeAvatarAsync(string avatar)
        {
            if (string.IsNullOrEmpty(avatar))
            {
                return Result.Fail("头像地址不能为空!");
            }

            int userId = _httpContextAccessor.HttpContext.User.Id();
            if (userId <= 0) return Result.Fail("授权失效!");

            Admin admin = Select.Include(c => c.AdminRole).Where(c => c.Id == userId).First();

            if (admin == null)
            {
                return Result.Fail("授权失效!");
            }

            if (admin.Status != AdminStatus.Active)
            {
                return Result.Fail("授权失效!");
            }

            admin.Avatar = avatar;

            UpdateDiy.UpdateColumns(c => c.Avatar).SetSource(admin).ExecuteAffrows();

            return Result.Success("修改头像成功!", GetAdminInfo(admin));
        }

        /// <summary>
        /// 修改个人资料（姓名、昵称）
        /// </summary>
        public virtual async Task<Result> UpdateProfileAsync(string name, string nickname)
        {
            int userId = _httpContextAccessor.HttpContext.User.Id();
            if (userId <= 0) return Result.Fail("授权失效!");

            Admin admin = Select.Include(c => c.AdminRole).Where(c => c.Id == userId).First();

            if (admin == null)
            {
                return Result.Fail("授权失效!");
            }

            if (admin.Status != AdminStatus.Active)
            {
                return Result.Fail("授权失效!");
            }

            if (!string.IsNullOrEmpty(name) && name.Length > 50)
            {
                return Result.Fail("姓名长度不能超过50个字符!");
            }

            if (!string.IsNullOrEmpty(nickname) && nickname.Length > 50)
            {
                return Result.Fail("昵称长度不能超过50个字符!");
            }

            admin.Name = name;
            admin.Nickname = nickname;

            UpdateDiy.UpdateColumns(c => new { c.Name, c.Nickname }).SetSource(admin).ExecuteAffrows();

            return Result.Success("修改资料成功!", GetAdminInfo(admin));
        }

        /// <summary>
        /// 修改密码
        /// </summary>
        /// <param name="oldPassword">旧密码</param>
        /// <param name="newPassword">新密码</param>
        public virtual Result ChangePassword(string oldPassword, string newPassword)
        {
            if (string.IsNullOrEmpty(oldPassword))
            {
                return Result.Fail("数据异常,修改失败!");
            }

            if (newPassword.Length < 6)
            {
                return Result.Fail("请输入6位以上新密码!");
            }


            int userId = _httpContextAccessor.HttpContext.User.Id();
            if (userId <= 0) return Result.Fail("授权失效!");

            Admin admin = Select.Include(c => c.AdminRole).Where(c => c.Id == userId).First();

            if (admin == null)
            {
                return Result.Fail("授权失效!");
            }

            if (admin.Status != AdminStatus.Active)
            {
                return Result.Fail("授权失效!");
            }

            if (!PasswordUtil.VerifyPassword(oldPassword, admin.Password))
            {
                return Result.Fail("旧密码错误!");
            }

            admin.Password = PasswordUtil.EncryptPassword(newPassword);

            UpdateDiy.UpdateColumns(c => c.Password).SetSource(admin).ExecuteAffrows();


            return Result.Success("修改密码成功!", GetAdminInfo(admin));
        }

        /// <summary>
        /// 获取管理员信息
        /// </summary>
        /// <param name="admin">管理员数据实体</param>
        private object GetAdminInfo(Admin admin)
        {
            string homePath = _menuService.GetDefaultMenupathByUserId(admin.Id);
            return new
            {
                userId = admin.Id,
                admin.Id,
                admin.Username,
                RoleKey = admin.AdminRole?.RoleKey,
                RoleName = admin.AdminRole?.Name,
                Name = admin.Name,
                admin.Nickname,
                admin.Avatar,
                parentId = admin.ParentId,
                BindGoogle = admin.AuthenticatorBindTime.HasValue,
                admin.AuthenticatorBindTime,
                homePath = homePath,
            };
        }

        #region 谷歌验证器
        /// <summary>
        /// 获取验证器二维码
        /// </summary>
        public virtual Result GetGoogleCodeQR()
        {
            int adminId = _httpContextAccessor.HttpContext.User.Id();
            if (adminId <= 0) return Result.Fail("授权失效!");

            Admin admin = Select.Where(c => c.Id == adminId).First();

            if (admin == null)
            {
                return Result.Fail("管理员不存在！");
            }

            if (admin.AuthenticatorBindTime.HasValue)
            {
                return Result.Fail("管理员已绑定验证器，如果要重新绑定请先解除绑定！");
            }

            admin.AuthenticatorKey = Guid.NewGuid().ToString("N").ToUpper();

            this.UpdateDiy.UpdateColumns(c => c.AuthenticatorKey).SetSource(admin).ExecuteAffrows();

            TwoFactorAuthenticator tfa = new TwoFactorAuthenticator();
            SetupCode setupInfo = tfa.GenerateSetupCode(AppGlobal.AppSettings.ProductCn, admin.Username, admin.AuthenticatorKey, false, 3);

            string qrCodeImageUrl = setupInfo.QrCodeSetupImageUrl;
            string manualEntrySetupCode = setupInfo.ManualEntryKey;

            return Result.Success(new
            {
                QRBase64 = setupInfo.QrCodeSetupImageUrl,
                setupInfo.ManualEntryKey
            });
        }

        /// <summary>
        /// 绑定验证器
        /// </summary>
        public virtual Result BindGoogleCode(string code, string password)
        {
            int adminId = _httpContextAccessor.HttpContext.User.Id();

            Admin admin = Select.Where(c => c.Id == adminId).First();

            if (admin == null)
            {
                return Result.Fail("管理员不存在！");
            }

            // 验证当前密码
            if (!PasswordUtil.VerifyPassword(password, admin.Password))
            {
                return Result.Fail("当前密码错误！");
            }

            if (string.IsNullOrEmpty(admin.AuthenticatorKey))
            {
                return Result.Fail("管理员还没用生成绑定秘钥，请先获取绑定二维码！");
            }

            if (admin.AuthenticatorBindTime.HasValue)
            {
                return Result.Fail("管理员已绑定验证器，如果要重新绑定请先解除绑定！");
            }

            // verify
            TwoFactorAuthenticator tfa = new TwoFactorAuthenticator();
            bool result = tfa.ValidateTwoFactorPIN(admin.AuthenticatorKey, code, 1);

            if (result)
            {
                admin.AuthenticatorBindTime = AppTime.Now;
                this.UpdateDiy.UpdateColumns(c => c.AuthenticatorBindTime).SetSource(admin).ExecuteAffrows();
                return Result.Success("绑定成功！");
            }

            return Result.Fail("谷歌验证码密码错误！");
        }

        /// <summary>
        /// 获取绑定谷歌验证器信息
        /// </summary>
        public virtual Result GetGoogleCodeInfo()
        {
            int adminId = _httpContextAccessor.HttpContext.User.Id();
            Admin admin = Select.Where(c => c.Id == adminId).First();

            return Result.Success(new
            {
                admin.Username,
                admin.AuthenticatorBindTime
            });
        }

        /// <summary>
        /// 解绑谷歌验证器（管理员自行解绑，需验证 TOTP 和密码）
        /// </summary>
        public virtual Result UnbindGoogleCode(string totpCode, string password)
        {
            int adminId = _httpContextAccessor.HttpContext.User.Id();
            if (adminId <= 0) return Result.Fail("授权失效!");

            Admin admin = Select.Where(c => c.Id == adminId).First();
            if (admin == null)
            {
                return Result.Fail("管理员不存在！");
            }

            // 验证当前密码
            if (!PasswordUtil.VerifyPassword(password, admin.Password))
            {
                return Result.Fail("当前密码错误！");
            }

            if (!admin.AuthenticatorBindTime.HasValue)
            {
                return Result.Fail("当前未绑定谷歌验证器！");
            }

            if (string.IsNullOrEmpty(totpCode))
            {
                return Result.Fail("请输入谷歌验证码！");
            }

            TwoFactorAuthenticator tfa = new TwoFactorAuthenticator();
            bool result = tfa.ValidateTwoFactorPIN(admin.AuthenticatorKey, totpCode, 1);
            if (!result)
            {
                return Result.Fail("谷歌验证码错误！");
            }

            admin.AuthenticatorKey = null;
            admin.AuthenticatorBindTime = null;
            this.UpdateDiy.UpdateColumns(c => new { c.AuthenticatorKey, c.AuthenticatorBindTime }).SetSource(admin).ExecuteAffrows();

            return Result.Success("解绑谷歌验证器成功！");
        }

        /// <summary>
        /// 超级管理员重置他人谷歌验证器
        /// </summary>
        public virtual Result ResetGoogleCode(int targetAdminId)
        {
            int currentAdminId = _httpContextAccessor.HttpContext.User.Id();
            if (currentAdminId <= 0) return Result.Fail("授权失效!");

            Admin currentAdmin = Select.Include(c => c.AdminRole).Where(c => c.Id == currentAdminId).First();
            if (currentAdmin == null)
            {
                return Result.Fail("授权失效!");
            }

            if (currentAdmin.AdminRole?.RoleKey != "super")
            {
                return Result.Fail("仅超级管理员可执行此操作！");
            }

            if (targetAdminId == currentAdminId)
            {
                return Result.Fail("不能重置自己的谷歌验证器，请使用解绑功能！");
            }

            Admin targetAdmin = Select.Where(c => c.Id == targetAdminId).First();
            if (targetAdmin == null)
            {
                return Result.Fail("目标管理员不存在！");
            }

            if (!targetAdmin.AuthenticatorBindTime.HasValue)
            {
                return Result.Fail("目标管理员未绑定谷歌验证器！");
            }

            targetAdmin.AuthenticatorKey = null;
            targetAdmin.AuthenticatorBindTime = null;
            this.UpdateDiy.UpdateColumns(c => new { c.AuthenticatorKey, c.AuthenticatorBindTime }).SetSource(targetAdmin).ExecuteAffrows();

            return Result.Success("重置谷歌验证器成功！");
        }

        #endregion

        #region CURD

        /// <summary>
        /// 获取管理员列表
        /// </summary>
        public virtual Result GetList(string username = "", string name = "", int? roleId = null, string field = "", string order = "", int page = 1, int pageSize = 999)
        {
            var query = Select
.Include(c => c.AdminRole);

            query = query.WhereIf(!string.IsNullOrEmpty(username), c => c.Username == username);
            query = query.WhereIf(!string.IsNullOrEmpty(name), c => c.Name == name);
            query = query.WhereIf(roleId.HasValue, c => c.RoleId == roleId.Value);

            if (string.IsNullOrEmpty(field))
            {
                query = query.OrderByDescending(c => c.Id);
            }
            else
            {
                query = query.OrderByPropertyName(field, order == "ascend");
            }

            var data = query.Count(out long dataCount).Page(page, pageSize).ToList(
            c => new
            {
                c.Id,
                c.Username,
                c.Name,
                c.Avatar,
                c.RoleId,
                Role = c.AdminRole.Name,
                c.Status,
                c.CreatedTime,
                c.LastOnlineTime,
                c.AuthenticatorKey,
                c.AuthenticatorBindTime,
            }
            );

            return Result.Page(data, dataCount);
        }

        /// <summary>
        /// 创建管理员
        /// </summary>
        public virtual Result Create(Admin admin)
        {
            if (Select.Where(c => c.Username == admin.Username).Any())
            {
                return Result.Fail("用户名已存在!");
            }

            admin.CreatedTime = AppTime.Now;
            admin.LastOnlineTime = null;
            admin = this.Insert(admin);

            return Result.Success("管理员添加成功!", admin);
        }

        /// <summary>
        /// 更新管理员
        /// </summary>
        public virtual Result Edit(Admin admin)
        {
            if (admin.Id == 1 && admin.Username != "admin")
            {
                return Result.Fail("超级管理员用户名不允许修改!");
            }

            int count = this.UpdateDiy.UpdateColumns(c => new
            {
                c.Username,
                c.Password,
                c.Name,
                c.Avatar,
                c.RoleId,
                c.Status,
            }).

            SetSource(admin).ExecuteAffrows();

            if (count <= 0)
            {
                return Result.Fail("管理员更新失败!");
            }

            return Result.Success("管理员更新成功!", admin);
        }

        /// <summary>
        /// 删除管理员 — override 基类，需要事务 + 业务规则
        /// </summary>
        public override Result Del(int id)
        {
            if (id == 1)
            {
                return Result.Fail("超级管理员不允许删除!");
            }

            try
            {
                return this.ExecuteInTransaction<Admin, Result>(uow =>
                {
                    Admin admin = this.Select.Where(c => c.Id == id).ForUpdate().First();

                    if (admin == null)
                    {
                        return Result.Fail("管理员不存在,删除失败!");
                    }

                    admin.IsDeleted = true;
                    int count = this.UpdateDiy.UpdateColumns(c => new
                    {
                        c.IsDeleted
                    }).SetSource(admin).ExecuteAffrows();

                    if (count <= 0)
                    {
                        throw new Exception("管理员伪删除更新数据为0条,逻辑异常!");
                    }

                    return Result.Success("删除管理员成功!");
                }, _logger);
            }
            catch (Exception)
            {
                return Result.Fail("删除管理员失败，数据操作异常!");
            }
        }

        /// <summary>
        /// 查询管理员详情 — override 基类，需要 Include AdminRole
        /// </summary>
        public override Result Detail(int id)
        {
            var query = this.Select.Include(c => c.AdminRole);
            Admin admin = query.Where(c => c.Id == id).First();

            if (admin == null)
            {
                return Result.Fail("查询的管理员不存在,请确认操作是否正确!");
            }

            return Result.Success(admin);
        }

        /// <summary>
        /// 改变管理员显示状态
        /// </summary>
        public virtual Result ChangeShow(int id)
        {
            Admin admin = this.Select.Where(c => c.Id == id).First();

            if (admin == null)
            {
                return Result.Fail("查询的管理员不存在,请确认操作是否正确!");
            }

            return Result.Success(admin);
        }

        /// <summary>
        /// 获取当前登录的管理员的权限菜单
        /// 优化:
        /// 1. 从 3 次查询 (AdminRoleMenu + Menu + ParentMenu)合并为 2 次查询
        /// 2. 加入缓存，用户权限菜单 30 分钟过期
        /// 3. 角色-菜单变更时自动清除缓存
        /// </summary>
        public virtual object MenuAll()
        {
            // 获取当前登录用户
            var currentUser = GetCurrentAdmin();
            if (currentUser == null)
            {
                return Result.Fail("未登录或登录已过期");
            }

            int roleId = currentUser.RoleId;
            string cacheKey = $"menu:user:{currentUser.Id}:role:{roleId}";

            // 优化: 检查缓存 (TTL: 30 分钟)
            var cachedMenus = _cacheService.Get<List<VbenMenu>>(cacheKey);
            if (cachedMenus != null)
            {
                _logger.LogDebug($"用户 {currentUser.Id} 的权限菜单从缓存读取");
                return Result.Success(cachedMenus);
            }

            // 缓存未命中，从数据库查询
            var allMenus = _fsql.Select<AdminRoleMenu>()
                .Where(jm => jm.RoleId == roleId)
                .ToList(jm => jm.MenuId);

            if (!allMenus.Any())
            {
                // 空菜单也缓存
                var emptyMenus = new List<VbenMenu>();
                _cacheService.Set(cacheKey, emptyMenus, TimeSpan.FromMinutes(30));
                return Result.Success(emptyMenus);
            }

            // 获取所有授权菜单及其父菜单（单次查询）
            var menus = _fsql.Select<Menu>()
                .Where(m => allMenus.Contains(m.Id))
                .ToList();

            // 递归获取所有祖先菜单
            var allMenusWithParents = new Dictionary<int, Menu>();
            foreach (var m in menus) allMenusWithParents[m.Id] = m;

            var idsToCheck = menus.Where(m => m.ParentId > 0).Select(m => m.ParentId).Distinct().ToList();
            while (idsToCheck.Count > 0)
            {
                var missingIds = idsToCheck.Where(id => !allMenusWithParents.ContainsKey(id)).ToList();
                if (missingIds.Count == 0) break;
                var ancestors = _fsql.Select<Menu>().Where(m => missingIds.Contains(m.Id)).ToList();
                foreach (var a in ancestors) allMenusWithParents[a.Id] = a;
                idsToCheck = ancestors.Where(m => m.ParentId > 0).Select(m => m.ParentId).Distinct().ToList();
            }

            var allMenuList = allMenusWithParents.Values.OrderBy(m => m.OrderNo).ToList();

            // 递归构建树形结构
            var menuList = allMenuList
                .Where(m => m.ParentId == 0)
                .OrderBy(m => m.OrderNo)
                .Select(m => new VbenMenu(m, allMenuList))
                .ToList();

            // 将结果放入缓存 (TTL: 30 分钟)
            _cacheService.Set(cacheKey, menuList, TimeSpan.FromMinutes(30));
            _logger.LogInformation($"用户 {currentUser.Id} 的权限菜单已加载到缓存");

            return Result.Success(menuList);
        }
        #endregion

        #region 管理员管理（权限由 Role 控制）

        /// <summary>
        /// 获取当前登录的管理员信息
        /// </summary>
        private Admin GetCurrentAdmin()
        {
            int currentId = _httpContextAccessor.HttpContext.User.Id();
            if (currentId <= 0) return null;
            return Select.Where(c => c.Id == currentId).First();
        }

        /// <summary>
        /// 获取管理员列表（权限由 Role 控制）
        /// </summary>
        public virtual Result GetAdminList(
            string username = "",
            string name = "",
            int? roleId = null,
            AdminStatus? status = null,
            string field = "",
            string order = "",
            int page = 1,
            int pageSize = 20)
        {
            var query = Select.Include(c => c.AdminRole);

            query = query.WhereIf(!string.IsNullOrEmpty(username), c => c.Username.Contains(username));
            query = query.WhereIf(!string.IsNullOrEmpty(name), c => c.Name.Contains(name));
            query = query.WhereIf(roleId.HasValue, c => c.RoleId == roleId.Value);
            query = query.WhereIf(status.HasValue, c => c.Status == status.Value);

            if (string.IsNullOrEmpty(field))
            {
                query = query.OrderByDescending(c => c.Id);
            }
            else
            {
                query = query.OrderByPropertyName(field, order == "ascend");
            }

            var data = query.Count(out long dataCount).Page(page, pageSize).ToList(
                c => new
                {
                    c.Id,
                    c.ParentId,
                    c.Username,
                    c.Name,
                    c.Avatar,
                    c.RoleId,
                    Role = c.AdminRole.Name,
                    c.Status,
                    c.CreatedTime,
                    c.LastOnlineTime
                }
            );

            return Result.Page(data, dataCount);
        }

        /// <summary>
        /// 创建管理员（权限由 Role 控制）
        /// </summary>
        public virtual Result CreateAdmin(Admin admin)
        {
            var currentUser = GetCurrentAdmin();
            if (currentUser == null)
            {
                return Result.Fail("授权失效!");
            }

            if (string.IsNullOrEmpty(admin.Username))
            {
                return Result.Fail("用户名不能为空!");
            }

            if (string.IsNullOrEmpty(admin.Password))
            {
                return Result.Fail("密码不能为空!");
            }

            if (admin.Password.Length < 6)
            {
                return Result.Fail("密码长度不能少于6位!");
            }

            if (Select.Where(c => c.Username == admin.Username).Any())
            {
                return Result.Fail("用户名已存在!");
            }

            admin.CreatedTime = AppTime.Now;
            admin.LastOnlineTime = null;
            admin.IsDeleted = false;
            admin = this.Insert(admin);

            return Result.Success("管理员添加成功!", new
            {
                admin.Id,
                admin.ParentId,
                admin.Username,
                admin.Name,
                admin.Avatar,
                admin.RoleId,
                admin.Status,
                admin.CreatedTime
            });
        }

        /// <summary>
        /// 编辑管理员（权限由 Role 控制）
        /// </summary>
        public virtual Result EditAdmin(Admin admin)
        {
            var currentUser = GetCurrentAdmin();
            if (currentUser == null)
            {
                return Result.Fail("授权失效!");
            }

            // 查询要编辑的管理员
            var targetUser = Select.Where(c => c.Id == admin.Id).First();
            if (targetUser == null)
            {
                return Result.Fail("管理员不存在!");
            }

            // 不能编辑超级管理员
            if (targetUser.Id == 1)
            {
                return Result.Fail("超级管理员不允许修改!");
            }

            admin.ParentId = targetUser.ParentId;

            if (string.IsNullOrEmpty(admin.Username))
            {
                return Result.Fail("用户名不能为空!");
            }

            // 检查用户名是否重复（排除自己）
            if (Select.Where(c => c.Username == admin.Username && c.Id != admin.Id).Any())
            {
                return Result.Fail("用户名已存在!");
            }

            // 构建更新字段
            var updateDiy = UpdateDiy.SetSource(admin);
            updateDiy = UpdateDiy.UpdateColumns(c => new
            {
                c.Username,
                c.Name,
                c.Avatar,
                c.RoleId,
                c.ParentId,
                c.Status,
            }).SetSource(admin);

            // 如果提供了新密码则更新
            if (!string.IsNullOrEmpty(admin.Password) && admin.Password.Length >= 6)
            {
                // 加密密码
                admin.Password = PasswordUtil.EncryptPassword(admin.Password);
                updateDiy = UpdateDiy.UpdateColumns(c => new
                {
                    c.Username,
                    c.Password,
                    c.Name,
                    c.Avatar,
                    c.RoleId,
                    c.ParentId,
                    c.Status,
                }).SetSource(admin);
            }

            int count = updateDiy.ExecuteAffrows();

            if (count <= 0)
            {
                return Result.Fail("管理员更新失败!");
            }

            return Result.Success("管理员更新成功!");
        }

        /// <summary>
        /// 删除管理员（权限由 Role 控制）
        /// </summary>
        public virtual Result DelAdmin(int id)
        {
            var currentUser = GetCurrentAdmin();
            if (currentUser == null)
            {
                return Result.Fail("授权失效!");
            }

            // 不能删除超级管理员
            if (id == 1)
            {
                return Result.Fail("超级管理员不允许删除!");
            }

            // 不能删除自己
            if (id == currentUser.Id)
            {
                return Result.Fail("不能删除自己!");
            }

            try
            {
                return this.ExecuteInTransaction<Admin, Result>(uow =>
                {
                    Admin targetUser = this.Select.Where(c => c.Id == id).ForUpdate().First();

                    if (targetUser == null)
                    {
                        return Result.Fail("管理员不存在,删除失败!");
                    }

                    targetUser.IsDeleted = true;
                    int count = this.UpdateDiy.UpdateColumns(c => new
                    {
                        c.IsDeleted
                    }).SetSource(targetUser).ExecuteAffrows();

                    if (count <= 0)
                    {
                        throw new Exception("管理员伪删除更新数据为0条,逻辑异常!");
                    }

                    return Result.Success("删除管理员成功!");
                }, _logger);
            }
            catch (Exception)
            {
                return Result.Fail("删除管理员失败，数据操作异常!");
            }
        }

        /// <summary>
        /// 获取管理员详情（权限由 Role 控制）
        /// </summary>
        public virtual Result GetAdminDetail(int id)
        {
            var currentUser = GetCurrentAdmin();
            if (currentUser == null)
            {
                return Result.Fail("授权失效!");
            }

            var query = this.Select.Include(c => c.AdminRole);
            Admin admin = query.Where(c => c.Id == id).First();

            if (admin == null)
            {
                return Result.Fail("管理员不存在!");
            }

            return Result.Success(new
            {
                admin.Id,
                admin.ParentId,
                admin.Username,
                admin.Name,
                admin.Avatar,
                admin.RoleId,
                Role = admin.AdminRole?.Name,
                admin.Status,
                admin.CreatedTime,
                admin.LastOnlineTime
            });
        }

        /// <summary>
        /// 重置管理员密码（权限由 Role 控制）
        /// </summary>
        public virtual Result ResetAdminPassword(int id, string newPassword)
        {
            var currentUser = GetCurrentAdmin();
            if (currentUser == null)
            {
                return Result.Fail("授权失效!");
            }

            if (string.IsNullOrEmpty(newPassword) || newPassword.Length < 6)
            {
                return Result.Fail("新密码长度不能少于6位!");
            }

            // 不能重置超级管理员密码（除非自己是超级管理员）
            if (id == 1 && currentUser.Id != 1)
            {
                return Result.Fail("无权限重置超级管理员密码!");
            }

            var targetUser = Select.Where(c => c.Id == id).First();
            if (targetUser == null)
            {
                return Result.Fail("管理员不存在!");
            }

            targetUser.Password = PasswordUtil.EncryptPassword(newPassword);
            int count = UpdateDiy.UpdateColumns(c => c.Password).SetSource(targetUser).ExecuteAffrows();

            if (count <= 0)
            {
                return Result.Fail("密码重置失败!");
            }

            return Result.Success("密码重置成功!");
        }

        /// <summary>
        /// 切换管理员状态（权限由 Role 控制）
        /// </summary>
        public virtual Result ToggleAdminStatus(int id)
        {
            var currentUser = GetCurrentAdmin();
            if (currentUser == null)
            {
                return Result.Fail("授权失效!");
            }

            // 不能操作超级管理员
            if (id == 1)
            {
                return Result.Fail("超级管理员状态不允许修改!");
            }

            // 不能修改自己的状态
            if (id == currentUser.Id)
            {
                return Result.Fail("不能修改自己的状态!");
            }

            var targetUser = Select.Where(c => c.Id == id).First();
            if (targetUser == null)
            {
                return Result.Fail("管理员不存在!");
            }

            targetUser.Status = targetUser.Status == AdminStatus.Active
                ? AdminStatus.Disabled
                : AdminStatus.Active;

            int count = UpdateDiy.UpdateColumns(c => c.Status).SetSource(targetUser).ExecuteAffrows();

            if (count <= 0)
            {
                return Result.Fail("状态切换失败!");
            }

            return Result.Success("状态切换成功!", new { targetUser.Status });
        }

        #endregion
    }
}
