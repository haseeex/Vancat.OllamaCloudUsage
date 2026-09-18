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
            Unloaded += (s, e) =>
            {
                _countdownTimer.Stop();
                VSColorTheme.ThemeChanged -= OnThemeChanged;
                Loc.LanguageChanged -= OnLanguageChanged;
            };
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

            if (data?.Usage == null)
            {
                UsageText.Text = data?.Loading == true
                    ? Loc.T("StatusBar.Loading")
                    : Loc.T("StatusBar.NoData");
            }
            else
            {
                var sessionPct = (int)Math.Round(data.Usage.Session.Usage * 100);
                var weeklyPct = (int)Math.Round(data.Usage.Weekly.Usage * 100);
                UsageText.Text = Loc.T("StatusBar.Text", sessionPct, weeklyPct);
            }

            if (HoverPopup.IsOpen)
            {
                BuildHoverContent();
            }
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
            if (data?.Usage == null)
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

            var usage = data.Usage;

            // 标题
            HoverContent.Children.Add(new TextBlock
            {
                Text = Loc.T("Hover.Title"),
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 8),
            });

            AddLimitSection(Loc.T("Hover.SessionWindow"), usage.Session, ResetTime.NextSessionReset, isFirst: true);
            AddLimitSection(Loc.T("Hover.WeeklyWindow"), usage.Weekly, ResetTime.NextWeeklyReset, isFirst: false);

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

        private void AddLimitSection(string title, LimitUsage limit, Func<DateTimeOffset, DateTimeOffset> nextReset, bool isFirst)
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

            // 标题行：名称 + 百分比
            var head = new DockPanel { Margin = new Thickness(0, 0, 0, 5) };
            var pct = new TextBlock
            {
                Text = $"{Math.Round(limit.Usage * 100)}%",
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
            };
            DockPanel.SetDock(pct, Dock.Right);
            head.Children.Add(pct);
            head.Children.Add(new TextBlock { Text = title, FontSize = 11 });
            HoverContent.Children.Add(head);

            // 用量条
            var barHost = new Grid();
            var barBorder = new Border
            {
                Height = 6,
                CornerRadius = new CornerRadius(3),
                Background = new SolidColorBrush(Color.FromArgb(51, 128, 128, 128)),
                ClipToBounds = true,
                Child = barHost,
            };
            UsageBarRenderer.RenderBar(barHost, limit);
            HoverContent.Children.Add(barBorder);

            // 重置倒计时
            var countdown = new TextBlock
            {
                FontSize = 10,
                Margin = new Thickness(0, 4, 0, 0),
                Foreground = UsageBarRenderer.MutedBrush,
                Tag = nextReset,
            };
            HoverContent.Children.Add(countdown);
            UpdateCountdownText(countdown, nextReset);

            // 模型列表
            if (limit.Models.Count > 0)
            {
                var models = new StackPanel { Margin = new Thickness(0, 6, 0, 0) };
                UsageBarRenderer.RenderModelList(models, limit, fontSize: 10);
                HoverContent.Children.Add(models);
            }
        }

        private void UpdateCountdowns()
        {
            foreach (var child in HoverContent.Children)
            {
                if (child is TextBlock tb && tb.Tag is Func<DateTimeOffset, DateTimeOffset> nextReset)
                {
                    UpdateCountdownText(tb, nextReset);
                }
            }
        }

        private static void UpdateCountdownText(TextBlock target, Func<DateTimeOffset, DateTimeOffset> nextReset)
        {
            var now = DateTimeOffset.UtcNow;
            target.Text = Loc.T("Hover.ResetIn") + ResetTime.FormatRemaining(nextReset(now) - now);
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
