using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using SB.Framework.Services;
using SB.Framework.Models;
using SB.Framework.Core.Expand;


namespace SB.Framework.WebApi.Controllers;

/// <summary>
/// API 公开接口
/// </summary>
[ApiController,
    Authorize(AuthenticationSchemes = "JwtApi"),
    Route("/api/[action]")]
public class ApiController : Controller
{
    private readonly ILogger<ApiController> _logger;
    public IHttpContextAccessor _httpContextAccessor;
    public CommonService _commonService;
    public AppService _appService;
    public FileUploadService _fileUploadService;
    public ApiAuthService _authService;
    public IVerificationCodeService _verificationCodeService;

    public ApiController(ILogger<ApiController> logger,
        IHttpContextAccessor httpContextAccessor,
        AdminService adminService,
        CommonService commonService,
        AppService appService,
        FileUploadService fileUploadService,
        ApiAuthService authService,
        IVerificationCodeService verificationCodeService)
    {
        _logger = logger;
        _httpContextAccessor = httpContextAccessor;
        _commonService = commonService;
        _appService = appService;
        _fileUploadService = fileUploadService;
        _authService = authService;
        _verificationCodeService = verificationCodeService;
    }

    #region 认证

    [HttpPost, AllowAnonymous]
    public Result AuthLogin([FromForm] string username, [FromForm] string password)
        => _authService.Login(username, password);

    [HttpPost, AllowAnonymous]
    public Result AuthRegister([FromForm] string username, [FromForm] string password,
        [FromForm] string name, [FromForm] string? inviteCode = null)
        => _authService.Register(username, password, name, inviteCode);

    [HttpPost, AllowAnonymous]
    public Result AuthForgotPassword([FromForm] string username)
        => _authService.ForgotPassword(username);

    [HttpGet]
    public Result AuthInfo() => _authService.GetInfo();

    [HttpPost]
    public Result AuthUpdateNickname([FromForm] string name)
        => _authService.UpdateNickname(name);

    [HttpPost]
    public Result AuthUpdateAvatar([FromForm] string avatar)
        => _authService.UpdateAvatar(avatar);

    [HttpPost]
    public Result AuthLogout() => Result.Success("退出成功");

    [HttpPost]
    public Result AuthSubmitRealName([FromForm] string realName, [FromForm] string idNumber,
        [FromForm] string idFrontImage, [FromForm] string idBackImage)
        => _authService.SubmitRealName(realName, idNumber, idFrontImage, idBackImage);

    [HttpGet]
    public Result AuthRealNameInfo() => _authService.GetRealNameInfo();

    #endregion

    #region 安全

    [HttpPost]
    public Result SecurityChangeLoginPassword([FromForm] string oldPassword, [FromForm] string newPassword)
        => _authService.ChangeLoginPassword(oldPassword, newPassword);

    [HttpPost]
    public Result SecurityChangePaymentPassword([FromForm] string oldPassword, [FromForm] string newPassword)
        => _authService.ChangePaymentPassword(oldPassword, newPassword);

    #endregion

    #region 验证码

    [HttpPost, AllowAnonymous]
    public async Task<Result> SendVerificationCode([FromForm] string email, [FromForm] VerificationCodeType type)
        => await _verificationCodeService.SendVerificationCodeAsync(email, type);

    [HttpPost, AllowAnonymous]
    public async Task<Result> VerifyCode([FromForm] string email, [FromForm] string code, [FromForm] VerificationCodeType type)
        => await _verificationCodeService.VerifyCodeAsync(email, code, type);

    #endregion

    #region 文件上传

    [HttpPost]
    [AllowAnonymous]
    public Result Upload(IFormFile file) => _fileUploadService.Upload(file);

    #endregion
}
