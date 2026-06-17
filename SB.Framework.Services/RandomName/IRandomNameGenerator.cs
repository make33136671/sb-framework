namespace SB.Framework.Services
{
    /// <summary>
    /// 随机名称生成器:随机中文人名 + 生活服务备注名(送快递的 / 楼下饭店 / 快递小哥 / 送水的 …)。
    /// 框架层通用能力,业务层注入即用——造测试/占位数据、生成默认昵称备注等场景复用。
    /// </summary>
    public interface IRandomNameGenerator
    {
        /// <summary>随机中文人名(姓 + 名,名 1~2 字,含少量复姓)。</summary>
        string ChineseName();

        /// <summary>批量随机中文人名,尽力去重。</summary>
        IReadOnlyList<string> ChineseNames(int count);

        /// <summary>随机生活服务备注名(送快递的 / 楼下饭店 / 快递小哥 / 修空调的 / 顺丰快递 …)。</summary>
        string ServiceName();

        /// <summary>批量随机服务备注名,尽力去重。</summary>
        IReadOnlyList<string> ServiceNames(int count);
    }
}
