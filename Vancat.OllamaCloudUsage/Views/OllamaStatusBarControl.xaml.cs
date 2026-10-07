using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.VisualStudio.PlatformUI;
using Microsoft.VisualStudio.Shell;
using Vancat.OllamaCloudUsage.Services;

namespace Vancat.OllamaCloudUsage.Views
{
    /// <summary>
    /// 状态栏控件：显示用量文本，悬停展开详细浮窗，点击打开用量面板。
    /// 直接注入 VS 主窗口 WPF 状态栏（不依赖 IVsStatusbar 文本 API），
    /// 因此支持富文本提示与鼠标交互。
    ///
    /// 已适配官方 2026-10-06 新 API：数据来自 /api/balance（权威百分比与
    /// 重置时间）+ /api/usage（请求历史），支持旧版窗口计划与信用计划两种形态。
    /// </summary>
    public partial class OllamaStatusBarControl : UserControl
    {
        private readonly DispatcherTimer _countdownTimer;
        private UsageUpdatedEventArgs _data;

        public OllamaStatusBarControl()
        {
            InitializeComponent();

            ApplyTheme();
            ApplyLanguage();

            _countdownTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _countdownTimer.Tick += (s, e) =>
            {
                UpdateCountdowns();
                if (HoverPopup.IsOpen)
                {
                    BuildHoverContent();
                }
            };
            _countdownTimer.Start();

            VSColorTheme.ThemeChanged += OnThemeChanged;
            Loc.LanguageChanged += OnLanguageChanged;
            Config.DisplayChanged += OnDisplayChanged;
            Unloaded += (s, e) =>
            {
                _countdownTimer.Stop();
                VSColorTheme.ThemeChanged -= OnThemeChanged;
                Loc.LanguageChanged -= OnLanguageChanged;
                Config.DisplayChanged -= OnDisplayChanged;
            };
        }

        /// <summary>用量精度等显示设置变更时按新设置重绘。</summary>
        private void OnDisplayChanged(object sender, EventArgs e)
        {
            Render(_data);
            if (HoverPopup.IsOpen)
            {
                BuildHoverContent();
            }
        }

        /// <summary>语言切换时刷新界面文本。</summary>
        private void OnLanguageChanged(object sender, EventArgs e)
        {
            ApplyLanguage();
            Render(_data);

            if (HoverPopup.IsOpen)
            {
                BuildHoverContent();
            }
        }

        /// <summary>应用当前语言到静态文本。</summary>
        private void ApplyLanguage()
        {
            OpenButton.ToolTip = Loc.T("StatusBar.Tooltip");
        }

        /// <summary>由主包调用以刷新显示数据。</summary>
        public void Render(UsageUpdatedEventArgs data)
        {
            _data = data;

            if (data?.Usage?.Balance == null)
            {
                UsageText.Text = data?.Loading == true
                    ? Loc.T("StatusBar.Loading")
                    : Loc.T("StatusBar.NoData");
            }
            else
            {
                UsageText.Text = BuildStatusText(data.Usage);
            }

            if (HoverPopup.IsOpen)
            {
                BuildHoverContent();
            }
        }

        /// <summary>
        /// 状态栏文本：旧版计划显示 5 小时/每周已用百分比；
        /// 信用计划显示额度已用百分比。
        /// </summary>
        private static string BuildStatusText(UsageSnapshot snapshot)
        {
            var balance = snapshot.Balance;
            if (balance.IsLegacy)
            {
                var sessionUsed = QuotaPredictor.UsedFraction(balance.Legacy.Session.RemainingPercent);
                var weeklyUsed = QuotaPredictor.UsedFraction(balance.Legacy.Weekly.RemainingPercent);
                return Loc.T("StatusBar.Text",
                    Config.FormatUsagePercent(sessionUsed),
                    Config.FormatUsagePercent(weeklyUsed));
            }

            var credits = balance.Credits;
            var used = credits.AllowanceUsd > 0
                ? Math.Max(0, Math.Min(1, (credits.AllowanceUsd - credits.BalanceUsd) / credits.AllowanceUsd))
                : 0;
            return Loc.T("StatusBar.Credits", Config.FormatUsagePercent(used));
        }

        // ---- 主题 ----

        private void OnThemeChanged(ThemeChangedEventArgs e) => ApplyTheme();

        private void ApplyTheme()
        {
            try
            {
                var bg = VSColorTheme.GetThemedColor(EnvironmentColors.ToolWindowBackgroundColorKey);
                var luminance = (bg.R * 0.299 + bg.G * 0.587 + bg.B * 0.114) / 255.0;
                var isDark = luminance < 0.5;

                // 悬停高亮：深色主题用白色低透明，浅色主题用黑色低透明。
                var hoverColor = isDark
                    ? Color.FromArgb(31, 255, 255, 255)
                    : Color.FromArgb(31, 0, 0, 0);

                var hoverBrush = new SolidColorBrush(hoverColor);
                hoverBrush.Freeze();
                Resources["HoverBackground"] = hoverBrush;

                // 浮窗配色：用环境色模拟工具提示外观。
                var panelBg = ToMediaColor(VSColorTheme.GetThemedColor(EnvironmentColors.ToolWindowBackgroundColorKey));
                var panelFg = ToMediaColor(VSColorTheme.GetThemedColor(EnvironmentColors.ToolWindowTextColorKey));
                var borderColor = ToMediaColor(VSColorTheme.GetThemedColor(EnvironmentColors.ToolWindowBorderColorKey));

                HoverBorder.BorderBrush = new SolidColorBrush(borderColor);
                HoverBorder.Background = new SolidColorBrush(panelBg);

                // 状态栏文本跟随环境前景色。
                var statusFg = ToMediaColor(VSColorTheme.GetThemedColor(EnvironmentColors.StatusBarTextColorKey));
                Foreground = new SolidColorBrush(statusFg);

                // 浮窗内文字颜色（TextElement.Foreground 是附加属性，可作用于容器）。
                TextElement.SetForeground(HoverContent, new SolidColorBrush(panelFg));
            }
            catch
            {
                // 主题服务不可用时保持默认。
            }
        }

        private static Color ToMediaColor(System.Drawing.Color color)
        {
            return Color.FromArgb(color.A, color.R, color.G, color.B);
        }

        // ---- 悬停浮窗 ----

        private void OnMainEnter(object sender, MouseEventArgs e)
        {
            MainArea.Background = (Brush)Resources["HoverBackground"];
            BuildHoverContent();
            HoverPopup.IsOpen = true;
        }

        private void OnMainLeave(object sender, MouseEventArgs e)
        {
            MainArea.Background = Brushes.Transparent;
            HoverPopup.IsOpen = false;
        }

        private void OnButtonEnter(object sender, MouseEventArgs e)
        {
            OpenButton.Background = (Brush)Resources["HoverBackground"];
        }

        private void OnButtonLeave(object sender, MouseEventArgs e)
        {
            OpenButton.Background = Brushes.Transparent;
        }

        private void BuildHoverContent()
        {
            HoverContent.Children.Clear();

            var data = _data;
            if (data?.Usage?.Balance == null)
            {
                HoverContent.Children.Add(new TextBlock
                {
                    Text = data?.Error ?? Loc.T("Hover.NoData"),
                    FontSize = 11,
                    TextWrapping = TextWrapping.Wrap,
                    MaxWidth = 260,
                    Foreground = data?.Error != null ? UsageBarRenderer.ErrorBrush : UsageBarRenderer.MutedBrush,
                });
                return;
            }

            var snapshot = data.Usage;

            // 标题
            HoverContent.Children.Add(new TextBlock
            {
                Text = Loc.T("Hover.Title"),
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 8),
            });

            if (snapshot.Balance.IsLegacy)
            {
                var session = QuotaPredictor.SummarizeWindow(
                    snapshot.Balance.Legacy.Session.RemainingPercent,
                    snapshot.Balance.Legacy.Session.ResetsAt,
                    ResetTime.SessionWindowMs,
                    snapshot.Hourly.Buckets);

                var weekly = QuotaPredictor.SummarizeWindow(
                    snapshot.Balance.Legacy.Weekly.RemainingPercent,
                    snapshot.Balance.Legacy.Weekly.ResetsAt,
                    ResetTime.WeekMs,
                    snapshot.Daily.Buckets);

                AddWindowSection(Loc.T("Hover.SessionWindow"), session, isFirst: true);
                AddWindowSection(Loc.T("Hover.WeeklyWindow"), weekly, isFirst: false);
            }
            else
            {
                AddCreditsSection(snapshot.Balance, isFirst: true);
            }

            // 请求历史图表
            AddChartSection(snapshot);

            // 底部信息
            var refreshPart = Loc.T("Hover.Refresh", Config.FormatInterval(data.IntervalSeconds));
            var footer = data.LastUpdatedMs > 0
                ? refreshPart + Loc.T("Hover.LastUpdated", DateTimeOffset.FromUnixTimeMilliseconds(data.LastUpdatedMs).ToLocalTime().ToString("HH:mm:ss"))
                : refreshPart;

            HoverContent.Children.Add(new TextBlock
            {
                Text = footer + Loc.T("Hover.ClickToOpen"),
                FontSize = 10,
                Margin = new Thickness(0, 8, 0, 0),
                Foreground = UsageBarRenderer.MutedBrush,
                TextWrapping = TextWrapping.Wrap,
            });
        }

        /// <summary>窗口区块：标题 + 百分比 + 剩余预测 + 用量条 + 重置倒计时 + 请求数。</summary>
        private void AddWindowSection(string title, WindowSummary summary, bool isFirst)
        {
            AddSeparator(isFirst);

            // 标题行：名称 + 百分比（+ 剩余次数预测）
            var head = new DockPanel { Margin = new Thickness(0, 0, 0, 5) };
            var usageText = Config.FormatUsagePercent(summary.Used) + "%";
            var pct = new TextBlock
            {
                Text = summary.Estimate.HasValue
                    ? usageText + "　·　" + Loc.T("Hover.Remaining", QuotaPredictor.Format(summary.Estimate.Value))
                    : usageText,
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
            };
            if (summary.Estimate.HasValue)
            {
                pct.ToolTip = Loc.T("Hover.RemainingTip", QuotaPredictor.Format(summary.Estimate.Value));
            }

            DockPanel.SetDock(pct, Dock.Right);
            head.Children.Add(pct);
            head.Children.Add(new TextBlock { Text = title, FontSize = 11 });
            HoverContent.Children.Add(head);

            // 用量条（单色分级）
            var barHost = new Grid();
            var barBorder = new Border
            {
                Height = 6,
                CornerRadius = new CornerRadius(3),
                Background = UsageBarRenderer.BarTrackBrush,
                ClipToBounds = true,
                Child = barHost,
            };
            UsageBarRenderer.RenderBar(barHost, summary.Used);
            HoverContent.Children.Add(barBorder);

            // 重置倒计时（Tag 存重置时刻，定时器每秒刷新）
            var countdown = new TextBlock
            {
                FontSize = 10,
                Margin = new Thickness(0, 4, 0, 0),
                Foreground = UsageBarRenderer.MutedBrush,
                Tag = summary.ResetMs,
            };
            HoverContent.Children.Add(countdown);
            UpdateCountdownText(countdown, summary.ResetMs);

            // 请求数
            HoverContent.Children.Add(new TextBlock
            {
                Text = Loc.T("Usage.Requests", summary.Requests.ToString("N0")),
                FontSize = 10,
                Margin = new Thickness(0, 3, 0, 0),
                Foreground = UsageBarRenderer.MutedBrush,
            });
        }

        /// <summary>信用计划区块：包含额度进度 + 购买余额。</summary>
        private void AddCreditsSection(BalanceResponse balance, bool isFirst)
        {
            AddSeparator(isFirst);

            var credits = balance.Credits;
            var total = credits.AllowanceUsd;
            var used = total > 0 ? Math.Max(0, Math.Min(1, (total - credits.BalanceUsd) / total)) : 0;
            var resetMs = ResetTime.ParseTimestampMs(credits.Period?.Until);

            var head = new DockPanel { Margin = new Thickness(0, 0, 0, 5) };
            var pct = new TextBlock
            {
                Text = Config.FormatUsagePercent(used) + "%",
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
            };
            DockPanel.SetDock(pct, Dock.Right);
            head.Children.Add(pct);
            head.Children.Add(new TextBlock { Text = Loc.T("Panel.IncludedCredits"), FontSize = 11 });
            HoverContent.Children.Add(head);

            var barHost = new Grid();
            var barBorder = new Border
            {
                Height = 6,
                CornerRadius = new CornerRadius(3),
                Background = UsageBarRenderer.BarTrackBrush,
                ClipToBounds = true,
                Child = barHost,
            };
            UsageBarRenderer.RenderBar(barHost, used);
            HoverContent.Children.Add(barBorder);

            var countdown = new TextBlock
            {
                FontSize = 10,
                Margin = new Thickness(0, 4, 0, 0),
                Foreground = UsageBarRenderer.MutedBrush,
                Tag = resetMs,
            };
            HoverContent.Children.Add(countdown);
            UpdateCountdownText(countdown, resetMs);

            HoverContent.Children.Add(new TextBlock
            {
                Text = Loc.T("Usage.Included", credits.BalanceUsd.ToString("F2"), total.ToString("F2")),
                FontSize = 10,
                Margin = new Thickness(0, 3, 0, 0),
                Foreground = UsageBarRenderer.MutedBrush,
            });

            HoverContent.Children.Add(new TextBlock
            {
                Text = Loc.T("Usage.Balance", balance.PurchasedUsd.ToString("F2")),
                FontSize = 10,
                Margin = new Thickness(0, 2, 0, 0),
                Foreground = UsageBarRenderer.MutedBrush,
            });
        }

        /// <summary>请求历史迷你图表（每小时 24h + 每天 7d）。</summary>
        private void AddChartSection(UsageSnapshot snapshot)
        {
            AddSeparator(isFirst: false);

            AddChartRow(Loc.T("Usage.24h"), snapshot.Hourly);
            AddChartRow(Loc.T("Usage.7d"), snapshot.Daily);
        }

        private void AddChartRow(string title, UsageResponse response)
        {
            var buckets = response.Buckets;
            var total = response.Totals.RequestCount;
            var peak = 0L;
            foreach (var bucket in buckets)
            {
                if (bucket.RequestCount > peak)
                {
                    peak = bucket.RequestCount;
                }
            }

            HoverContent.Children.Add(new TextBlock
            {
                Text = title,
                FontSize = 10,
                Foreground = UsageBarRenderer.MutedBrush,
                Margin = new Thickness(0, 0, 0, 3),
            });

            // 矩形柱状图（底部严格对齐，不用块字符避免基线参差）。
            var chartHost = new Grid();
            var chartBorder = new Border
            {
                Height = UsageBarRenderer.ChartHeight,
                Margin = new Thickness(0, 0, 0, 3),
                Child = chartHost,
            };
            UsageBarRenderer.RenderChart(chartHost, buckets);
            HoverContent.Children.Add(chartBorder);

            HoverContent.Children.Add(new TextBlock
            {
                Text = Loc.T("Usage.Requests", total.ToString("N0")) + " · " + Loc.T("Usage.Peak", peak.ToString("N0")),
                FontSize = 10,
                Foreground = UsageBarRenderer.MutedBrush,
                Margin = new Thickness(0, 0, 0, 6),
            });
        }

        private void AddSeparator(bool isFirst)
        {
            if (!isFirst)
            {
                HoverContent.Children.Add(new Border
                {
                    Height = 1,
                    Margin = new Thickness(0, 8, 0, 8),
                    Background = UsageBarRenderer.MutedBrush,
                    Opacity = 0.25,
                });
            }
        }

        private void UpdateCountdowns()
        {
            foreach (var child in HoverContent.Children)
            {
                if (child is TextBlock tb && tb.Tag is long resetMs)
                {
                    UpdateCountdownText(tb, resetMs);
                }
            }
        }

        private static void UpdateCountdownText(TextBlock target, long resetMs)
        {
            var text = resetMs > 0
                ? ResetTime.FormatRemaining(
                    DateTimeOffset.FromUnixTimeMilliseconds(resetMs) - DateTimeOffset.UtcNow)
                : "—";
            target.Text = Loc.T("Hover.ResetIn") + text;
        }

        // ---- 点击交互 ----

        private void OnMainClick(object sender, MouseButtonEventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            HoverPopup.IsOpen = false;
            OllamaCloudUsagePackage.Instance?.OpenUsagePanel();
            e.Handled = true;
        }

        private void OnOpenPanelClick(object sender, MouseButtonEventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            HoverPopup.IsOpen = false;
            OllamaCloudUsagePackage.Instance?.OpenUsagePanel();
            e.Handled = true;
        }
    }
}
