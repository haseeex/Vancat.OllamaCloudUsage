using System;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Vancat.OllamaCloudUsage.Services
{
    /// <summary>
    /// 缓存快照（v2）：一次刷新包含小时用量、每日用量与余额三份原始 JSON。
    ///
    /// 版本号在存储格式变化时递增，旧格式缓存会被自动忽略并重新获取。
    /// </summary>
    public sealed class CacheSnapshot
    {
        /// <summary>存储格式版本。v2 = 适配 2026-10-06 新 API。</summary>
        public int Version { get; set; } = CacheVersion;

        /// <summary>按小时分桶的用量原始 JSON。</summary>
        public JObject Hourly { get; set; }

        /// <summary>按天分桶的用量原始 JSON。</summary>
        public JObject Daily { get; set; }

        /// <summary>余额原始 JSON。</summary>
        public JObject Balance { get; set; }

        /// <summary>写入时刻（epoch 毫秒）。</summary>
        public long FetchedAtMs { get; set; }

        /// <summary>账户 id。</summary>
        public string AccountId { get; set; }

        public const int CacheVersion = 2;
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
                        var snapshot = serializer.Deserialize<CacheSnapshot>(jsonReader);

                        // 旧格式缓存直接忽略。
                        if (snapshot == null || snapshot.Version != CacheSnapshot.CacheVersion)
                        {
                            return null;
                        }

                        return snapshot;
                    }
                }
                catch
                {
                    return null;
                }
            }
        }

        public void Write(JObject hourly, JObject daily, JObject balance, string accountId)
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
                        Hourly = hourly,
                        Daily = daily,
                        Balance = balance,
                        FetchedAtMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                        AccountId = accountId,
                    };

                    // 先写临时文件再原子替换，避免多实例同时写导致文件损坏。
                    var payload = JsonConvert.SerializeObject(snapshot);
                    var tmp = _filePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
                    File.WriteAllText(tmp, payload, Encoding.UTF8);

                    if (File.Exists(_filePath))
                    {
                        File.Delete(_filePath);
                    }

                    File.Move(tmp, _filePath);
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
            if (snapshot?.Hourly == null || snapshot.Daily == null || snapshot.Balance == null)
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
