using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SB.Framework.Models;
using SB.Framework.Services;

namespace SB.Framework.WebApi.Controllers
{
    [ApiController, Authorize(AuthenticationSchemes = "JwtAdmin"), Route("[controller]/[action]")]
    public class SystemSettingController : Controller
    {
        private readonly ILogger<SystemSettingController> _logger;
        private readonly SystemSettingService _systemSettingService;

        public SystemSettingController(ILogger<SystemSettingController> logger,
            SystemSettingService systemSettingService)
        {
            _logger = logger;
            _systemSettingService = systemSettingService;
        }

        /// <summary>
        /// 获取系统设置列表
        /// </summary>
        /// <param name="key">搜索关键词</param>
        /// <param name="field">排序字段</param>
        /// <param name="order">排序</param>
        /// <param name="page">页码</param>
        /// <param name="pageSize">单页系统设置数量</param>
        /// <returns>系统设置分页对象</returns>
        [HttpGet]
        public Result List(string key = "", string name = "", string field = "", string order = "", int page = 1, int pageSize = 10)
        {
            return _systemSettingService.GetList(key, name, field, order, page, pageSize);
        }

        /// <summary>
        /// 系统设置列表
        /// </summary>

        [HttpGet(Name = "SystemSettingCategoryWithSettingList")]
        public Result GetCategoryWithSettingList(int? categoryId = null, string field = "", string order = "", int page = 1, int pageSize = 10)
        {
            return _systemSettingService.GetCategoryWithSettingList(categoryId, field, order, page, pageSize);
        }

        /// <summary>
        /// 查询系统设置
        /// </summary>
        [HttpGet(Name = "SystemSettingQuerySystemSetting")]
        public Result QuerySystemSetting(string key) => _systemSettingService.QuerySystemSetting(key);

        /// <summary>
        /// 按 Key 精确查询单个系统设置项（前端按 key 取配置值用）
        /// </summary>
        [HttpGet]
        public Result GetByKey(string key)
        {
            if (string.IsNullOrWhiteSpace(key)) return Result.Fail("key 不能为空");
            var setting = _systemSettingService.Select.Where(c => c.Key == key.Trim()).First();
            if (setting == null) return Result.Fail("设置不存在");
            return Result.Success(setting);
        }

        /// <summary>
        /// 添加系统设置
        /// </summary>
        [HttpPost(Name = "SystemSettingAdd")]
        public Result Add([FromForm] SystemSetting systemSetting)
        {
            return _systemSettingService.Create(systemSetting);
        }

        /// <summary>
        /// 更新系统设置
        /// </summary>
        [HttpPost]
        public Result Edit([FromForm] SystemSetting systemSetting)
        {
            return _systemSettingService.Edit(systemSetting);
        }

        /// <summary>
        /// 批量保存系统设置
        /// </summary>
        [HttpPost(Name = "SystemSettingSaveList")]
        public Result SaveList([FromForm] string settings)
        {
            if (string.IsNullOrWhiteSpace(settings))
                return Result.Fail("数据内容为空,保存失败!");

            var jsonOptions = new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            jsonOptions.Converters.Add(new LenientStringConverter());
            // 必须与全局序列化配置（SbFrameworkExtensions.AddJsonOptions 的 JsonStringEnumConverter）对称：
            // 读取端点把枚举序列化成字符串（displayType:"InputNumber"），前端整行回传时
            // 若这里按数字解析枚举 → JsonException → 500「服务器内部错误」，系统设置永远存不上。
            // 同时容忍数字与字符串两种写法（旧前端/手工构造的载荷）。
            jsonOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
            List<SystemSetting> systemSettingList = System.Text.Json.JsonSerializer.Deserialize<List<SystemSetting>>(settings, jsonOptions)!;

            return _systemSettingService.SaveList(systemSettingList);
        }

        /// <summary>
        /// 删除系统设置
        /// </summary>
        [HttpPost(Name = "SystemSettingDel")]
        public Result Del([FromForm] int id)
        {
            return _systemSettingService.Del(id);
        }

        /// <summary>
        /// 系统设置详细信息
        /// </summary>
        [HttpGet(Name = "SystemSettingDetail")]
        public Result Detail(int id)
        {
            return _systemSettingService.Detail(id);
        }
    }

    /// <summary>
    /// 宽松字符串转换器：允许 JSON number/boolean 反序列化为 string 字段
    /// </summary>
    public class LenientStringConverter : System.Text.Json.Serialization.JsonConverter<string>
    {
        public override string? Read(ref System.Text.Json.Utf8JsonReader reader, Type typeToConvert, System.Text.Json.JsonSerializerOptions options)
        {
            return reader.TokenType switch
            {
                System.Text.Json.JsonTokenType.String => reader.GetString(),
                System.Text.Json.JsonTokenType.Number => reader.TryGetInt64(out var l) ? l.ToString() : reader.GetDouble().ToString(),
                System.Text.Json.JsonTokenType.True => "true",
                System.Text.Json.JsonTokenType.False => "false",
                System.Text.Json.JsonTokenType.Null => null,
                _ => null,
            };
        }

        public override void Write(System.Text.Json.Utf8JsonWriter writer, string value, System.Text.Json.JsonSerializerOptions options)
            => writer.WriteStringValue(value);
    }
}