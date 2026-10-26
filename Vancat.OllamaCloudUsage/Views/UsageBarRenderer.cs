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
    /// 用量条两种形态：
    /// - **按模型分段堆叠**（`group_by=model` 有数据时）：蓝色调色板，每模型一段；
    /// - **单色分级**（无模型数据时）：蓝色（&lt;75%）→ 琥珀色（≥75%）→ 红色（≥90%）。
    /// </summary>
    internal static class UsageBarRenderer
    {
        /// <summary>用量条分级配色阈值。</summary>
        private const double AmberThreshold = 0.75;
        private const double RedThreshold = 0.9;

        /// <summary>模型分段条的蓝色调色板（按索引分配）。</summary>
        private static readonly string[] Palette =
        {
            "#2563EB", "#3B82F6", "#4F46E5", "#60A5FA", "#1D4ED8", "#6366F1", "#818CF8", "#93C5FD",
        };

        /// <summary>模型列表各数据列之间的间距。</summary>
        private const double ColumnGap = 8;

        /// <summary>迷你图表的字符高度级别（与 VS Code 版一致的 8 级）。</summary>
        private static readonly string[] SparkChars = { "▁", "▂", "▃", "▄", "▅", "▆", "▇", "█" };

        /// <summary>图表柱子的默认高度（像素）。</summary>
        public const double ChartHeight = 26;

        /// <summary>图表柱子的最大宽度（像素），避免分桶少时柱子过粗。</summary>
        public const double ChartBarMaxWidth = 24;

        /// <summary>图表柱子的颜色（比用量条稍柔和的蓝）。</summary>
        private static readonly SolidColorBrush ChartBarBrush = Frozen("#FF4A7DE8");

        /// <summary>零值柱的基线色（半透明灰）。</summary>
        private static readonly SolidColorBrush ChartZeroBrush = Frozen("#40808080");

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

        /// <summary>模型分段条的调色板画刷（按索引循环取色）。</summary>
        public static SolidColorBrush ModelBrush(int index)
        {
            return Frozen(Palette[index % Palette.Length]);
        }

        /// <summary>
        /// 绘制**按模型分段堆叠**的用量条：已填充部分（= 窗口用量）按各模型
        /// 请求占比切分，每段一种蓝色调。
        ///
        /// 无模型数据时自动退回单色分级条。
        ///
        /// 列宽算法（务必保证总和 = 1，否则填充比例失真）：
        ///   每段 = (模型请求数 ÷ 总请求数) × 已用比例
        ///   剩余 = 1 − 已用比例
        /// </summary>
        public static void RenderSegmentedBar(Grid host, WindowSummary summary)
        {
            host.ColumnDefinitions.Clear();
            host.Children.Clear();

            var filled = Math.Max(0, Math.Min(1, summary?.Used ?? 0));
            var models = summary?.Models;

            var total = 0L;
            if (models != null)
            {
                foreach (var model in models)
                {
                    total += model.RequestCount;
                }
            }

            // 无模型数据或零用量 → 单色分级条。
            if (models == null || models.Count == 0 || total <= 0 || filled <= 0)
            {
                RenderBar(host, filled);
                return;
            }

            for (var i = 0; i < models.Count; i++)
            {
                var model = models[i];
                host.ColumnDefinitions.Add(new ColumnDefinition
                {
                    Width = new GridLength((double)model.RequestCount / total * filled, GridUnitType.Star),
                });

                var segment = new Border
                {
                    Background = ModelBrush(i),
                    ToolTip = model.Name + "\n" + Loc.T("Models.Requests", model.RequestCount.ToString("N0")),
                };
                Grid.SetColumn(segment, i);
                host.Children.Add(segment);
            }

            // 未使用部分（灰底由外层容器提供）。
            host.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = new GridLength(Math.Max(1 - filled, 0.0001), GridUnitType.Star),
            });
        }

        /// <summary>
        /// 渲染模型明细列表：色点 + 名称 + 占窗口配额比例 + 请求数 + 剩余预测。
        ///
        /// 各数据列使用共享尺寸组，保证多行纵向对齐。
        /// </summary>
        public static void RenderModelList(StackPanel host, WindowSummary summary, double fontSize = 11)
        {
            host.Children.Clear();

            var models = summary?.Models;
            if (models == null || models.Count == 0)
            {
                host.Children.Add(new TextBlock
                {
                    Text = Loc.T("Models.None"),
                    FontSize = fontSize,
                    Foreground = MutedBrush,
                });
                return;
            }

            // 共享尺寸作用域：让各行的「占窗口 / 次数 / 剩余」列取相同宽度。
            Grid.SetIsSharedSizeScope(host, true);

            var capacity = QuotaPredictor.WindowCapacity(summary.Requests, summary.Used);

            for (var i = 0; i < models.Count; i++)
            {
                var model = models[i];
                var row = new Grid { Margin = new Thickness(0, 1.5, 0, 1.5) };

                // 列：色点 | 名称(*) | 占窗口 | 次数 | 剩余
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto, SharedSizeGroup = "ModelShare" });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto, SharedSizeGroup = "ModelCount" });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto, SharedSizeGroup = "ModelRemain" });

                var dot = new Ellipse
                {
                    Width = 7,
                    Height = 7,
                    Margin = new Thickness(0, 0, 6, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    Fill = ModelBrush(i),
                };
                Grid.SetColumn(dot, 0);
                row.Children.Add(dot);

                var name = new TextBlock
                {
                    Text = model.Name,
                    FontSize = fontSize,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    VerticalAlignment = VerticalAlignment.Center,
                };
                Grid.SetColumn(name, 1);
                row.Children.Add(name);

                // 占窗口配额比例（窗口用量 × 该模型请求占比）。
                var windowShare = QuotaPredictor.ModelWindowShare(summary.Used, summary.Requests, model.RequestCount);
                if (windowShare.HasValue)
                {
                    var share = new TextBlock
                    {
                        Text = Loc.T("Models.WindowShare", Config.FormatSharePercent(windowShare.Value)),
                        FontSize = fontSize,
                        Foreground = MutedBrush,
                        VerticalAlignment = VerticalAlignment.Center,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Margin = new Thickness(ColumnGap, 0, 0, 0),
                        ToolTip = Loc.T("Models.WindowShareTip",
                            Config.FormatSharePercent(windowShare.Value),
                            Config.FormatUsagePercent(summary.Used)),
                    };
                    Grid.SetColumn(share, 2);
                    UseTabularNumbers(share);
                    row.Children.Add(share);
                }

                var count = new TextBlock
                {
                    Text = Loc.T("Models.Requests", model.RequestCount.ToString("N0")),
                    FontSize = fontSize,
                    Foreground = MutedBrush,
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Margin = new Thickness(ColumnGap, 0, 0, 0),
                };
                Grid.SetColumn(count, 3);
                UseTabularNumbers(count);
                row.Children.Add(count);

                var estimate = QuotaPredictor.EstimateRemainingRequestsForModel(
                    model.RequestCount, summary.Requests, summary.Used);
                if (estimate.HasValue)
                {
                    var remain = new TextBlock
                    {
                        Text = Loc.T("Models.Remaining", QuotaPredictor.Format(estimate.Value)),
                        FontSize = fontSize,
                        Foreground = MutedBrush,
                        VerticalAlignment = VerticalAlignment.Center,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Margin = new Thickness(ColumnGap, 0, 0, 0),
                        ToolTip = Loc.T("Models.RemainingTip",
                            QuotaPredictor.Format(capacity ?? 0),
                            QuotaPredictor.Format(estimate.Value)),
                    };
                    Grid.SetColumn(remain, 4);
                    UseTabularNumbers(remain);
                    row.Children.Add(remain);
                }

                host.Children.Add(row);
            }
        }

        /// <summary>
        /// 把请求历史分桶渲染为**矩形柱状图**（底部对齐）。
        ///
        /// 不用 Unicode 块字符（▁▂▃▄▅▆▇█）的原因：块字符的高度依赖字体的
        /// 基线取整，不同字号/字体下会出现底部参差不齐；用真实矩形绘制可保证
        /// 所有柱子底部严格对齐，且高度可像素级精确控制。
        /// </summary>
        /// <param name="host">承载图表的 Grid（列宽按分桶数等分）。</param>
        /// <param name="buckets">请求历史分桶。</param>
        /// <param name="height">图表高度（像素），默认 26。</param>
        public static void RenderChart(Grid host, IList<UsageBucket> buckets, double height = ChartHeight)
        {
            host.ColumnDefinitions.Clear();
            host.Children.Clear();

            if (buckets == null || buckets.Count == 0)
            {
                return;
            }

            var max = 0L;
            foreach (var bucket in buckets)
            {
                if (bucket.RequestCount > max)
                {
                    max = bucket.RequestCount;
                }
            }

            for (var i = 0; i < buckets.Count; i++)
            {
                host.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                var count = buckets[i].RequestCount;
                double barHeight;
                Brush brush;

                if (count <= 0 || max <= 0)
                {
                    // 零值：仅显示一条细基线，保持视觉连续性。
                    barHeight = 1.5;
                    brush = ChartZeroBrush;
                }
                else
                {
                    // 非零值至少 2px，避免小用量柱消失。
                    barHeight = Math.Max(2, (double)count / max * height);
                    brush = ChartBarBrush;
                }

                var bar = new Border
                {
                    Height = barHeight,
                    MaxWidth = ChartBarMaxWidth,

                    // 关键：必须用 Stretch（而非 Center）！
                    // Border 没有内容时，Center 会按「内容宽度」布局 = 0px，柱子完全不可见。
                    // Stretch 会填满列宽，再受 MaxWidth 限制后自动居中。
                    VerticalAlignment = VerticalAlignment.Bottom,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    Background = brush,
                    Margin = new Thickness(1, 0, 1, 0),
                    CornerRadius = new CornerRadius(1),
                    ToolTip = Loc.T("Usage.Requests", count.ToString("N0")),
                };

                Grid.SetColumn(bar, i);
                host.Children.Add(bar);
            }
        }

        /// <summary>
        /// 把请求历史分桶渲染为一行迷你柱状图（按最大值归一化）。
        /// 文本模式，保留作为 tooltip 等场景的备用；界面请用 <see cref="RenderChart"/>。
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
