using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;

namespace SB.Framework.WebApi.Extensions;

/// <summary>
/// 从 MVC Controller 发现结果中移除被禁模块拥有的 Controller，使其端点不进路由表。
/// 按 Controller TypeInfo.FullName 与被禁 Controller 全名集合（大小写不敏感）匹配。
/// </summary>
public sealed class DisabledModuleControllerFilter : IApplicationFeatureProvider<ControllerFeature>
{
    private readonly IReadOnlySet<string> _bannedNames;

    public DisabledModuleControllerFilter(IReadOnlySet<string> bannedNames)
        => _bannedNames = bannedNames;

    public void PopulateFeature(IEnumerable<ApplicationPart> parts, ControllerFeature feature)
    {
        if (_bannedNames.Count == 0) return;
        var remove = feature.Controllers
            .Where(c => c.FullName != null && _bannedNames.Contains(c.FullName))
            .ToList();
        foreach (var c in remove)
            feature.Controllers.Remove(c);
    }
}
