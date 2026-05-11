using System.Text;

namespace SB.Framework.Core.Util
{
    /// <summary>
    /// HTTP 请求工具
    /// </summary>
    public static class HttpUtil
    {
        private static readonly HttpClient _client = new()
        {
            Timeout = TimeSpan.FromSeconds(30)
        };

        public static async Task<string> GetAsync(string url, CancellationToken cancellationToken = default)
        {
            var response = await _client.GetAsync(url, cancellationToken);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsStringAsync(cancellationToken);
        }

        public static async Task<string> PostJsonAsync(string url, object data, CancellationToken cancellationToken = default)
        {
            var json = System.Text.Json.JsonSerializer.Serialize(data);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await _client.PostAsync(url, content, cancellationToken);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsStringAsync(cancellationToken);
        }

        public static async Task<string> PostFormAsync(string url, Dictionary<string, string> formData, CancellationToken cancellationToken = default)
        {
            var content = new FormUrlEncodedContent(formData);
            var response = await _client.PostAsync(url, content, cancellationToken);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsStringAsync(cancellationToken);
        }
    }
}
