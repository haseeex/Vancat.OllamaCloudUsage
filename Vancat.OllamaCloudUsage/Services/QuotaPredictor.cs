using System;
using System.Collections.Generic;

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

        /// <summary>窗口内各模型的请求数，按请求数降序排列。</summary>
        public List<ModelUsage> Models { get; set; } = new List<ModelUsage>();
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
        /// 结合 balance 窗口与用量分桶，汇总出窗口的用量、请求数、重置时间、
        /// 剩余预测与模型明细。
        /// </summary>
        public static WindowSummary SummarizeWindow(
            double remainingPercent,
            string resetsAt,
            long windowMs,
            IList<UsageBucket> buckets)
        {
            var resetMs = ResetTime.ParseTimestampMs(resetsAt);
            var used = UsedFraction(remainingPercent);

            long requests = 0;
            List<ModelUsage> models = null;
            if (resetMs > 0)
            {
                var fromMs = ResetTime.WindowStartMs(resetMs, windowMs);
                requests = ResetTime.SumRequestsInRange(buckets, fromMs, resetMs);
                models = ResetTime.SumModelUsageInRange(buckets, fromMs, resetMs);
            }

            return new WindowSummary
            {
                Used = used,
                Requests = requests,
                ResetMs = resetMs,
                Estimate = EstimateRemainingRequests(requests, used),
                Models = models ?? new List<ModelUsage>(),
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

        /// <summary>
        /// 窗口容量：按当前平均消耗水平，重置前总共可请求的次数
        /// （= 窗口请求数 ÷ 已用比例）。返回 null 表示数据不足。
        /// </summary>
        public static long? WindowCapacity(long requestsInWindow, double usedFraction)
        {
            if (requestsInWindow <= 0 || usedFraction <= 0)
            {
                return null;
            }

            var capacity = requestsInWindow / usedFraction;
            if (double.IsNaN(capacity) || double.IsInfinity(capacity) || capacity < 0)
            {
                return null;
            }

            return (long)Math.Min(capacity, MaxEstimate);
        }

        /// <summary>
        /// 预测单个模型在窗口内剩余还可请求的次数（假设仅使用该模型）。
        ///
        /// 公式：窗口容量 − 该模型已用次数
        ///
        /// 为什么不是「模型请求数 ÷ used − 模型请求数」：那样等于假设该模型的
        /// 请求数消耗了整个窗口的用量。例如 glm 只用 3 次（占周请求 0.14%），
        /// 却会算出「剩余 22 次」，明显荒谬。按等单价分摊时，每个模型单独使用
        /// 的窗口总容量都相同（= 窗口请求数 ÷ used），差别只在于各自已用掉多少。
        /// </summary>
        public static long? EstimateRemainingRequestsForModel(
            long modelRequestCount, long requestsInWindow, double usedFraction)
        {
            if (modelRequestCount <= 0)
            {
                return null;
            }

            var capacity = WindowCapacity(requestsInWindow, usedFraction);
            if (!capacity.HasValue)
            {
                return null;
            }

            if (usedFraction >= 1)
            {
                return 0;
            }

            var remaining = capacity.Value - modelRequestCount;
            return remaining < 0 ? 0 : remaining;
        }

        /// <summary>
        /// 某模型占窗口总配额的比例 = 窗口用量 × 该模型的请求占比。
        ///
        /// 例：周窗口已用 11.9%、某模型占请求 58.1% → 该模型占用窗口配额 6.9%。
        /// 返回 null 表示窗口无用量或该模型无请求记录。
        /// </summary>
        public static double? ModelWindowShare(double usedFraction, long requestsInWindow, long modelRequestCount)
        {
            if (usedFraction <= 0)
            {
                return null;
            }

            var share = RequestShare(modelRequestCount, requestsInWindow);
            return share.HasValue ? usedFraction * share.Value : (double?)null;
        }

        /// <summary>
        /// 请求数占比（某模型/分桶占窗口总请求数的比例）。
        /// 返回 null 表示窗口无请求记录。
        /// </summary>
        public static double? RequestShare(long part, long total)
        {
            if (total <= 0 || part <= 0)
            {
                return null;
            }

            return (double)part / total;
        }

        /// <summary>把预测值格式化为界面文本（千分位；达到上限时追加 + 号）。</summary>
        public static string Format(long estimate)
        {
            return estimate >= MaxEstimate
                ? estimate.ToString("N0") + "+"
                : estimate.ToString("N0");
        }
    }
}
