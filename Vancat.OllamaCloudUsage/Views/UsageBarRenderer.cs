using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Shapes;
using Vancat.OllamaCloudUsage.Services;

namespace Vancat.OllamaCloudUsage.Views
{
    /// <summary>
    /// 用量条与图表的共用渲染逻辑（状态栏弹窗与工具窗口共用）。
    ///
    /// 新版 API 不再提供模型维度数据，用量条改为**单色分级**：
    /// 蓝色（&lt;75%）→ 琥珀色（≥75%）→ 红色（≥90%）。
    /// </summary>
    internal static class UsageBarRenderer
    {
        /// <summary>用量条分级配色阈值。</summary>
        private const double AmberThreshold = 0.75;
        private const double RedThreshold = 0.9;

        /// <summary>迷你图表的字符高度级别（与 VS Code 版一致的 8 级）。</summary>
        private static readonly string[] SparkChars = { "▁", "▂", "▃", "▄", "▅", "▆", "▇", "█" };

        /// <summary>描述文字画刷（灰）。</summary>
        public static readonly SolidColorBrush MutedBrush = Frozen("#FF888888");

        /// <summary>错误文字画刷（红）。</summary>
        public static readonly SolidColorBrush ErrorBrush = Frozen("#FFE51400");

        /// <summary>用量条底色（半透明灰）。</summary>
        public static readonly SolidColorBrush BarTrackBrush = Frozen("#33808080");

        /// <summary>
        /// 用量条颜色：舒适区为蓝色，超过 75% 转琥珀色，超过 90% 转红色。
        /// </summary>
        public static Color UsageColor(double usedFraction)
        {
            if (usedFraction >= RedThreshold)
            {
                return Color.FromRgb(0xE5, 0x53, 0x4B);
            }

            if (usedFraction >= AmberThreshold)
            {
                return Color.FromRgb(0xD2, 0x99, 0x22);
            }

            return Color.FromRgb(0x25, 0x63, 0xEB);
        }

        public static SolidColorBrush UsageBrush(double usedFraction)
        {
            var brush = new SolidColorBrush(UsageColor(usedFraction));
            brush.Freeze();
            return brush;
        }

        private static SolidColorBrush Frozen(string hex)
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
            brush.Freeze();
            return brush;
        }

        /// <summary>
        /// 绘制单色分级用量条：已填充部分 = 窗口用量（按 75%/90% 阈值换色）。
        /// </summary>
        public static void RenderBar(Grid host, double usedFraction)
        {
            host.ColumnDefinitions.Clear();
            host.Children.Clear();

            var filled = Math.Max(0, Math.Min(1, usedFraction));

            host.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = new GridLength(Math.Max(filled, 0.0001), GridUnitType.Star),
            });
            host.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = new GridLength(Math.Max(1 - filled, 0.0001), GridUnitType.Star),
            });

            if (filled <= 0)
            {
                return;
            }

            var fill = new Border { Background = UsageBrush(filled) };
            Grid.SetColumn(fill, 0);
            host.Children.Add(fill);
        }

        /// <summary>
        /// 绘制信用计划的额度进度条（已用比例按同一分级配色）。
        /// </summary>
        public static void RenderCreditsBar(Grid host, double usedFraction)
        {
            RenderBar(host, usedFraction);
        }

        /// <summary>
        /// 把请求历史分桶渲染为一行迷你柱状图（按最大值归一化）。
        /// </summary>
        public static string Sparkline(IList<UsageBucket> buckets)
        {
            if (buckets == null || buckets.Count == 0)
            {
                return string.Empty;
            }

            var max = 0L;
            foreach (var bucket in buckets)
            {
                if (bucket.RequestCount > max)
                {
                    max = bucket.RequestCount;
                }
            }

            if (max <= 0)
            {
                return Repeat(SparkChars[0], buckets.Count);
            }

            var builder = new System.Text.StringBuilder(buckets.Count);
            foreach (var bucket in buckets)
            {
                var level = (int)((double)bucket.RequestCount / max * SparkChars.Length);
                if (level < 0)
                {
                    level = 0;
                }
                else if (level >= SparkChars.Length)
                {
                    level = SparkChars.Length - 1;
                }

                builder.Append(SparkChars[level]);
            }

            return builder.ToString();
        }

        private static string Repeat(string text, int count)
        {
            var builder = new System.Text.StringBuilder(text.Length * Math.Max(0, count));
            for (var i = 0; i < count; i++)
            {
                builder.Append(text);
            }

            return builder.ToString();
        }

        /// <summary>
        /// 让文本块使用等宽数字（tabular figures），使各行数字宽度一致，
        /// 配合共享尺寸组实现整齐的纵向对齐。字体不支持时静默忽略。
        /// </summary>
        public static void UseTabularNumbers(TextBlock text)
        {
            try
            {
                Typography.SetNumeralAlignment(text, FontNumeralAlignment.Tabular);
            }
            catch
            {
                // 某些字体/环境不支持 OpenType 数字对齐特性。
            }
        }
    }
}
