using System;

namespace Vancat.OllamaCloudUsage.Services
{
    /// <summary>
    /// 一个配额窗口的汇总：由 balance 端点的百分比 + 重置时间，
    /// 结合匹配的用量分桶（小时/每日）计算得出。
    /// </summary>
    public sealed class WindowSummary
    {
        /// <summary>已用比例（0–1），由 balance 的 `remaining_percent` 换算。</summary>
        public double Used { get; set; }

        /// <summary>当前窗口内的请求数（来自匹配的分桶区间）。</summary>
        public long Requests { get; set; }

        /// <summary>下次重置的 epoch 毫秒；时间不可用时为 0。</summary>
        public long ResetMs { get; set; }

        /// <summary>按当前速度估算的剩余可请求次数；无法计算时为 null。</summary>
        public long? Estimate { get; set; }
    }

    /// <summary>
    /// 剩余请求次数预测器。
    ///
    /// 公式（窗口级）：
    ///     窗口可用总次数 = 窗口总请求数 ÷ 窗口用量比例
    ///     剩余可请求次数 = 窗口可用总次数 − 窗口总请求数
    ///                    = 总请求数 × (1 − used) ÷ used
    ///
    /// 例：5 小时窗口请求 102 次、已用 6% → 102 / 0.06 = 1700（窗口容量），
    /// 1700 − 102 = 1598（还能再请求约 1598 次）。
    ///
    /// 说明：该估算假定「继续按当前窗口的平均消耗水平使用」。
    /// </summary>
    public static class QuotaPredictor
    {
        /// <summary>预测显示上限：超过该值按上限显示，避免极小用量下的天文数字。</summary>
        public const long MaxEstimate = 999999L;

        /// <summary>由 balance 的剩余百分比换算已用比例（0–1）。</summary>
        public static double UsedFraction(double remainingPercent)
        {
            return Math.Max(0, Math.Min(1, (100 - remainingPercent) / 100));
        }

        /// <summary>
        /// 结合 balance 窗口与用量分桶，汇总出窗口的用量、请求数、重置时间与剩余预测。
        /// </summary>
        public static WindowSummary SummarizeWindow(
            double remainingPercent,
            string resetsAt,
            long windowMs,
            System.Collections.Generic.IList<UsageBucket> buckets)
        {
            var resetMs = ResetTime.ParseTimestampMs(resetsAt);
            var used = UsedFraction(remainingPercent);
            var requests = resetMs > 0
                ? ResetTime.SumRequestsInRange(buckets, ResetTime.WindowStartMs(resetMs, windowMs), resetMs)
                : 0;

            return new WindowSummary
            {
                Used = used,
                Requests = requests,
                ResetMs = resetMs,
                Estimate = EstimateRemainingRequests(requests, used),
            };
        }

        /// <summary>
        /// 预测该窗口剩余还可请求的次数。
        /// 返回 null 表示数据不足以预测（窗口尚无请求记录或用量为 0）。
        /// </summary>
        public static long? EstimateRemainingRequests(long requestsInWindow, double usedFraction)
        {
            if (requestsInWindow <= 0)
            {
                return null;
            }

            if (usedFraction <= 0)
            {
                return null;
            }

            if (usedFraction >= 1)
            {
                return 0;
            }

            var estimate = requestsInWindow * (1 - usedFraction) / usedFraction;
            if (double.IsNaN(estimate) || double.IsInfinity(estimate) || estimate < 0)
            {
                return null;
            }

            return (long)Math.Min(estimate, MaxEstimate);
        }

        /// <summary>把预测值格式化为界面文本（千分位；达到上限时追加 + 号）。</summary>
        public static string Format(long estimate)
        {
            return estimate >= MaxEstimate
                ? estimate.ToString("N0") + "+"
                : estimate.ToString("N0");
        }

        /// <summary>
        /// 请求数占比（某模型/分桶占窗口总请求数的比例）。
        /// 返回 null 表示窗口无请求记录。
        /// </summary>
        public static double? RequestShare(long part, long total)
        {
            return total > 0 ? (double)part / total : (double?)null;
        }
    }
}
