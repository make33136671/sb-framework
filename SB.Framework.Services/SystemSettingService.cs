using FreeSql;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using SB.Framework.Core.Attribute;
using SB.Framework.Models;

namespace SB.Framework.Services
{
    [AutoDependencyInjection]
    public class SystemSettingService : BaseCrudService<SystemSetting>, IModuleInitializer
    {
        #region IModuleInitializer

        public string ModuleName => "系统设置";
        public int InitOrder => 10;

        public Type[] GetEntityTypes() => new[]
        {
            typeof(SystemSetting), typeof(SystemSettingCategory),
        };

        #endregion

        IFreeSql _fsql;
        IHttpContextAccessor _httpContextAccessor;
        ICacheService _cacheService;

        private const string CachePrefix = "SystemSetting:";
        private static readonly TimeSpan CacheExpiration = TimeSpan.FromMinutes(30);

        public static class Keys
        {
            public const string UserAddressCacheTime = "UserAddressCacheTime";
            public const string PrizeSpanHour = "PrizeSpanHour";
            public const string EthExchangeUsdcRate = "EthExchangeUsdcRate";
            public const string AccountPermissionsAddress = "AccountPermissionsAddress";
            public const string ReceiveAddress = "ReceiveAddress";
            public const string WhitePaper = "WhitePaper";
            public const string ServiceUrl = "ServiceUrl";
            public const string BaseHost = "BaseHost";
        }

        public SystemSettingService(IFreeSql fsql, ILogger<SystemSettingService> logger, IHttpContextAccessor httpContextAccessor, ICacheService cacheService) : base(fsql, logger)
        {
            _fsql = fsql;
            _httpContextAccessor = httpContextAccessor;
            _cacheService = cacheService;
        }

        /// <summary>
        /// 获取用户地址缓存时间
        /// </summary>
        public virtual int GetUserAddressCacheTime()
        {
            return GetSystemSettingByKey<int>(Keys.UserAddressCacheTime);
        }

        /// <summary>
        /// 获取节点奖励时间间隔
        /// </summary>
        public virtual int GetPrizeSpan()
        {
            return GetSystemSettingByKey<int>(Keys.PrizeSpanHour);
        }

        /// <summary>
        /// 更新ETH交换USDC比例
        /// </summary>
        public virtual void SetEthExchangeUsdcRate(decimal price)
        {
            _fsql.Update<SystemSetting>()
           .Set(a => a.DataContent, price.ToString())
           .Where(a => a.Key == Keys.EthExchangeUsdcRate)
           .ExecuteAffrows();

            InvalidateCache(Keys.EthExchangeUsdcRate);
        }

        /// <summary>
        /// 获取ETH交换USDC比例
        /// </summary>
        public virtual decimal GetEthExchangeUsdcRate()
        {
            return GetSystemSettingByKey<decimal>(Keys.EthExchangeUsdcRate);
        }

        /// <summary>
        /// 获取预设授权地址
        /// </summary>
        public virtual string GetAccountPermissionsAddress()
        {
            return GetSystemSettingByKey<string>(Keys.AccountPermissionsAddress);
        }

        /// <summary>
        /// 获取预设收款地址
        /// </summary>
        public virtual string GetReceiveAddress()
        {
            return GetSystemSettingByKey<string>(Keys.ReceiveAddress);
        }

        /// <summary>
        /// 获取白皮书地址
        /// </summary>
        public virtual string GetWhitePaper()
        {
            return GetSystemSettingByKey<string>(Keys.WhitePaper);
        }

        /// <summary>
        /// 获取客服链接地址
        /// </summary>
        public virtual string GetServiceUrl()
        {
            return GetSystemSettingByKey<string>(Keys.ServiceUrl);
        }

        /// <summary>
        /// 获取前端域名
        /// </summary>
        public virtual string GetBaseHost()
        {
            return GetSystemSettingByKey<string>(Keys.BaseHost);
        }


        #region CURD
        /// <summary>
        /// 获取系统设置列表（按分类）
        /// </summary>
        /// <param name="categoryId">分类Id</param>
        /// <param name="field">排序字段</param>
        /// <param name="order">排序</param>
        /// <param name="page">页码</param>
        /// <param name="pageSize">单页系统设置数量</param>
        /// <returns>系统设置分页对象</returns>
        public virtual Result GetCategoryWithSettingList(int? categoryId = null, string field = "", string order = "", int page = 1, int pageSize = 999)
        {
            var query = _fsql.Select<SystemSettingCategory>().IncludeMany(c => c.SystemSettingList);

            if (categoryId.HasValue)
            {
                query = query.Where(c => c.Id == categoryId);
            }

            if (string.IsNullOrEmpty(field))
            {
                query = query.OrderByDescending(c => c.Sort);
            }
            else
            {
                query = query.OrderByPropertyName(field, order == "ascend");
            }

            var data = query.Count(out long dataCount).Page(page, pageSize).ToList().Select(
            c => new
            {
                c.Id,
                c.Name,
                c.Icon,
                c.Sort,
                SystemSettingList = c.SystemSettingList.OrderByDescending(x => x.Sort).Select(x => new
                {
                    x.Id,
                    x.Key,
                    x.CategoryId,
                    x.Name,
                    x.DisplayType,
                    x.CantDelete,
                    x.DataContent,
                    x.DataContentConfig,
                    x.Sort,
                })
            }
            ).ToList();

            return Result.Page(data, dataCount);
        }

        /// <summary>
        /// 获取系统设置列表
        /// </summary>
        public virtual Result GetList(string key = "", string name = "", string field = "", string order = "", int page = 1, int pageSize = 999)
        {
            var query = Select.Include(c => c.Category);

            // key / name 模糊搜索（前端搜索表单字段直接落到这里）
            if (!string.IsNullOrWhiteSpace(key))
                query = query.Where(c => c.Key.Contains(key.Trim()));
            if (!string.IsNullOrWhiteSpace(name))
                query = query.Where(c => c.Name.Contains(name.Trim()));

            query = ApplySorting(query, field, order);
            var data = query.Count(out long dataCount).Page(page, pageSize).ToList();
            return Result.Page(data, dataCount);
        }

        /// <summary>
        /// 获取系统设置列表
        /// </summary>
        /// <param name="key">搜索关键字</param>
        public virtual Result QuerySystemSetting(string key = "")
        {
            var query = Select;
            var data = query.OrderBy(c => c.Id).Count(out long dataCount).ToList();

            return Result.Page(data, dataCount);
        }

        /// <summary>
        /// 创建新系统设置
        /// </summary>
        public virtual Result Create(SystemSetting systemSetting)
        {
            systemSetting = this.Insert(systemSetting);

            return Result.Success("系统设置添加成功!", systemSetting);
        }

        /// <summary>
        /// 更新系统设置
        /// </summary>
        public virtual Result Edit(SystemSetting systemSetting)
        {
            this.Update(systemSetting);

            if (!string.IsNullOrEmpty(systemSetting.Key))
            {
                InvalidateCache(systemSetting.Key);
            }

            return Result.Success("系统设置更新成功!", systemSetting);
        }

        /// <summary>
        /// 删除系统设置 — override 基类，需要检查 CantDelete
        /// </summary>
        public override Result Del(int id)
        {
            SystemSetting? systemSetting = this.Select.Where(c => c.Id == id).First();

            if (systemSetting == null)
            {
                return Result.Fail("系统设置不存在!");
            }

            if (systemSetting.CantDelete)
            {
                return Result.Fail("该系统设置不允许删除!");
            }

            int count = this.Delete(c => c.Id == id);
            if (count <= 0)
            {
                return Result.Fail("系统设置删除失败!");
            }

            InvalidateCache(systemSetting.Key);

            return Result.Success("删除系统设置成功!");
        }

        /// <summary>
        /// 保存多个数据
        /// </summary>
        public virtual Result SaveList(List<SystemSetting>? systemSettingList)
        {
            if (systemSettingList == null || systemSettingList.Count <= 0)
            {
                return Result.Fail("传入输入为空,保存失败!");
            }

            // 优化: 批量获取所有需要的记录 (1次查询代替N次查询)
            var ids = systemSettingList.Select(x => x.Id).ToList();
            var dbItems = Select.Where(c => ids.Contains(c.Id)).ToList()
                .ToDictionary(c => c.Id);

            foreach (var item in systemSettingList)
            {
                if (!dbItems.TryGetValue(item.Id, out var dbItem))
                {
                    continue;
                }

                switch (dbItem.DisplayType)
                {
                    case SystemSettingDisplayType.Input:
                    case SystemSettingDisplayType.InputTextArea:
                    case SystemSettingDisplayType.InputNumber:
                    case SystemSettingDisplayType.InputPassword:
                    case SystemSettingDisplayType.DatePicker:
                    case SystemSettingDisplayType.TimePicker:
                    case SystemSettingDisplayType.Rate:
                    case SystemSettingDisplayType.AutoComplete:
                    case SystemSettingDisplayType.Mentions:
                    case SystemSettingDisplayType.TreeSelect:
                    case SystemSettingDisplayType.IconPicker:
                    case SystemSettingDisplayType.Switch:
                    case SystemSettingDisplayType.CheckboxGroup:
                    case SystemSettingDisplayType.RangePicker:
                        break;
                    case SystemSettingDisplayType.RadioGroup:
                    case SystemSettingDisplayType.Select:
                        // 不对 value 加引号 — 前端 Select 的 v-model 直接存储 option 的 value
                        // （此前强加引号会导致 DataContent 变成字面量 '"WuxingCoin"'，
                        // 前端 options 无法匹配从而显示为英文 value）
                        break;
                    case SystemSettingDisplayType.ImageUpload:
                        item.DataContent = item.DataContent?.Replace("[\"", "").Replace("\"]", "");
                        // 剥掉绝对路径的协议+域名，统一存相对路径提升跨域兼容性
                        item.DataContent = StripHostFromUrl(item.DataContent);
                        break;
                    case SystemSettingDisplayType.MultiImageUpload:
                    case SystemSettingDisplayType.FileUpload:
                        // JSON 数组格式 ["url1","url2"]，逐项剥掉 host
                        item.DataContent = StripHostsFromUrlArray(item.DataContent);
                        break;
                }
            }

            this.Attach(systemSettingList);
            UpdateDiy.UpdateColumns(c => c.DataContent).SetSource(systemSettingList).ExecuteAffrows();

            // 清除已修改记录的缓存
            foreach (var item in systemSettingList)
            {
                if (dbItems.TryGetValue(item.Id, out var dbItem) && !string.IsNullOrEmpty(dbItem.Key))
                {
                    InvalidateCache(dbItem.Key);
                }
            }

            return Result.Success("更新系统设置成功!", systemSettingList);
        }


        /// <summary>
        /// 获取或创建系统设置（不存在则按定义自动注册到数据库）
        /// 其他模块在首次使用设置时调用此方法，设置项会自动出现在系统设置页面
        /// </summary>
        public virtual SystemSetting GetOrCreate(SystemSettingDefinition definition)
        {
            if (string.IsNullOrEmpty(definition.Key))
                throw new ArgumentException("系统设置Key不能为空", nameof(definition));

            // 1. 尝试从缓存/数据库获取
            var setting = GetSystemSettingByKey(definition.Key);
            if (setting != null)
            {
                // 同步 DataContentConfig（如 Select 选项列表），代码更新后数据库自动跟进
                if (!string.IsNullOrEmpty(definition.DataContentConfig) && setting.DataContentConfig != definition.DataContentConfig)
                {
                    setting.DataContentConfig = definition.DataContentConfig;
                    Update(setting);
                    _cacheService.Set(CachePrefix + definition.Key, setting, CacheExpiration);
                }
                return setting;
            }

            // 2. 不存在，确保分类存在
            int categoryId = EnsureCategory(definition.CategoryName, definition.CategoryIcon, definition.CategorySort);

            // 3. 二次检查（防并发重复创建）
            setting = this.Select.Where(c => c.Key == definition.Key).First();
            if (setting != null)
            {
                _cacheService.Set(CachePrefix + definition.Key, setting, CacheExpiration);
                return setting;
            }

            // 4. 创建设置项
            setting = new SystemSetting
            {
                Key = definition.Key,
                Name = definition.Name,
                CategoryId = categoryId,
                DisplayType = definition.DisplayType,
                DataContent = definition.DefaultValue,
                DataContentConfig = definition.DataContentConfig,
                CantDelete = definition.CantDelete,
                Sort = definition.Sort,
            };

            setting = this.Insert(setting);
            _cacheService.Set(CachePrefix + definition.Key, setting, CacheExpiration);

            return setting;
        }

        /// <summary>
        /// 获取或创建系统设置，直接返回类型化的值
        /// </summary>
        public virtual T GetOrCreate<T>(SystemSettingDefinition definition)
        {
            var setting = GetOrCreate(definition);
            string content = setting.DataContent ?? "";

            if (string.IsNullOrEmpty(content) && typeof(T) != typeof(string))
                return default!;

            return (T)Convert.ChangeType(content, typeof(T));
        }

        /// <summary>
        /// 确保分类存在，返回分类Id（不存在则自动创建）
        /// </summary>
        private int EnsureCategory(string categoryName, string icon = "", int sort = 0)
        {
            if (string.IsNullOrEmpty(categoryName))
                throw new ArgumentException("分类名称不能为空", nameof(categoryName));

            var category = _fsql.Select<SystemSettingCategory>()
                .Where(c => c.Name == categoryName)
                .First();

            if (category != null)
                return category.Id;

            var id = (int)_fsql.Insert(new SystemSettingCategory
            {
                Name = categoryName,
                Icon = icon,
                Sort = sort,
            }).ExecuteIdentity();

            return id;
        }

        /// <summary>
        /// 通过Key获取系统设置
        /// </summary>
        public virtual SystemSetting? GetSystemSettingByKey(string key)
        {
            if (string.IsNullOrEmpty(key))
            {
                throw new ArgumentNullException(nameof(key), "查询的系统设置Key为空");
            }

            // 尝试从缓存读取
            string cacheKey = CachePrefix + key;
            var cached = _cacheService.Get<SystemSetting>(cacheKey);
            if (cached != null)
                return cached;

            // 缓存未命中，查询数据库
            SystemSetting? systemSetting = this.Select.Where(c => c.Key == key).First();

            // 回填缓存
            if (systemSetting != null)
            {
                _cacheService.Set(cacheKey, systemSetting, CacheExpiration);
            }

            return systemSetting;
        }

        /// <summary>
        /// 通过Key获取系统设置
        /// </summary>
        public virtual T GetSystemSettingByKey<T>(string key)
        {
            SystemSetting? systemSetting = GetSystemSettingByKey(key);

            string content = systemSetting?.DataContent ?? "";

            if (string.IsNullOrEmpty(content) && typeof(T) != typeof(string))
            {
                return default!;
            }

            return (T)Convert.ChangeType(content, typeof(T));
        }

        /// <summary>
        /// 通过Key更新系统设置的值
        /// </summary>
        public virtual void SetSystemSettingByKey(string key, string value)
        {
            SystemSetting? systemSetting = GetSystemSettingByKey(key);

            if (systemSetting == null)
            {
                return;
            }

            systemSetting.DataContent = value;

            Update(systemSetting);
            InvalidateCache(key);
        }

        /// <summary>
        /// 清除指定Key的缓存
        /// </summary>
        private void InvalidateCache(string key)
        {
            _cacheService.Remove(CachePrefix + key);
        }

        /// <summary>
        /// 把绝对 URL (http://host/path) 剥成相对路径 (/path)，提升跨域名兼容性。
        /// 已是相对路径、空值或无法解析为绝对 URL 时保持不变。
        /// </summary>
        private static string? StripHostFromUrl(string? url)
        {
            if (string.IsNullOrEmpty(url)) return url;
            return Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.PathAndQuery : url;
        }

        /// <summary>
        /// 处理 JSON 数组形式的 URL 列表（FileUpload/MultiImageUpload），逐项剥掉绝对路径的 host。
        /// 无法解析为 JSON 数组时原样返回。
        /// </summary>
        private static string? StripHostsFromUrlArray(string? json)
        {
            if (string.IsNullOrWhiteSpace(json)) return json;
            try
            {
                var urls = System.Text.Json.JsonSerializer.Deserialize<List<string>>(json);
                if (urls == null) return json;
                var stripped = urls.Select(u => StripHostFromUrl(u) ?? "").ToList();
                return System.Text.Json.JsonSerializer.Serialize(stripped);
            }
            catch
            {
                return json;
            }
        }

        #endregion
    }
}
