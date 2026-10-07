using System;
using System.Collections.Generic;
using System.Globalization;

namespace Vancat.OllamaCloudUsage.Services
{
    /// <summary>
    /// 窗口时间计算（适配官方 /api/balance 端点）。
    ///
    /// `resets_at` 是当前窗口的**排他结束时刻**，因此窗口区间为
    /// `[resets_at − windowLength, resets_at)`；窗口内的请求数等于与该区间
    /// 重叠的用量分桶之和。
    ///
    /// 注意：不再本地推算 UTC 边界 —— 服务端返回的重置时间才是权威值。
    /// </summary>
    public static class ResetTime
    {
        /// <summary>旧版计划的 5 小时会话窗口长度（毫秒）。</summary>
        public const long SessionWindowMs = 5L * 3600 * 1000;

        /// <summary>旧版计划的 7 天每周窗口长度（毫秒）。</summary>
        public const long WeekMs = 7L * 86400 * 1000;

        /// <summary>以 `resetAtMs` 为结束点的窗口起始时刻（epoch 毫秒）。</summary>
        public static long WindowStartMs(long resetAtMs, long windowMs)
        {
            return resetAtMs - windowMs;
        }

        /// <summary>
        /// 解析 ISO-8601 时间串为 epoch 毫秒；不可用时返回 0。
        /// </summary>
        public static long ParseTimestampMs(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return 0;
            }

            return DateTimeOffset.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal,
                out var parsed)
                ? parsed.ToUnixTimeMilliseconds()
                : 0;
        }

        /// <summary>
        /// 统计与 `[fromMs, untilMs)` 重叠的分桶请求数之和。
        ///
        /// 窗口边界取自 balance 的 `resets_at`，与分桶网格（24h 为整点、
        /// 7d 为 UTC 零点）对齐，因此实际每个分桶要么完全在窗口内、要么完全
        /// 在外。部分重叠的分桶按整桶计入 —— 这是偏保守的近似，可避免低估。
        /// </summary>
        public static long SumRequestsInRange(IList<UsageBucket> buckets, long fromMs, long untilMs)
        {
            var total = 0L;
            if (buckets == null)
            {
                return total;
            }

            foreach (var bucket in buckets)
            {
                var bucketFrom = ParseTimestampMs(bucket.From);
                var bucketUntil = ParseTimestampMs(bucket.Until);
                if (bucketFrom == 0 || bucketUntil == 0)
                {
                    continue;
                }

                if (bucketUntil > fromMs && bucketFrom < untilMs)
                {
                    total += bucket.RequestCount;
                }
            }

            return total;
        }

        /// <summary>将剩余时间格式化为「X 天 / X 小时 Y 分钟 / X 分钟 / X 秒」。</summary>
        public static string FormatRemaining(TimeSpan remaining)
        {
            if (remaining < TimeSpan.Zero)
            {
                remaining = TimeSpan.Zero;
            }

            if (remaining.TotalDays >= 1)
            {
                return Loc.T("Time.Days", (int)remaining.TotalDays);
            }

            if (remaining.TotalHours >= 1)
            {
                var hours = (int)remaining.TotalHours;
                var minutes = remaining.Minutes;
                return minutes > 0
                    ? Loc.T("Time.HoursMinutes", hours, minutes)
                    : Loc.T("Time.Hours", hours);
            }

            if (remaining.TotalMinutes >= 1)
            {
                return Loc.T("Time.Minutes", (int)remaining.TotalMinutes);
            }

            return Loc.T("Time.Seconds", (int)Math.Max(0, remaining.TotalSeconds));
        }
    }
}
