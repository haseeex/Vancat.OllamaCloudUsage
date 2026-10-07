using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Vancat.OllamaCloudUsage.Services
{
    /// <summary>`GET /api/usage` 接受的时间范围。</summary>
    public enum UsageRange
    {
        Last24Hours,
        Last7Days,
        Last30Days,
    }

    /// <summary>用量指标：旧版计划仅上报请求数，其余指标为可选。</summary>
    public sealed class UsageMetrics
    {
        public long RequestCount { get; set; }
        public double? UsageUsd { get; set; }
        public long? InputTokens { get; set; }
        public long? CachedInputTokens { get; set; }
        public long? OutputTokens { get; set; }
    }

    /// <summary>单个时间分桶（按小时或按天）。</summary>
    public sealed class UsageBucket
    {
        public string From { get; set; }
        public string Until { get; set; }
        public bool Partial { get; set; }
        public UsageMetrics Metrics { get; set; } = new UsageMetrics();

        public long RequestCount => Metrics.RequestCount;
    }

    /// <summary>`GET /api/usage?range=…` 的响应。</summary>
    public sealed class UsageResponse
    {
        public string Range { get; set; }
        public string Scope { get; set; }
        public string Granularity { get; set; }
        public string From { get; set; }
        public string Until { get; set; }
        public UsageMetrics Totals { get; set; } = new UsageMetrics();
        public List<UsageBucket> Buckets { get; set; } = new List<UsageBucket>();
    }

    /// <summary>旧版计划的一个配额窗口（5 小时会话 / 每周），按剩余百分比计。</summary>
    public sealed class BalanceWindow
    {
        public double RemainingPercent { get; set; }
        public string ResetsAt { get; set; }
    }

    /// <summary>旧版计划余额：会话与每周两个窗口。</summary>
    public sealed class LegacyIncludedBalance
    {
        public BalanceWindow Session { get; set; }
        public BalanceWindow Weekly { get; set; }
    }

    /// <summary>信用计划的周期信息。</summary>
    public sealed class CreditsPeriod
    {
        public string From { get; set; }
        public string Until { get; set; }
    }

    /// <summary>信用计划余额：包含额度与周期。</summary>
    public sealed class CreditsIncludedBalance
    {
        public double BalanceUsd { get; set; }
        public double AllowanceUsd { get; set; }
        public CreditsPeriod Period { get; set; }
    }

    /// <summary>`GET /api/balance` 的响应（两种计划形态之一）。</summary>
    public sealed class BalanceResponse
    {
        /// <summary>旧版计划的会话/每周窗口；信用计划时为 null。</summary>
        public LegacyIncludedBalance Legacy { get; set; }

        /// <summary>信用计划的额度信息；旧版计划时为 null。</summary>
        public CreditsIncludedBalance Credits { get; set; }

        /// <summary>已购买余额（美元）。</summary>
        public double PurchasedUsd { get; set; }

        /// <summary>是否为旧版（会话/每周窗口）计划。</summary>
        public bool IsLegacy => Legacy != null;
    }

    /// <summary>一次刷新所需的全部数据：小时/每日请求历史 + 配额余额。</summary>
    public sealed class UsageSnapshot
    {
        /// <summary>按小时分桶（用于 5 小时会话窗口的请求数统计）。</summary>
        public UsageResponse Hourly { get; set; }

        /// <summary>按天分桶（用于每周窗口的请求数统计）。</summary>
        public UsageResponse Daily { get; set; }

        /// <summary>配额余额（用量百分比与权威重置时间）。</summary>
        public BalanceResponse Balance { get; set; }
    }

    /// <summary>用量 API 异常。</summary>
    public sealed class UsageApiException : Exception
    {
        public UsageApiException(string message) : base(message) { }
        public UsageApiException(string message, Exception inner) : base(message, inner) { }
    }

    /// <summary>
    /// Ollama Cloud API 客户端（适配官方 2026-10-06 改版）。
    ///
    /// 端点：
    /// - `GET /api/usage?range=24h|7d|30d`：按小时/天分桶的请求计数，
    ///   可选 `usage_usd`、`input_tokens` 等指标（旧版计划仅有请求数）。
    /// - `GET /api/balance`：5 小时 / 每周窗口的剩余百分比与权威重置时间
    ///   （旧版计划），或包含额度与周期（信用计划）。
    /// </summary>
    public sealed class OllamaApiClient : IDisposable
    {
        private const string UsageUrl = "https://ollama.com/api/usage";
        private const string BalanceUrl = "https://ollama.com/api/balance";
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

        /// <summary>
        /// 拉取一次刷新所需的全部数据（并行请求 24h、7d 用量与余额）。
        /// </summary>
        public async Task<UsageSnapshot> FetchSnapshotAsync(string apiKey, CancellationToken cancellationToken)
        {
            var hourlyTask = FetchUsageAsync(apiKey, UsageRange.Last24Hours, cancellationToken);
            var dailyTask = FetchUsageAsync(apiKey, UsageRange.Last7Days, cancellationToken);
            var balanceTask = FetchBalanceAsync(apiKey, cancellationToken);

            await Task.WhenAll(hourlyTask, dailyTask, balanceTask).ConfigureAwait(false);

            return new UsageSnapshot
            {
                Hourly = await hourlyTask.ConfigureAwait(false),
                Daily = await dailyTask.ConfigureAwait(false),
                Balance = await balanceTask.ConfigureAwait(false),
            };
        }

        /// <summary>
        /// 拉取用量并返回原始 JSON（用于缓存写入，保留服务端的字段名）。
        /// </summary>
        public async Task<JObject> FetchUsageJsonAsync(string apiKey, UsageRange range, CancellationToken cancellationToken)
        {
            var url = UsageUrl + "?range=" + ToRangeText(range);
            return await GetJsonAsync(url, apiKey, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>拉取余额原始 JSON。</summary>
        public async Task<JObject> FetchBalanceJsonAsync(string apiKey, CancellationToken cancellationToken)
        {
            return await GetJsonAsync(BalanceUrl, apiKey, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>拉取并解析用量。</summary>
        public async Task<UsageResponse> FetchUsageAsync(string apiKey, UsageRange range, CancellationToken cancellationToken)
        {
            var json = await FetchUsageJsonAsync(apiKey, range, cancellationToken).ConfigureAwait(false);
            return ParseUsage(json);
        }

        /// <summary>拉取并解析余额。</summary>
        public async Task<BalanceResponse> FetchBalanceAsync(string apiKey, CancellationToken cancellationToken)
        {
            var json = await FetchBalanceJsonAsync(apiKey, cancellationToken).ConfigureAwait(false);
            return ParseBalance(json);
        }

        private static string ToRangeText(UsageRange range)
        {
            switch (range)
            {
                case UsageRange.Last24Hours:
                    return "24h";
                case UsageRange.Last30Days:
                    return "30d";
                default:
                    return "7d";
            }
        }

        private async Task<JObject> GetJsonAsync(string url, string apiKey, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                throw new UsageApiException(Loc.T("Err.EmptyKey"));
            }

            using (var request = new HttpRequestMessage(HttpMethod.Get, url))
            {
                request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + apiKey.Trim());
                HttpResponseMessage response;
                try
                {
                    response = await _http.SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    throw new UsageApiException(Loc.T("Err.Timeout"));
                }
                catch (HttpRequestException ex)
                {
                    throw new UsageApiException(Loc.T("Err.CannotConnect", ex.Message), ex);
                }

                using (response)
                {
                    // 429：提示等待秒数（Retry-After）。
                    if ((int)response.StatusCode == 429)
                    {
                        double? retryAfter = null;
                        var retryHeader = response.Headers.RetryAfter;
                        if (retryHeader != null)
                        {
                            if (retryHeader.Delta.HasValue)
                            {
                                retryAfter = retryHeader.Delta.Value.TotalSeconds;
                            }
                            else if (retryHeader.Date.HasValue)
                            {
                                retryAfter = (retryHeader.Date.Value - DateTimeOffset.UtcNow).TotalSeconds;
                            }
                        }

                        var seconds = retryAfter.HasValue
                            ? Math.Max(0, Math.Round(retryAfter.Value)).ToString("0")
                            : "?";
                        throw new UsageApiException(Loc.T("Err.RateLimited", seconds));
                    }

                    if (!response.IsSuccessStatusCode)
                    {
                        throw new UsageApiException(Loc.T("Err.HttpStatus", (int)response.StatusCode));
                    }

                    var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    if (body.Length > MaxResponseBytes)
                    {
                        throw new UsageApiException(Loc.T("Err.ResponseTooLarge"));
                    }

                    try
                    {
                        return ParseJson(body);
                    }
                    catch (Exception ex)
                    {
                        throw new UsageApiException(Loc.T("Err.InvalidResponse"), ex);
                    }
                }
            }
        }

        // ---- 解析 ----

        internal static UsageResponse ParseUsage(JObject root)
        {
            return new UsageResponse
            {
                Range = RequireString(root, "range", "range"),
                Scope = RequireString(root, "scope", "scope"),
                Granularity = RequireString(root, "granularity", "granularity"),
                From = RequireString(root, "from", "from"),
                Until = RequireString(root, "until", "until"),
                Totals = ParseMetrics(root["totals"], "totals"),
                Buckets = ParseBuckets(root["buckets"], "buckets"),
            };
        }

        internal static BalanceResponse ParseBalance(JObject root)
        {
            var included = RequireObject(root["included"], "included");
            var purchased = RequireObject(root["purchased"], "purchased");

            var response = new BalanceResponse
            {
                PurchasedUsd = RequireNumber(purchased, "balance_usd", "purchased.balance_usd"),
            };

            // 旧版计划带 session/weekly 窗口；信用计划带美元额度。
            if (included["session"] != null && included["weekly"] != null)
            {
                response.Legacy = new LegacyIncludedBalance
                {
                    Session = ParseBalanceWindow(included["session"], "included.session"),
                    Weekly = ParseBalanceWindow(included["weekly"], "included.weekly"),
                };
            }
            else
            {
                var period = RequireObject(included["period"], "included.period");
                response.Credits = new CreditsIncludedBalance
                {
                    BalanceUsd = RequireNumber(included, "balance_usd", "included.balance_usd"),
                    AllowanceUsd = RequireNumber(included, "allowance_usd", "included.allowance_usd"),
                    Period = new CreditsPeriod
                    {
                        From = RequireString(period, "from", "included.period.from"),
                        Until = RequireString(period, "until", "included.period.until"),
                    },
                };
            }

            return response;
        }

        private static BalanceWindow ParseBalanceWindow(JToken token, string name)
        {
            var obj = RequireObject(token, name);
            return new BalanceWindow
            {
                RemainingPercent = RequireNumber(obj, "remaining_percent", name + ".remaining_percent"),
                ResetsAt = RequireString(obj, "resets_at", name + ".resets_at"),
            };
        }

        private static UsageMetrics ParseMetrics(JToken token, string name)
        {
            var obj = RequireObject(token, name);
            return new UsageMetrics
            {
                RequestCount = (long)RequireNumber(obj, "request_count", name + ".request_count"),
                UsageUsd = OptionalNumber(obj["usage_usd"]),
                InputTokens = OptionalLong(obj["input_tokens"]),
                CachedInputTokens = OptionalLong(obj["cached_input_tokens"]),
                OutputTokens = OptionalLong(obj["output_tokens"]),
            };
        }

        private static List<UsageBucket> ParseBuckets(JToken token, string name)
        {
            if (!(token is JArray array))
            {
                throw new UsageApiException(Loc.T("Err.InvalidField", name));
            }

            var result = new List<UsageBucket>();
            for (var i = 0; i < array.Count; i++)
            {
                var item = RequireObject(array[i], name + "[" + i + "]");
                result.Add(new UsageBucket
                {
                    From = RequireString(item, "from", name + "[" + i + "].from"),
                    Until = RequireString(item, "until", name + "[" + i + "].until"),
                    Partial = item["partial"] != null && item["partial"].Type == JTokenType.Boolean && item["partial"].Value<bool>(),
                    Metrics = ParseMetrics(item, name + "[" + i + "]"),
                });
            }

            return result;
        }

        private static JObject RequireObject(JToken token, string displayName)
        {
            if (token is JObject obj)
            {
                return obj;
            }

            throw new UsageApiException(Loc.T("Err.InvalidField", displayName));
        }

        private static string RequireString(JObject parent, string key, string name)
        {
            var token = parent[key];
            if (token != null && token.Type == JTokenType.String)
            {
                return token.Value<string>();
            }

            throw new UsageApiException(Loc.T("Err.InvalidField", name));
        }

        private static double RequireNumber(JObject parent, string key, string name)
        {
            var token = parent[key];
            if (token != null && (token.Type == JTokenType.Integer || token.Type == JTokenType.Float))
            {
                return token.Value<double>();
            }

            throw new UsageApiException(Loc.T("Err.InvalidField", name));
        }

        /// <summary>可选数值：缺失或格式异常时返回 null（不视为错误）。</summary>
        private static double? OptionalNumber(JToken token)
        {
            if (token != null && (token.Type == JTokenType.Integer || token.Type == JTokenType.Float))
            {
                return token.Value<double>();
            }

            return null;
        }

        private static long? OptionalLong(JToken token)
        {
            var value = OptionalNumber(token);
            return value.HasValue ? (long)value.Value : (long?)null;
        }

        public void Dispose()
        {
            _http.Dispose();
        }
    }
}
