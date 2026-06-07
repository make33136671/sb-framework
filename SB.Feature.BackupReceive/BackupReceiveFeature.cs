using SB.Framework.Features;

namespace SB.Feature.BackupReceive
{
    /// <summary>
    /// 云端备份接收端插件清单（无状态薄壳）。
    /// Service 靠 [AutoDependencyInjection] 自动注册，清单【不】手动 AddScoped（避免双注册）。
    /// 无接口绑定特例、无中间件 → 不实现 ConfigureServices / Configure。
    /// </summary>
    public sealed class BackupReceiveFeature : IFeatureModule
    {
        public string FeatureName => "BackupReceive";
        public string DisplayName => "云端备份接收";
        public Type OptionsType => typeof(BackupReceiveOptions); // 自动绑定 Features:BackupReceive
    }
}
