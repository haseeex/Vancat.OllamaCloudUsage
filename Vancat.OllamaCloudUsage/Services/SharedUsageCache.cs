using System;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Vancat.OllamaCloudUsage.Services
{
    /// <summary>缓存快照。</summary>
    public sealed class CacheSnapshot
    {
        public JObject Usage { get; set; }
        public long FetchedAtMs { get; set; }
        public string AccountId { get; set; }
    }

    /// <summary>
    /// 跨 Visual Studio 实例共享的用量缓存。
    /// 所有实例读写同一文件，缓存新鲜时直接复用，避免多实例重复请求 API。
    /// </summary>
    public sealed class SharedUsageCache
    {
        private readonly string _filePath;
        private readonly object _sync = new object();

        public SharedUsageCache(string filePath = null)
        {
            _filePath = filePath ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "vancat", "OllamaCloudUsage", "usage-cache.json");
        }

        public string FilePath => _filePath;

        public CacheSnapshot Read()
        {
            lock (_sync)
            {
                if (!File.Exists(_filePath))
                {
                    return null;
                }

                try
                {
                    var json = File.ReadAllText(_filePath, Encoding.UTF8);

                    // 禁用日期自动转换：Usage 内的 ISO 时间串必须保持字符串类型，
                    // 否则后续 ParseUsage 的字段校验会失败。
                    using (var stringReader = new StringReader(json))
                    using (var jsonReader = new JsonTextReader(stringReader))
                    {
                        jsonReader.DateParseHandling = DateParseHandling.None;
                        var serializer = JsonSerializer.CreateDefault();
                        return serializer.Deserialize<CacheSnapshot>(jsonReader);
                    }
                }
                catch
                {
                    return null;
                }
            }
        }

        public void Write(JObject usage, string accountId)
        {
            lock (_sync)
            {
                try
                {
                    var dir = Path.GetDirectoryName(_filePath);
                    if (!string.IsNullOrEmpty(dir))
                    {
                        Directory.CreateDirectory(dir);
                    }

                    var snapshot = new CacheSnapshot
                    {
                        Usage = usage,
                        FetchedAtMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                        AccountId = accountId,
                    };
                    File.WriteAllText(_filePath, JsonConvert.SerializeObject(snapshot), Encoding.UTF8);
                }
                catch
                {
                    // 缓存写入失败不影响主流程。
                }
            }
        }

        /// <summary>判断缓存是否仍然新鲜（未超过刷新间隔）。</summary>
        public static bool IsFresh(CacheSnapshot snapshot, TimeSpan maxAge, string accountId)
        {
            if (snapshot?.Usage == null)
            {
                return false;
            }

            if (!string.IsNullOrEmpty(accountId) && snapshot.AccountId != accountId)
            {
                return false;
            }

            var age = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - snapshot.FetchedAtMs;
            return age >= 0 && age < (long)maxAge.TotalMilliseconds;
        }
    }
}
