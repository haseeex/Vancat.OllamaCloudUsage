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

            _countdownTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _countdownTimer.Tick += (s, e) => UpdateCountdowns();
            _countdownTimer.Start();
        }

        /// <summary>由工具窗口调用以渲染最新数据。</summary>
        public void Render(UsageUpdatedEventArgs data)
        {
            _data = data;

            RenderAccounts(data);

            if (data.Loading)
            {
                SetStatus("正在加载 Ollama 用量…", false);
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
                SetStatus("暂无数据。", false);
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

                ActivityText.Text = $"费用：${usage.Activity?.Cost ?? "—"}　·　周期：{usage.Activity?.Period?.Type ?? "—"}";
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
            percentText.Text = $"已用 {Math.Round(limit.Usage * 100)}%";

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
            SessionReset.Text = "重置倒计时：" + ResetTime.FormatRemaining(ResetTime.NextSessionReset(now) - now);
            WeeklyReset.Text = "重置倒计时：" + ResetTime.FormatRemaining(ResetTime.NextWeeklyReset(now) - now);
        }

        private void SetStatus(string text, bool isError)
        {
            StatusText.Text = text;
            StatusText.Foreground = isError ? UsageBarRenderer.ErrorBrush : UsageBarRenderer.MutedBrush;
            StatusText.Visibility = Visibility.Visible;
        }

        private void RenderFooter(UsageUpdatedEventArgs data)
        {
            var parts = new List<string> { $"每 {Config.FormatInterval(data.IntervalSeconds)}自动刷新" };
            if (data.LastUpdatedMs > 0)
            {
                var local = DateTimeOffset.FromUnixTimeMilliseconds(data.LastUpdatedMs).ToLocalTime();
                parts.Add($"上次更新 {local:HH:mm:ss}");
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
