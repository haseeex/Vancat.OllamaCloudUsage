using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Vancat.OllamaCloudUsage.Services;
using Vancat.OllamaCloudUsage.Views;

namespace Vancat.OllamaCloudUsage.ToolWindows
{
    /// <summary>
    /// 用量面板控件：渲染用量条、请求历史图表与重置倒计时。
    ///
    /// 已适配官方 2026-10-06 新 API：数据来自 /api/balance（权威百分比与
    /// 重置时间）+ /api/usage（请求历史），支持旧版窗口计划与信用计划两种形态。
    /// </summary>
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

        private void OnLanguageChanged(object sender, EventArgs e)
        {
            ApplyLanguage();
            Render(_data);
        }

        private void OnDisplayChanged(object sender, EventArgs e) => Render(_data);

        /// <summary>应用当前语言到静态文本与按钮提示。</summary>
        private void ApplyLanguage()
        {
            TitleText.Text = Loc.T("Panel.Title");
            RefreshButton.ToolTip = Loc.T("Panel.Refresh");
            SettingsButton.ToolTip = Loc.T("Panel.Settings");
            AddAccountButton.ToolTip = Loc.T("Panel.AddAccount");
            RemoveAccountButton.ToolTip = Loc.T("Panel.RemoveAccount");
            AddKeyButton.Content = Loc.T("Panel.AddKey");
            SessionTitle.Text = Loc.T("Panel.SessionWindow");
            WeeklyTitle.Text = Loc.T("Panel.WeeklyWindow");
            CreditsTitle.Text = Loc.T("Panel.IncludedCredits");
            Chart24hTitle.Text = Loc.T("Usage.24h");
            Chart7dTitle.Text = Loc.T("Usage.7d");
        }

        /// <summary>由工具窗口调用以渲染最新数据。</summary>
        public void Render(UsageUpdatedEventArgs data)
        {
            _data = data;

            // 刷新中：按钮进入忙碌状态（禁用 + 显示省略号），给用户明确反馈。
            var loading = data?.Loading == true;
            RefreshButton.IsEnabled = !loading;
            RefreshButton.Content = loading ? "…" : "⟳";

            RenderAccounts(data);

            var usage = data?.Usage;
            if (data == null || usage?.Balance == null)
            {
                SetStatus(data?.Loading == true ? Loc.T("Panel.Loading") : (data?.Error ?? Loc.T("Panel.NoData")), data?.Error != null);
                HideAllSections();
                AddKeyButton.Visibility = data?.Error != null && string.IsNullOrWhiteSpace(data.Accounts.Active?.Key)
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            }
            else
            {
                // 有数据时若刷新失败，仍显示错误提示（不遮挡已有数据）。
                if (!string.IsNullOrEmpty(data.Error))
                {
                    SetStatus("⚠ " + data.Error, true);
                }
                else
                {
                    StatusText.Visibility = Visibility.Collapsed;
                }

                AddKeyButton.Visibility = Visibility.Collapsed;

                if (usage.Balance.IsLegacy)
                {
                    // 旧版计划：5 小时 + 每周窗口
                    CreditsPanel.Visibility = Visibility.Collapsed;
                    SessionPanel.Visibility = Visibility.Visible;
                    WeeklyPanel.Visibility = Visibility.Visible;
                    MiddleSeparator.Visibility = Visibility.Visible;

                    var session = QuotaPredictor.SummarizeWindow(
                        usage.Balance.Legacy.Session.RemainingPercent,
                        usage.Balance.Legacy.Session.ResetsAt,
                        ResetTime.SessionWindowMs,
                        usage.Hourly.Buckets);

                    var weekly = QuotaPredictor.SummarizeWindow(
                        usage.Balance.Legacy.Weekly.RemainingPercent,
                        usage.Balance.Legacy.Weekly.ResetsAt,
                        ResetTime.WeekMs,
                        usage.Daily.Buckets);

                    RenderWindow(SessionBar, SessionPercent, SessionReset, SessionCount, SessionEstimate, session);
                    RenderWindow(WeeklyBar, WeeklyPercent, WeeklyReset, WeeklyCount, WeeklyEstimate, weekly);
                }
                else
                {
                    // 信用计划：包含额度 + 购买余额
                    SessionPanel.Visibility = Visibility.Collapsed;
                    WeeklyPanel.Visibility = Visibility.Collapsed;
                    MiddleSeparator.Visibility = Visibility.Collapsed;
                    CreditsPanel.Visibility = Visibility.Visible;

                    var credits = usage.Balance.Credits;
                    var total = credits.AllowanceUsd;
                    var used = total > 0 ? Math.Max(0, Math.Min(1, (total - credits.BalanceUsd) / total)) : 0;
                    var resetMs = ResetTime.ParseTimestampMs(credits.Period?.Until);

                    CreditsPercent.Text = Loc.T("Panel.Used", Config.FormatUsagePercent(used));
                    UsageBarRenderer.RenderBar(CreditsBar, used);
                    CreditsReset.Tag = resetMs;
                    UpdateCountdownText(CreditsReset, resetMs);
                    CreditsIncluded.Text = Loc.T("Usage.Included", credits.BalanceUsd.ToString("F2"), total.ToString("F2"));
                    CreditsBalance.Text = Loc.T("Usage.Balance", usage.Balance.PurchasedUsd.ToString("F2"));
                }

                // 请求历史图表
                ChartPanel.Visibility = Visibility.Visible;
                RenderChart(Chart24hHost, Chart24hMeta, usage.Hourly);
                RenderChart(Chart7dHost, Chart7dMeta, usage.Daily);

                UpdateCountdowns();
            }

            RenderFooter(data);
        }

        private void HideAllSections()
        {
            SessionPanel.Visibility = Visibility.Collapsed;
            WeeklyPanel.Visibility = Visibility.Collapsed;
            CreditsPanel.Visibility = Visibility.Collapsed;
            MiddleSeparator.Visibility = Visibility.Collapsed;
            ChartPanel.Visibility = Visibility.Collapsed;
        }

        private void RenderAccounts(UsageUpdatedEventArgs data)
        {
            _suppressAccountChange = true;
            try
            {
                AccountCombo.Items.Clear();
                if (data?.Accounts != null)
                {
                    foreach (var account in data.Accounts.Accounts)
                    {
                        AccountCombo.Items.Add(new ComboBoxItem
                        {
                            Content = account.Label,
                            Tag = account.Id,
                            IsSelected = account.Id == data.Accounts.ActiveId,
                        });
                    }
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

        /// <summary>渲染一个配额窗口：百分比 + 剩余预测 + 用量条 + 倒计时 + 请求数。</summary>
        private static void RenderWindow(
            Grid barHost, TextBlock percentText, TextBlock resetText, TextBlock countText, TextBlock estimateText, WindowSummary summary)
        {
            percentText.Text = Loc.T("Panel.Used", Config.FormatUsagePercent(summary.Used));

            if (summary.Estimate.HasValue)
            {
                estimateText.Text = Loc.T("Panel.Remaining", QuotaPredictor.Format(summary.Estimate.Value));
                estimateText.ToolTip = Loc.T("Panel.RemainingTip", QuotaPredictor.Format(summary.Estimate.Value));
                estimateText.Visibility = Visibility.Visible;
            }
            else
            {
                estimateText.Visibility = Visibility.Collapsed;
            }

            UsageBarRenderer.RenderBar(barHost, summary.Used);

            resetText.Tag = summary.ResetMs;
            UpdateCountdownText(resetText, summary.ResetMs);

            countText.Text = Loc.T("Usage.Requests", summary.Requests.ToString("N0"));
        }

        /// <summary>渲染请求历史柱状图（矩形绘制，底部严格对齐）与总量/峰值。</summary>
        private static void RenderChart(Grid host, TextBlock meta, UsageResponse response)
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

            UsageBarRenderer.RenderChart(host, buckets);
            meta.Text = Loc.T("Usage.Requests", total.ToString("N0")) + "　·　" + Loc.T("Usage.Peak", peak.ToString("N0"));
        }

        private void UpdateCountdowns()
        {
            if (_data?.Usage?.Balance == null)
            {
                return;
            }

            UpdateCountdownText(SessionReset, SessionReset.Tag);
            UpdateCountdownText(WeeklyReset, WeeklyReset.Tag);
            UpdateCountdownText(CreditsReset, CreditsReset.Tag);
        }

        private static void UpdateCountdownText(TextBlock target, object tag)
        {
            if (!(tag is long resetMs))
            {
                return;
            }

            var text = resetMs > 0
                ? ResetTime.FormatRemaining(DateTimeOffset.FromUnixTimeMilliseconds(resetMs) - DateTimeOffset.UtcNow)
                : "—";
            target.Text = Loc.T("Panel.ResetIn") + text;
        }

        private void SetStatus(string text, bool isError)
        {
            StatusText.Text = text;
            StatusText.Foreground = isError ? UsageBarRenderer.ErrorBrush : UsageBarRenderer.MutedBrush;
            StatusText.Visibility = Visibility.Visible;
        }

        private void RenderFooter(UsageUpdatedEventArgs data)
        {
            var parts = new System.Collections.Generic.List<string>
            {
                Loc.T("Panel.AutoRefresh", Config.FormatInterval(data?.IntervalSeconds ?? Config.DefaultRefreshIntervalSeconds)),
            };

            if (data != null && data.LastUpdatedMs > 0)
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
