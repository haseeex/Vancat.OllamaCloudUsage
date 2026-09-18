using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Vancat.OllamaCloudUsage.Services
{
    /// <summary>单个模型的用量记录。</summary>
    public sealed class ModelUsage
    {
        public string Name { get; set; }
        public long RequestCount { get; set; }
    }

    /// <summary>单个限额窗口（会话 / 每周）的用量。</summary>
    public sealed class LimitUsage
    {
        public double Usage { get; set; }
        public List<ModelUsage> Models { get; set; } = new List<ModelUsage>();
    }

    /// <summary>活动周期信息。</summary>
    public sealed class ActivityPeriod
    {
        public string Type { get; set; }
        public string StartingAt { get; set; }
        public string EndingAt { get; set; }
    }

    /// <summary>活动统计。</summary>
    public sealed class ActivityInfo
    {
        public string Cost { get; set; }
        public ActivityPeriod Period { get; set; }
        public List<ModelUsage> Models { get; set; } = new List<ModelUsage>();
    }

    /// <summary>Ollama Cloud 用量响应。</summary>
    public sealed class UsageResponse
    {
        public ActivityInfo Activity { get; set; }
        public LimitUsage Session { get; set; }
        public LimitUsage Weekly { get; set; }
    }

    /// <summary>用量 API 异常。</summary>
    public sealed class UsageApiException : Exception
    {
        public UsageApiException(string message) : base(message) { }
        public UsageApiException(string message, Exception inner) : base(message, inner) { }
    }

    /// <summary>Ollama Cloud 用量 API 客户端。</summary>
    public sealed class OllamaApiClient : IDisposable
    {
        private const string UsageUrl = "https://ollama.com/api/usage";
        private const int MaxResponseBytes = 1024 * 1024;
        private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(10);

        /// <summary>
        /// 解析设置：禁用日期自动转换，避免 ISO 时间串被转成 Date token
        /// （否则字符串字段校验会失败）。
        /// </summary>
        private static readonly JsonLoadSettings LoadSettings = new JsonLoadSettings
        {
            CommentHandling = CommentHandling.Ignore,
        };

        private readonly HttpClient _http;

        public OllamaApiClient()
        {
            _http = new HttpClient { Timeout = RequestTimeout };
            _http.DefaultRequestHeaders.Add("Accept", "application/json");
        }

        /// <summary>按统一设置解析 JSON 对象（禁用日期自动转换）。</summary>
        public static JObject ParseJson(string json)
        {
            using (var stringReader = new StringReader(json))
            using (var jsonReader = new JsonTextReader(stringReader))
            {
                jsonReader.DateParseHandling = DateParseHandling.None;
                return JObject.Load(jsonReader, LoadSettings);
            }
        }

        public async Task<UsageResponse> FetchUsageAsync(string apiKey, CancellationToken cancellationToken)
        {
            var json = await FetchUsageJsonAsync(apiKey, cancellationToken).ConfigureAwait(false);
            return ParseUsage(json);
        }

        /// <summary>
        /// 拉取用量并返回原始 JSON（用于缓存写入，保留服务端的字段名）。
        /// </summary>
        public async Task<JObject> FetchUsageJsonAsync(string apiKey, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                throw new UsageApiException("Ollama API 密钥为空。");
            }

            using (var request = new HttpRequestMessage(HttpMethod.Get, UsageUrl))
            {
                request.Headers.Add("Authorization", apiKey.Trim());
                HttpResponseMessage response;
                try
                {
                    response = await _http.SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    throw new UsageApiException("Ollama 请求超时。");
                }
                catch (HttpRequestException ex)
                {
                    throw new UsageApiException("无法连接 Ollama Cloud：" + ex.Message, ex);
                }

                using (response)
                {
                    if (!response.IsSuccessStatusCode)
                    {
                        throw new UsageApiException($"Ollama 返回 HTTP {(int)response.StatusCode}。");
                    }

                    var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    if (body.Length > MaxResponseBytes)
                    {
                        throw new UsageApiException("Ollama 响应过大。");
                    }

                    try
                    {
                        return ParseJson(body);
                    }
                    catch (Exception ex)
                    {
                        throw new UsageApiException("Ollama 响应无效。", ex);
                    }
                }
            }
        }

        internal static UsageResponse ParseUsage(JObject root)
        {
            var activity = RequireObject(root, "activity", "activity");
            var period = RequireObject(activity, "period", "activity.period");
            var limits = RequireObject(root, "limits", "limits");

            return new UsageResponse
            {
                Activity = new ActivityInfo
                {
                    Cost = RequireString(activity, "cost", "activity.cost"),
                    Period = new ActivityPeriod
                    {
                        Type = RequireString(period, "type", "activity.period.type"),
                        StartingAt = RequireString(period, "starting_at", "activity.period.starting_at"),
                        EndingAt = RequireString(period, "ending_at", "activity.period.ending_at"),
                    },
                    Models = ParseModels(activity["models"], "activity.models"),
                },
                Session = ParseLimit(limits["session"], "limits.session"),
                Weekly = ParseLimit(limits["weekly"], "limits.weekly"),
            };
        }

        private static JObject RequireObject(JObject parent, string key, string displayName)
        {
            var token = parent[key];
            if (token is JObject obj)
            {
                return obj;
            }

            throw new UsageApiException($"Ollama 响应中的 {displayName} 无效。");
        }

        private static string RequireString(JObject parent, string key, string name)
        {
            var token = parent[key];
            if (token != null && token.Type == JTokenType.String)
            {
                return token.Value<string>();
            }

            throw new UsageApiException($"Ollama 响应中的 {name} 无效。");
        }

        private static List<ModelUsage> ParseModels(JToken token, string name)
        {
            if (!(token is JArray array))
            {
                throw new UsageApiException($"Ollama 响应中的 {name} 无效。");
            }

            var result = new List<ModelUsage>();
            for (var i = 0; i < array.Count; i++)
            {
                if (!(array[i] is JObject item))
                {
                    throw new UsageApiException($"Ollama 响应中的 {name}[{i}] 无效。");
                }

                var modelName = item["name"];
                var count = item["request_count"];
                if (modelName == null || modelName.Type != JTokenType.String ||
                    count == null || (count.Type != JTokenType.Integer && count.Type != JTokenType.Float))
                {
                    throw new UsageApiException($"Ollama 响应中的 {name}[{i}] 无效。");
                }

                result.Add(new ModelUsage
                {
                    Name = modelName.Value<string>(),
                    RequestCount = count.Value<long>(),
                });
            }

            return result;
        }

        private static LimitUsage ParseLimit(JToken token, string name)
        {
            if (!(token is JObject obj))
            {
                throw new UsageApiException($"Ollama 响应中的 {name} 无效。");
            }

            var usage = obj["usage"];
            if (usage == null || (usage.Type != JTokenType.Integer && usage.Type != JTokenType.Float))
            {
                throw new UsageApiException($"Ollama 响应中的 {name}.usage 无效。");
            }

            return new LimitUsage
            {
                Usage = usage.Value<double>(),
                Models = ParseModels(obj["models"], $"{name}.models"),
            };
        }

        public void Dispose()
        {
            _http.Dispose();
        }
    }
}
