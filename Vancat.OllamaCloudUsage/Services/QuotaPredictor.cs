using System;

namespace Vancat.OllamaCloudUsage.Services
{
    /// <summary>
    /// 剩余请求次数预测器。
    ///
    /// 公式（窗口级）：
    ///     窗口可用总次数 = 窗口总请求数 ÷ 窗口用量比例
    ///     剩余可请求次数 = 窗口可用总次数 − 窗口总请求数
    ///                    = 总请求数 × (1 − usage) ÷ usage
    ///
    /// 例：5 小时窗口请求 102 次、已用 6% → 102 / 0.06 = 1700（窗口容量），
    /// 1700 − 102 = 1598（还能再请求约 1598 次）。
    ///
    /// 说明：该估算假定「继续按当前窗口的平均消耗水平使用」；usage 用的是 API
    /// 返回的精确比例（非界面四舍五入后的百分比）。
    /// </summary>
    public static class QuotaPredictor
    {
        /// <summary>预测显示上限：超过该值按上限显示，避免极小用量下的天文数字。</summary>
        public const long MaxEstimate = 999999L;

        /// <summary>
        /// 预测该窗口剩余还可请求的次数（窗口级，与具体模型无关）。
        /// 返回 null 表示数据不足以预测（窗口尚无请求记录或用量为 0）。
        /// </summary>
        public static long? EstimateRemainingRequests(LimitUsage limit)
        {
            if (limit == null)
            {
                return null;
            }

            var usage = limit.Usage;
            if (usage <= 0)
            {
                return null;
            }

            if (usage >= 1)
            {
                return 0;
            }

            var totalRequests = 0L;
            foreach (var model in limit.Models)
            {
                totalRequests += model.RequestCount;
            }

            if (totalRequests <= 0)
            {
                return null;
            }

            // 总请求数 / usage − 总请求数
            var estimate = totalRequests / usage - totalRequests;
            if (double.IsNaN(estimate) || double.IsInfinity(estimate) || estimate < 0)
            {
                return null;
            }

            return (long)Math.Min(estimate, MaxEstimate);
        }

        /// <summary>
        /// 预测单个模型在该窗口剩余还可请求的次数。
        ///
        /// 公式：模型剩余 = 窗口总容量 − 该模型已用次数
        ///               = 窗口总请求数 ÷ usage − 该模型请求数
        ///
        /// 为什么不是「模型请求数 ÷ usage − 模型请求数」：那样等于假设该模型的
        /// 请求数消耗了整个窗口的用量。例如 glm 只用 3 次（占周请求 0.14%），
        /// 却会算出「剩余 22 次」，明显荒谬。按等单价分摊时，每个模型单独使用
        /// 的窗口总容量都相同（= 总请求数 ÷ usage），差别只在于各自已用掉多少。
        /// 返回 null 表示数据不足以预测（窗口用量为 0 或该模型无请求记录）。
        /// </summary>
        public static long? EstimateRemainingRequestsForModel(LimitUsage limit, long modelRequestCount)
        {
            if (limit == null || modelRequestCount <= 0)
            {
                return null;
            }

            var usage = limit.Usage;
            if (usage <= 0)
            {
                return null;
            }

            if (usage >= 1)
            {
                return 0;
            }

            var totalRequests = 0L;
            foreach (var model in limit.Models)
            {
                totalRequests += model.RequestCount;
            }

            if (totalRequests <= 0)
            {
                return null;
            }

            // 窗口总容量 − 该模型已用次数
            var estimate = totalRequests / usage - modelRequestCount;
            if (double.IsNaN(estimate) || double.IsInfinity(estimate) || estimate < 0)
            {
                return null;
            }

            return (long)Math.Min(estimate, MaxEstimate);
        }

        /// <summary>
        /// 计算该窗口的总容量（按当前平均消耗水平，重置前总共可请求的次数）。
        /// = 窗口总请求数 ÷ usage。返回 null 表示数据不足。
        /// </summary>
        public static long? WindowCapacity(LimitUsage limit)
        {
            if (limit == null)
            {
                return null;
            }

            var usage = limit.Usage;
            if (usage <= 0)
            {
                return null;
            }

            var totalRequests = 0L;
            foreach (var model in limit.Models)
            {
                totalRequests += model.RequestCount;
            }

            if (totalRequests <= 0)
            {
                return null;
            }

            var capacity = totalRequests / usage;
            if (double.IsNaN(capacity) || double.IsInfinity(capacity) || capacity < 0)
            {
                return null;
            }

            return (long)Math.Min(capacity, MaxEstimate);
        }

        /// <summary>
        /// 计算某模型的请求数占窗口总请求数的比例（用于界面说明）。
        /// 返回 null 表示窗口无请求记录。
        /// </summary>
        public static double? RequestShare(LimitUsage limit, long modelRequestCount)
        {
            if (limit == null || modelRequestCount <= 0)
            {
                return null;
            }

            var totalRequests = 0L;
            foreach (var model in limit.Models)
            {
                totalRequests += model.RequestCount;
            }

            return totalRequests > 0 ? (double)modelRequestCount / totalRequests : (double?)null;
        }

        /// <summary>
        /// 计算某模型占窗口总配额的比例（窗口占用比例）。
        /// = 窗口用量 × 该模型请求占比，例如周窗口已用 11.9%、某模型占请求 58.1%
        /// → 该模型占用窗口配额 6.9%。
        /// 返回 null 表示窗口无用量或该模型无请求记录。
        /// </summary>
        public static double? ModelWindowShare(LimitUsage limit, long modelRequestCount)
        {
            if (limit == null || limit.Usage <= 0)
            {
                return null;
            }

            var share = RequestShare(limit, modelRequestCount);
            return share.HasValue ? limit.Usage * share.Value : (double?)null;
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
