using System.Reflection;

namespace SB.Framework.Features;

/// <summary>单文件/裁剪发布时，派生项目实现此接口显式提供 feature 程序集清单。
/// 常规发布无需实现 —— DependencyContext 路径即可。
/// 用法：派生项目在 Program.cs 显式 new XxxSingleFilePublish().IncludeAssemblies() 传入
/// FeatureScannerOptions.SingleFilePublishAssemblies（最稳，不依赖反射）。</summary>
public interface IFeatureSingleFilePublish
{
    Assembly[] IncludeAssemblies();
}
