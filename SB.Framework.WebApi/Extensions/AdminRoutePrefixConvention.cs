using Microsoft.AspNetCore.Mvc.ApplicationModels;
using Microsoft.AspNetCore.Mvc.Routing;

namespace SB.Framework.WebApi.Extensions;

/// <summary>
/// 路由约定：自动为所有控制器添加 admin/ 前缀
/// 使用绝对路由（以 / 开头）的控制器不受影响
/// </summary>
public class AdminRoutePrefixConvention : IApplicationModelConvention
{
    private readonly AttributeRouteModel _prefix = new() { Template = "admin" };

    public void Apply(ApplicationModel application)
    {
        foreach (var controller in application.Controllers)
        {
            foreach (var selector in controller.Selectors)
            {
                if (selector.AttributeRouteModel is { } route)
                {
                    // 绝对路由（以 / 或 ~/ 开头）不处理
                    if (route.Template != null &&
                        (route.Template.StartsWith('/') || route.Template.StartsWith("~/")))
                        continue;

                    // 相对路由：拼接 admin/ 前缀
                    selector.AttributeRouteModel = AttributeRouteModel.CombineAttributeRouteModel(_prefix, route);
                }
                else
                {
                    // 无路由定义：添加默认 admin 路由
                    selector.AttributeRouteModel = new AttributeRouteModel
                    {
                        Template = "admin/[controller]/[action]"
                    };
                }
            }
        }
    }
}
