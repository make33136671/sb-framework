using System;
using System.IO;

namespace SB.Framework.Core.License
{
    /// <summary>
    /// 客户端密钥服务 — 管理首次安装生成的 UUID 标识
    /// </summary>
    public static class ClientKeyService
    {
        private static string? _cached;
        private const string FileName = "client.key";

        /// <summary>
        /// 获取客户端密钥（首次调用时自动生成并持久化）
        /// </summary>
        public static string GetClientKey(string? basePath = null)
        {
            if (_cached != null) return _cached;

            var dir = basePath ?? AppContext.BaseDirectory;
            var filePath = Path.Combine(dir, FileName);

            if (File.Exists(filePath))
            {
                var key = File.ReadAllText(filePath).Trim();
                if (!string.IsNullOrEmpty(key))
                {
                    _cached = key;
                    return _cached;
                }
            }

            // 首次安装，生成新的 UUID
            _cached = Guid.NewGuid().ToString("D");
            File.WriteAllText(filePath, _cached);
            return _cached;
        }
    }
}
