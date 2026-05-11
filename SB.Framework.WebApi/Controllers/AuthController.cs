using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using SB.Framework.Services;
using SB.Framework.Models;
using SB.Framework.Core.Expand;
using Microsoft.AspNetCore.Http;


namespace SB.Framework.WebApi.Controllers;

/// <summary>
/// 认证与账户管理
/// </summary>
[ApiController,
    Authorize(AuthenticationSchemes = "JwtAdmin"),
    Route("[controller]/[action]")]
public class AuthController : Controller
{
    private readonly ILogger<AuthController> _logger;
    public IHttpContextAccessor _httpContextAccessor;
    public AdminService _adminService;
    public FileUploadService _fileUploadService;
    public AuthController(ILogger<AuthController> logger, IHttpContextAccessor httpContextAccessor, AdminService adminService, FileUploadService fileUploadService)
    {
        _logger = logger;
        _httpContextAccessor = httpContextAccessor;
        _adminService = adminService;
        _fileUploadService = fileUploadService;
    }


    #region 身份认证
    /// <summary>
    /// 登录管理系统
    /// </summary>
    /// <param name="yonghuming">用户名</param>
    /// <param name="mima">密码</param>
    /// 取消身份验证过滤
    [HttpPost(Name = "Login"), AllowAnonymous, EnableRateLimiting("login")]
    public async Task<Result> Login([FromForm] string username, [FromForm] string password, [FromForm] string totpCode) => await _adminService.LoginAsync(username, password, totpCode);


    /// <summary>
    /// 获取当前登录的管理员信息
    /// </summary>
    [HttpGet(Name = "GetInfo")]
    public async Task<Result> GetInfo() => await _adminService.GetInfoAsync();

    /// <summary>
    /// 退出管理系统
    /// </summary>
    [HttpPost, AllowAnonymous]
    public IActionResult Logout()
    {
        return Content("2");
    }

    /// <summary>
    /// 修改密码
    /// </summary>
    /// <param name="oldPassword">旧密码</param>
    /// <param name="newPassword">新密码</param>
    [HttpPost]
    public Result ChangePassword([FromForm] string oldPassword, [FromForm] string newPassword) => _adminService.ChangePassword(oldPassword, newPassword);

    /// <summary>
    /// 修改头像
    /// </summary>
    /// <param name="avatar">新头像URL</param>
    [HttpPost]
    public async Task<Result> ChangeAvatar([FromForm] string avatar) => await _adminService.ChangeAvatarAsync(avatar);

    /// <summary>
    /// 修改个人资料
    /// </summary>
    [HttpPost]
    public async Task<Result> UpdateProfile([FromForm] string name, [FromForm] string nickname) => await _adminService.UpdateProfileAsync(name, nickname);
    #endregion

    #region 文件上传

    /// <summary>
    /// 上传文件
    /// </summary>
    [HttpPost]
    [AllowAnonymous]
    public Result Upload(IFormFile file) => _fileUploadService.Upload(file);

    #endregion

    #region 谷歌验证器

    /// <summary>
    /// 获取绑定谷歌验证器信息
    /// </summary>
    [HttpGet]
    public Result GetGoogleCodeInfo() => _adminService.GetGoogleCodeInfo();

    /// <summary>
    /// 获取验证器二维码
    /// </summary>
    [HttpGet]
    public Result GetGoogleCodeQR() => _adminService.GetGoogleCodeQR();

    /// <summary>
    /// 绑定验证器
    /// </summary>
    [HttpPost]
    public Result PostBindGoogleCode([FromForm] string code, [FromForm] string password) => _adminService.BindGoogleCode(code, password);

    /// <summary>
    /// 解绑谷歌验证器
    /// </summary>
    [HttpPost]
    public Result UnbindGoogleCode([FromForm] string totpCode, [FromForm] string password) => _adminService.UnbindGoogleCode(totpCode, password);

    #endregion

}

