using System;

namespace Vancat.OllamaCloudUsage.Services
{
    /// <summary>
    /// 重置时间计算：会话窗口按 5 小时 UTC 边界（00/05/10/15/20 UTC）对齐；
    /// 每周窗口锚定到周一 00:00 UTC。
    /// </summary>
    public static class ResetTime
    {
        private const long SessionWindowMs = 5L * 3600 * 1000;
        private const long WeekMs = 7L * 86400 * 1000;

        // Unix 纪元（1970-01-01）是星期四，周一锚点需向前偏移 4 天。
        private const long WeekAnchorOffsetMs = 4L * 86400 * 1000;

        public static DateTimeOffset NextSessionReset(DateTimeOffset now)
        {
            var epochMs = now.ToUnixTimeMilliseconds();
            var nextMs = epochMs + (SessionWindowMs - (epochMs % SessionWindowMs));
            return DateTimeOffset.FromUnixTimeMilliseconds(nextMs);
        }

        public static DateTimeOffset NextWeeklyReset(DateTimeOffset now)
        {
            var epochMs = now.ToUnixTimeMilliseconds();
            var offset = (epochMs - WeekAnchorOffsetMs) % WeekMs;
            if (offset < 0)
            {
                offset += WeekMs;
            }

            var nextMs = epochMs + (WeekMs - offset);
            return DateTimeOffset.FromUnixTimeMilliseconds(nextMs);
        }

        /// <summary>将剩余时间格式化（跟随当前界面语言）。</summary>
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
