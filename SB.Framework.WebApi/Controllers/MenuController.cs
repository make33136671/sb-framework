using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using SB.Framework.Services;
using SB.Framework.Models;
using Microsoft.AspNetCore.Http;


namespace SB.Framework.WebApi.Controllers;

/// <summary>
/// 菜单管理
/// </summary>
[ApiController,
    Authorize(AuthenticationSchemes = "JwtAdmin"),
    Route("[controller]/[action]")]
public class MenuController : Controller
{
    private readonly ILogger<MenuController> _logger;
    public IHttpContextAccessor _httpContextAccessor;
    public AdminService _adminService;
    private readonly MenuService _menuService;
    public MenuController(ILogger<MenuController> logger, IHttpContextAccessor httpContextAccessor, AdminService adminService, MenuService menuService)
    {
        _logger = logger;
        _httpContextAccessor = httpContextAccessor;
        _adminService = adminService;
        _menuService = menuService;
    }


    #region 身份认证
    /// <summary>
    /// 获取当前登录的管理员的权限菜单
    /// </summary>
    [HttpGet(Name = "All")]
    public object All() => _adminService.MenuAll();


    #endregion


    #region CURD
    /// <summary>
    /// 更新菜单属性
    /// </summary>
    [HttpPost]
    public Result UpdateMenu([FromForm] int id, [FromForm] string field, [FromForm] string value) => _menuService.UpdateMenu(id, field, value);

    /// <summary>
    /// 权限菜单列表
    /// </summary>
    [HttpGet]
    public Result TreeList(string name = "", string title = "", string field = "", string order = "", int page = 1, int pageSize = 10)
    {
        return _menuService.GetTreeList(name, title, field, order, page, pageSize);
    }

    /// <summary>
    /// 权限菜单列表
    /// </summary>
    [HttpGet]
    public Result List(string name = "", string title = "", string field = "", string order = "", int page = 1, int pageSize = 10)
    {
        return _menuService.GetList(name, title, field, order, page, pageSize);
    }

    /// <summary>
    /// 查询权限菜单
    /// </summary>
    [HttpGet]
    public Result QueryMenu(string key) => _menuService.QueryMenu(key);

    /// <summary>
    /// 添加权限菜单
    /// </summary>
    [HttpPost]
    public Result Add([FromForm] Menu menu)
    {
        return _menuService.Create(menu);
    }

    /// <summary>
    /// 更新权限菜单
    /// </summary>
    [HttpPost]
    public Result Edit([FromForm] Menu menu)
    {
        return _menuService.Edit(menu);
    }

    /// <summary>
    /// 删除权限菜单
    /// </summary>
    [HttpPost]
    public Result Del([FromForm] int id)
    {
        return _menuService.Del(id);
    }

    /// <summary>
    /// 权限菜单详细信息
    /// </summary>
    [HttpGet]
    public Result Detail(int id)
    {
        return _menuService.Detail(id);
    }
    #endregion


}

