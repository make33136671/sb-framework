using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;
using SB.Framework.Models;
using SB.Framework.Services;

namespace SB.Framework.WebApi.Controllers
{
    [ApiController, Authorize(AuthenticationSchemes = "JwtAdmin"), Route("[controller]/[action]")]
    public class SystemSettingCategoryController : Controller
    {
        private readonly ILogger<SystemSettingCategoryController> _logger;
        private readonly SystemSettingCategoryService _systemSettingCategoryService;

        public SystemSettingCategoryController(ILogger<SystemSettingCategoryController> logger,
            SystemSettingCategoryService systemSettingCategoryService)
        {
            _logger = logger;
            _systemSettingCategoryService = systemSettingCategoryService;
        }

        #region CURD
        /// <summary>
        /// 设置分类列表
        /// </summary>
        [HttpGet]
        public Result List(string key = "", string name = "", string field = "", string order = "", int page = 1, int pageSize = 10)
        {
            return _systemSettingCategoryService.GetList(key, name, field, order, page, pageSize);
        }

        /// <summary>
        /// 查询设置分类
        /// </summary>
        [HttpGet]
        public Result QuerySystemSettingCategory(string key) => _systemSettingCategoryService.QuerySystemSettingCategory(key);

        /// <summary>
        /// 添加设置分类
        /// </summary>
        [HttpPost]
        public Result Add([FromForm] SystemSettingCategory systemSettingCategory)
        {
            return _systemSettingCategoryService.Create(systemSettingCategory);
        }

        /// <summary>
        /// 更新设置分类
        /// </summary>
        [HttpPost]
        public Result Edit([FromForm] SystemSettingCategory systemSettingCategory)
        {
            return _systemSettingCategoryService.Edit(systemSettingCategory);
        }

        /// <summary>
        /// 删除设置分类
        /// </summary>
        [HttpPost]
        public Result Del([FromForm] int id)
        {
            return _systemSettingCategoryService.Del(id);
        }

        /// <summary>
        /// 设置分类详细信息
        /// </summary>
        [HttpGet]
        public Result Detail(int id)
        {
            return _systemSettingCategoryService.Detail(id);
        }
        #endregion
    }
}