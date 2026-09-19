using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Vancat.OllamaCloudUsage.Services;
using Vancat.OllamaCloudUsage.Views;

namespace Vancat.OllamaCloudUsage.ToolWindows
{
    /// <summary>用量面板控件：渲染用量条、模型列表与重置倒计时。</summary>
    public partial class UsageToolWindowControl : UserControl
    {
        private readonly DispatcherTimer _countdownTimer;
        private UsageUpdatedEventArgs _data;
        private bool _suppressAccountChange;

        public UsageToolWindowControl()
        {
            InitializeComponent();

            ApplyLanguage();

            _countdownTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _countdownTimer.Tick += (s, e) => UpdateCountdowns();
            _countdownTimer.Start();

            Loc.LanguageChanged += OnLanguageChanged;
            Config.DisplayChanged += OnDisplayChanged;
            Unloaded += (s, e) =>
            {
                _countdownTimer.Stop();
                Loc.LanguageChanged -= OnLanguageChanged;
                Config.DisplayChanged -= OnDisplayChanged;
            };
        }

        /// <summary>用量精度等显示设置变更时按新设置重绘。</summary>
        private void OnDisplayChanged(object sender, EventArgs e)
        {
            Render(_data);
        }

        /// <summary>语言切换时刷新全部界面文本。</summary>
        private void OnLanguageChanged(object sender, EventArgs e)
        {
            ApplyLanguage();
            Render(_data);
        }

        /// <summary>应用当前语言到静态文本与工具提示。</summary>
        private void ApplyLanguage()
        {
            TitleText.Text = Loc.T("Panel.Title");
            RefreshButton.ToolTip = Loc.T("Panel.Refresh");
            SettingsButton.ToolTip = Loc.T("Panel.Settings");
            RemoveAccountButton.ToolTip = Loc.T("Panel.RemoveAccount");
            AddAccountButton.ToolTip = Loc.T("Panel.AddAccount");
            AddKeyButton.Content = Loc.T("Panel.AddKey");
            SessionTitle.Text = Loc.T("Panel.SessionWindow");
            WeeklyTitle.Text = Loc.T("Panel.WeeklyWindow");
            SessionModelsLabel.Text = Loc.T("Panel.SessionModels");
            WeeklyModelsLabel.Text = Loc.T("Panel.WeeklyModels");
            ActivityLabel.Text = Loc.T("Panel.Activity");
        }

        /// <summary>由工具窗口调用以渲染最新数据。</summary>
        public void Render(UsageUpdatedEventArgs data)
        {
            _data = data;

            RenderAccounts(data);

            if (data.Loading)
            {
                SetStatus(Loc.T("Panel.Loading"), false);
                SessionPanel.Visibility = Visibility.Collapsed;
                WeeklyPanel.Visibility = Visibility.Collapsed;
                MiddleSeparator.Visibility = Visibility.Collapsed;
                ActivityPanel.Visibility = Visibility.Collapsed;
                AddKeyButton.Visibility = Visibility.Collapsed;
            }
            else if (data.Error != null)
            {
                SetStatus("⚠ " + data.Error, true);
                SessionPanel.Visibility = Visibility.Collapsed;
                WeeklyPanel.Visibility = Visibility.Collapsed;
                MiddleSeparator.Visibility = Visibility.Collapsed;
                ActivityPanel.Visibility = Visibility.Collapsed;
                AddKeyButton.Visibility = string.IsNullOrWhiteSpace(data.Accounts.Active?.Key)
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            }
            else if (data.Usage == null)
            {
                SetStatus(Loc.T("Panel.NoData"), false);
                SessionPanel.Visibility = Visibility.Collapsed;
                WeeklyPanel.Visibility = Visibility.Collapsed;
                MiddleSeparator.Visibility = Visibility.Collapsed;
                ActivityPanel.Visibility = Visibility.Collapsed;
                AddKeyButton.Visibility = Visibility.Collapsed;
            }
            else
            {
                StatusText.Visibility = Visibility.Collapsed;
                AddKeyButton.Visibility = Visibility.Collapsed;
                SessionPanel.Visibility = Visibility.Visible;
                WeeklyPanel.Visibility = Visibility.Visible;
                MiddleSeparator.Visibility = Visibility.Visible;
                ActivityPanel.Visibility = Visibility.Visible;

                var usage = data.Usage;
                RenderLimit(usage.Session, SessionBar, SessionPercent, SessionModels);
                RenderLimit(usage.Weekly, WeeklyBar, WeeklyPercent, WeeklyModels);

                var cost = Loc.T("Panel.Cost", usage.Activity?.Cost ?? "—");
                var period = Loc.T("Panel.Period", usage.Activity?.Period?.Type ?? "—");
                ActivityText.Text = cost + "　·　" + period;
                UpdateCountdowns();
            }

            RenderFooter(data);
        }

        private void RenderAccounts(UsageUpdatedEventArgs data)
        {
            _suppressAccountChange = true;
            try
            {
                AccountCombo.Items.Clear();
                foreach (var account in data.Accounts.Accounts)
                {
                    AccountCombo.Items.Add(new ComboBoxItem
                    {
                        Content = account.Label,
                        Tag = account.Id,
                        IsSelected = account.Id == data.Accounts.ActiveId,
                    });
                }

                if (AccountCombo.SelectedIndex < 0 && AccountCombo.Items.Count > 0)
                {
                    AccountCombo.SelectedIndex = 0;
                }

                AccountCombo.IsEnabled = AccountCombo.Items.Count > 0;
                RemoveAccountButton.IsEnabled = AccountCombo.Items.Count > 0;
            }
            finally
            {
                _suppressAccountChange = false;
            }
        }

        private void RenderLimit(LimitUsage limit, Grid barHost, TextBlock percentText, StackPanel modelHost)
        {
            // 百分比 + 窗口级剩余次数预测（总请求数 / usage − 总请求数）。
            var text = Loc.T("Panel.Used", Config.FormatUsagePercent(limit.Usage));
            var estimate = QuotaPredictor.EstimateRemainingRequests(limit);
            if (estimate.HasValue)
            {
                text += "　·　" + Loc.T("Panel.Remaining", QuotaPredictor.Format(estimate.Value));
                percentText.ToolTip = Loc.T("Panel.RemainingTip", QuotaPredictor.Format(estimate.Value));
            }
            else
            {
                percentText.ToolTip = null;
            }

            percentText.Text = text;

            // 用量条与模型列表复用共享渲染器，保证与状态栏浮窗显示一致。
            UsageBarRenderer.RenderBar(barHost, limit);
            UsageBarRenderer.RenderModelList(modelHost, limit, fontSize: 12);
        }

        private void UpdateCountdowns()
        {
            if (_data?.Usage == null)
            {
                return;
            }

            var now = DateTimeOffset.UtcNow;
            SessionReset.Text = Loc.T("Panel.ResetIn") + ResetTime.FormatRemaining(ResetTime.NextSessionReset(now) - now);
            WeeklyReset.Text = Loc.T("Panel.ResetIn") + ResetTime.FormatRemaining(ResetTime.NextWeeklyReset(now) - now);
        }

        private void SetStatus(string text, bool isError)
        {
            StatusText.Text = text;
            StatusText.Foreground = isError ? UsageBarRenderer.ErrorBrush : UsageBarRenderer.MutedBrush;
            StatusText.Visibility = Visibility.Visible;
        }

        private void RenderFooter(UsageUpdatedEventArgs data)
        {
            var parts = new List<string> { Loc.T("Panel.AutoRefresh", Config.FormatInterval(data.IntervalSeconds)) };
            if (data.LastUpdatedMs > 0)
            {
                var local = DateTimeOffset.FromUnixTimeMilliseconds(data.LastUpdatedMs).ToLocalTime();
                parts.Add(Loc.T("Panel.LastUpdated", local.ToString("HH:mm:ss")));
            }

            FooterText.Text = string.Join("　·　", parts);
        }

        // ---- 事件处理 ----

        private void OnRefresh(object sender, RoutedEventArgs e)
        {
            _ = OllamaCloudUsagePackage.Instance?.RefreshUsageAsync();
        }

        private void OnSettings(object sender, RoutedEventArgs e)
        {
            _ = OllamaCloudUsagePackage.Instance?.SetRefreshIntervalAsync();
        }

        private void OnAddAccount(object sender, RoutedEventArgs e)
        {
            _ = OllamaCloudUsagePackage.Instance?.AddAccountAsync();
        }

        private void OnRemoveAccount(object sender, RoutedEventArgs e)
        {
            _ = OllamaCloudUsagePackage.Instance?.RemoveAccountAsync();
        }

        private void OnAccountChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_suppressAccountChange)
            {
                return;
            }

            if (AccountCombo.SelectedItem is ComboBoxItem item && item.Tag is string id)
            {
                var package = OllamaCloudUsagePackage.Instance;
                if (package != null)
                {
                    _ = package.JoinableTaskFactory.RunAsync(async () =>
                    {
                        await package.JoinableTaskFactory.SwitchToMainThreadAsync();
                        package.SwitchAccount(id);
                    });
                }
            }
        }
    }
}
