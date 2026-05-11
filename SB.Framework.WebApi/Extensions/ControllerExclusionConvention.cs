using Microsoft.AspNetCore.Mvc.ApplicationModels;

namespace SB.Framework.WebApi.Extensions;

/// <summary>
/// 路由约定：排除指定的 Controller，使其不参与路由匹配。
/// fork 项目可通过此约定禁用框架 Controller，用项目自己的 Controller 替代。
/// </summary>
public class ControllerExclusionConvention : IApplicationModelConvention
{
    private readonly HashSet<string> _excludedControllers;

    /// <summary>
    /// 构造函数
    /// </summary>
    /// <param name="excludedControllerTypeNames">
    /// 要排除的 Controller 类型全名列表（含命名空间），
    /// 如 "SB.Framework.WebApi.Controllers.AuthController"
    /// </param>
    public ControllerExclusionConvention(IEnumerable<string> excludedControllerTypeNames)
    {
        _excludedControllers = new HashSet<string>(excludedControllerTypeNames);
    }

    public void Apply(ApplicationModel application)
    {
        for (int i = application.Controllers.Count - 1; i >= 0; i--)
        {
            var controller = application.Controllers[i];
            if (_excludedControllers.Contains(controller.ControllerType.FullName!))
            {
                application.Controllers.RemoveAt(i);
            }
        }
    }
}
