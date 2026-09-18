using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Vancat.OllamaCloudUsage.Services;

namespace Vancat.OllamaCloudUsage.Views
{
    /// <summary>用量条与模型列表的共用渲染逻辑（状态栏弹窗与工具窗口共用）。</summary>
    internal static class UsageBarRenderer
    {
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

        /// <summary>渲染模型请求列表（色点 + 名称 + 次数）。</summary>
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

            for (var i = 0; i < limit.Models.Count; i++)
            {
                var model = limit.Models[i];
                var row = new DockPanel { Margin = new Thickness(0, 1.5, 0, 1.5) };

                var count = new TextBlock
                {
                    Text = Loc.T("Models.Requests", model.RequestCount.ToString("N0")),
                    FontSize = fontSize,
                    Foreground = MutedBrush,
                    VerticalAlignment = VerticalAlignment.Center,
                };
                DockPanel.SetDock(count, Dock.Right);
                row.Children.Add(count);

                row.Children.Add(new Ellipse
                {
                    Width = 7,
                    Height = 7,
                    Margin = new Thickness(0, 0, 6, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    Fill = BrushFor(i),
                });

                row.Children.Add(new TextBlock
                {
                    Text = model.Name,
                    FontSize = fontSize,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    VerticalAlignment = VerticalAlignment.Center,
                });

                host.Children.Add(row);
            }
        }
    }
}
