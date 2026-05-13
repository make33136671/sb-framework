using Microsoft.Extensions.Configuration;
using SB.Framework.Models.Qdrant;
using SB.Framework.Services.Qdrant;

namespace SB.Framework.WebApi.Extensions;

public static class QdrantSetup
{
    /// <summary>
    /// 注册 Qdrant 向量数据库驱动。配置段 <see cref="QdrantOptions.SectionName"/>(<c>Sb:Qdrant</c>)。
    /// <para>未配置时使用默认值(localhost:6334)。Singleton 仅注册 factory,首次解析 <see cref="IQdrantDriver"/> 时才建连。</para>
    /// </summary>
    public static IServiceCollection AddQdrantService(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<QdrantOptions>(configuration.GetSection(QdrantOptions.SectionName));
        services.AddSingleton<IQdrantDriver, QdrantDriver>();
        return services;
    }
}
