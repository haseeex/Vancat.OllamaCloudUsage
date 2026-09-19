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
    /// <summary>用量条与模型列表的共用渲染逻辑（状态栏弹窗与工具窗口共用）。</summary>
    internal static class UsageBarRenderer
    {
        /// <summary>模型列表各数据列之间的间距。</summary>
        private const double ColumnGap = 10;

        /// <summary>
        /// 让文本块使用等宽数字（tabular figures），使各行数字宽度一致，
        /// 配合共享尺寸组实现整齐的纵向对齐。字体不支持时静默忽略。
        /// </summary>
        private static void UseTabularNumbers(TextBlock text)
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

        public static readonly string[] Palette =
        {
            "#2563EB", "#3B82F6", "#4F46E5", "#60A5FA", "#1D4ED8", "#6366F1", "#818CF8", "#93C5FD",
        };

        /// <summary>描述文字画刷（灰）。</summary>
        public static readonly SolidColorBrush MutedBrush = Frozen("#FF888888");

        /// <summary>错误文字画刷（红）。</summary>
        public static readonly SolidColorBrush ErrorBrush = Frozen("#FFE51400");

        public static SolidColorBrush BrushFor(int index) => Frozen(Palette[index % Palette.Length]);

        private static SolidColorBrush Frozen(string hex)
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
            brush.Freeze();
            return brush;
        }

        /// <summary>按模型请求数占比绘制用量条（已填充部分 = 窗口用量）。</summary>
        public static void RenderBar(Grid host, LimitUsage limit)
        {
            host.ColumnDefinitions.Clear();
            host.Children.Clear();

            var filled = Math.Max(0, Math.Min(1, limit.Usage));
            var total = 0L;
            foreach (var model in limit.Models)
            {
                total += model.RequestCount;
            }

            if (total <= 0 || filled <= 0)
            {
                // 空条：单个占位列，灰底由外层容器提供。
                host.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                return;
            }

            for (var i = 0; i < limit.Models.Count; i++)
            {
                var model = limit.Models[i];
                host.ColumnDefinitions.Add(new ColumnDefinition
                {
                    Width = new GridLength((double)model.RequestCount / total * filled, GridUnitType.Star),
                });

                var segment = new Border
                {
                    Background = BrushFor(i),
                    ToolTip = $"{model.Name}\n{Loc.T("Models.Requests", model.RequestCount.ToString("N0"))}",
                };
                Grid.SetColumn(segment, i);
                host.Children.Add(segment);
            }

            // 剩余未使用部分占位。
            host.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = new GridLength(Math.Max(1 - filled, 0.0001), GridUnitType.Star),
            });
        }

        /// <summary>
        /// 渲染模型请求列表（色点 + 名称 + 占窗口比例 + 次数 + 剩余预测）。
        /// 各数据列使用共享尺寸组，保证多行纵向对齐。
        /// </summary>
        public static void RenderModelList(StackPanel host, LimitUsage limit, double fontSize = 12)
        {
            host.Children.Clear();

            if (limit.Models.Count == 0)
            {
                host.Children.Add(new TextBlock
                {
                    Text = Loc.T("Models.None"),
                    FontSize = fontSize,
                    Foreground = MutedBrush,
                });
                return;
            }

            // 共享尺寸作用域：让各行的「占窗口 / 次数 / 剩余」列取相同宽度，实现纵向对齐。
            Grid.SetIsSharedSizeScope(host, true);

            for (var i = 0; i < limit.Models.Count; i++)
            {
                var model = limit.Models[i];
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
                    Fill = BrushFor(i),
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

                // 窗口占用比例（该模型占窗口总配额的百分比 = 窗口用量 × 该模型请求占比）。
                var windowShare = QuotaPredictor.ModelWindowShare(limit, model.RequestCount);
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
                            Config.FormatUsagePercent(limit.Usage)),
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

                var estimate = QuotaPredictor.EstimateRemainingRequestsForModel(limit, model.RequestCount);
                if (estimate.HasValue)
                {
                    // 提示里同时给出窗口容量与已用次数，便于核对算法。
                    var capacity = QuotaPredictor.WindowCapacity(limit);
                    var remain = new TextBlock
                    {
                        Text = Loc.T("Models.Remaining", QuotaPredictor.Format(estimate.Value)),
                        FontSize = fontSize,
                        Foreground = MutedBrush,
                        VerticalAlignment = VerticalAlignment.Center,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Margin = new Thickness(ColumnGap, 0, 0, 0),
                        ToolTip = Loc.T("Models.RemainingTip",
                            QuotaPredictor.Format(capacity ?? 0), QuotaPredictor.Format(estimate.Value)),
                    };
                    Grid.SetColumn(remain, 4);
                    UseTabularNumbers(remain);
                    row.Children.Add(remain);
                }

                host.Children.Add(row);
            }
        }
    }
}
