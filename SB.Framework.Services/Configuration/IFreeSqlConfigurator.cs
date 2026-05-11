using FreeSql;

namespace SB.Framework.Services.Configuration;

/// <summary>
/// FreeSql 配置扩展点 — 派生项目实现此接口注册 GlobalFilter / Mapper / Converter。
/// 基座在 IFreeSql 实例化后从 DI 拿所有实现，按 Order 顺序调用 Configure。
/// </summary>
public interface IFreeSqlConfigurator
{
    /// <summary>执行顺序，越小越先（基座 0，派生 100+）</summary>
    int Order { get; }

    /// <summary>对 FreeSql 实例做配置</summary>
    void Configure(IFreeSql fsql);
}
