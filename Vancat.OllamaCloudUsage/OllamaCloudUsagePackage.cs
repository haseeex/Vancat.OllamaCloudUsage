using System;
using System.ComponentModel.Design;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Task = System.Threading.Tasks.Task;
using Vancat.OllamaCloudUsage.Services;
using Vancat.OllamaCloudUsage.ToolWindows;

namespace Vancat.OllamaCloudUsage
{
    /// <summary>Ollama Cloud 用量监控扩展主包。</summary>
    [PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
    [InstalledProductRegistration("Ollama Cloud 用量监控", "在 Visual Studio 中查看 Ollama Cloud 用量与限额。", "1.2.2")]
    [ProvideMenuResource("Menus.ctmenu", 1)]
    [ProvideToolWindow(typeof(UsageToolWindow), Style = VsDockStyle.Tabbed, Window = ToolWindowGuids.SolutionExplorer)]
    [ProvideAutoLoad(VSConstants.UICONTEXT.ShellInitialized_string, PackageAutoLoadFlags.BackgroundLoad)]
    [Guid(PackageGuidString)]
    public sealed class OllamaCloudUsagePackage : AsyncPackage
    {
        public const string PackageGuidString = "3E9F1A72-8C4B-4D6E-B2A9-5F7C1D8E3A64";

        internal static OllamaCloudUsagePackage Instance { get; private set; }

        internal UsageService UsageService { get; private set; }
        internal AccountStore AccountStore { get; private set; }

        private StatusBarManager _statusBar;
        private System.Threading.Timer _refreshTimer;
        private System.Threading.Timer _injectTimer;
        private (OleMenuCommand Command, string Key)[] _menuCommands;

        protected override async Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
        {
            await base.InitializeAsync(cancellationToken, progress).ConfigureAwait(false);
            await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

            Instance = this;
            AccountStore = new AccountStore();
            UsageService = new UsageService(AccountStore, new SharedUsageCache(), new OllamaApiClient());

            _statusBar = new StatusBarManager(this);
            UsageService.Updated += OnUsageUpdated;

            var commandService = await GetServiceAsync(typeof(IMenuCommandService)).ConfigureAwait(false) as OleMenuCommandService;
            if (commandService != null)
            {
                RegisterCommands(commandService);
            }

            // 主窗口/状态栏可能尚未就绪，按秒重试注入直至成功。
            TryInjectStatusBar();
            _injectTimer = new System.Threading.Timer(
                _ => _ = JoinableTaskFactory.RunAsync(async () =>
                {
                    await JoinableTaskFactory.SwitchToMainThreadAsync();
                    TryInjectStatusBar();
                }),
                null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));

            ScheduleRefresh();
            _ = UsageService.RefreshAsync(false);
        }

        /// <summary>尝试注入状态栏控件；成功后将数据推送到控件。</summary>
        private void TryInjectStatusBar()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (_statusBar == null)
            {
                return;
            }

            if (_statusBar.IsInjected)
            {
                // 窗口重载等场景下控件可能已被移除，此时允许重新注入。
                if (_statusBar.IsStillAttached())
                {
                    return;
                }

                _statusBar.Remove();
            }

            if (_statusBar.TryInject())
            {
                _statusBar.Update(UsageService.Snapshot());
            }
        }

        private void OnUsageUpdated(object sender, UsageUpdatedEventArgs e)
        {
            _ = JoinableTaskFactory.RunAsync(async () =>
            {
                await JoinableTaskFactory.SwitchToMainThreadAsync();
                TryInjectStatusBar();
                _statusBar?.Update(e);
            });
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _refreshTimer?.Dispose();
                _injectTimer?.Dispose();
                UsageService.Updated -= OnUsageUpdated;
                Loc.LanguageChanged -= OnLanguageChangedForMenu;
                UsageService?.Dispose();
                _statusBar?.Dispose();
            }

            base.Dispose(disposing);
        }

        private void RegisterCommands(OleMenuCommandService commandService)
        {
            var cmdSet = new Guid("B6D4E8F2-1A3C-4E5F-9B7D-2C8A6F4E1B93");

            var refreshCmd = new OleMenuCommand(
                (s, e) => { ThreadHelper.ThrowIfNotOnUIThread(); _ = RefreshUsageAsync(); },
                new CommandID(cmdSet, 0x0100));
            var openPanelCmd = new OleMenuCommand(
                (s, e) => { ThreadHelper.ThrowIfNotOnUIThread(); OpenUsagePanel(); },
                new CommandID(cmdSet, 0x0101));
            var addAccountCmd = new OleMenuCommand(
                (s, e) => { ThreadHelper.ThrowIfNotOnUIThread(); _ = AddAccountAsync(); },
                new CommandID(cmdSet, 0x0102));
            var removeAccountCmd = new OleMenuCommand(
                (s, e) => { ThreadHelper.ThrowIfNotOnUIThread(); _ = RemoveAccountAsync(); },
                new CommandID(cmdSet, 0x0103));
            var setIntervalCmd = new OleMenuCommand(
                (s, e) => { ThreadHelper.ThrowIfNotOnUIThread(); _ = SetRefreshIntervalAsync(); },
                new CommandID(cmdSet, 0x0104));
            var toggleLangCmd = new OleMenuCommand(
                (s, e) => { ThreadHelper.ThrowIfNotOnUIThread(); _ = ToggleLanguageAsync(); },
                new CommandID(cmdSet, 0x0105));

            commandService.AddCommand(refreshCmd);
            commandService.AddCommand(openPanelCmd);
            commandService.AddCommand(addAccountCmd);
            commandService.AddCommand(removeAccountCmd);
            commandService.AddCommand(setIntervalCmd);
            commandService.AddCommand(toggleLangCmd);

            // 菜单文本跟随语言切换（VSCT 里的初始文本为中文）。
            _menuCommands = new (OleMenuCommand Command, string Key)[]
            {
                (refreshCmd, "Cmd.Refresh"),
                (openPanelCmd, "Cmd.OpenPanel"),
                (addAccountCmd, "Cmd.AddAccount"),
                (removeAccountCmd, "Cmd.RemoveAccount"),
                (setIntervalCmd, "Cmd.SetInterval"),
                (toggleLangCmd, "Cmd.ToggleLang"),
            };
            ApplyMenuLanguage();

            Loc.LanguageChanged += OnLanguageChangedForMenu;
        }

        /// <summary>语言切换时更新菜单文本。</summary>
        private void OnLanguageChangedForMenu(object sender, EventArgs e)
        {
            _ = JoinableTaskFactory.RunAsync(async () =>
            {
                await JoinableTaskFactory.SwitchToMainThreadAsync();
                ApplyMenuLanguage();
            });
        }

        /// <summary>应用当前语言到所有菜单命令文本。</summary>
        private void ApplyMenuLanguage()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (_menuCommands == null)
            {
                return;
            }

            foreach (var (command, key) in _menuCommands)
            {
                try
                {
                    command.Text = Loc.T(key);
                }
                catch
                {
                    // 命令表尚未就绪时忽略。
                }
            }
        }

        internal void OpenUsagePanel()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var window = FindToolWindow(typeof(UsageToolWindow), 0, true);
            if (window?.Frame == null)
            {
                VsShellUtilities.ShowMessageBox(
                    this, Loc.T("Msg.CannotCreatePanel"), Loc.T("Window.Title"),
                    OLEMSGICON.OLEMSGICON_WARNING, OLEMSGBUTTON.OLEMSGBUTTON_OK, OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
                return;
            }

            var windowFrame = (IVsWindowFrame)window.Frame;
            ErrorHandler.ThrowOnFailure(windowFrame.Show());
        }

        internal async Task RefreshUsageAsync()
        {
            await JoinableTaskFactory.SwitchToMainThreadAsync();
            await UsageService.RefreshAsync(true).ConfigureAwait(false);
        }

        internal async Task AddAccountAsync()
        {
            await JoinableTaskFactory.SwitchToMainThreadAsync();

            var label = PromptForAccountLabel();
            if (label == null)
            {
                return;
            }

            var key = PromptForApiKey();
            if (key == null)
            {
                return;
            }

            AccountStore.Add(label, key);
            await UsageService.RefreshAsync(true).ConfigureAwait(false);
        }

        internal async Task RemoveAccountAsync()
        {
            await JoinableTaskFactory.SwitchToMainThreadAsync();

            var state = AccountStore.Load();
            if (state.Accounts.Count == 0)
            {
                return;
            }

            var picked = PickAccount(state, Loc.T("Dlg.RemoveAccountTitle"));
            if (picked == null)
            {
                return;
            }

            AccountStore.Remove(picked.Id);
            await UsageService.RefreshAsync(true).ConfigureAwait(false);
        }

        internal async Task SetRefreshIntervalAsync()
        {
            await JoinableTaskFactory.SwitchToMainThreadAsync();

            var input = PromptForInterval();
            if (input == null)
            {
                return;
            }

            Config.SetRefreshIntervalSeconds(input.Value);
            ScheduleRefresh();
            await UsageService.RefreshAsync(true).ConfigureAwait(false);
        }

        /// <summary>在中英文之间切换界面语言。</summary>
        internal async Task ToggleLanguageAsync()
        {
            await JoinableTaskFactory.SwitchToMainThreadAsync();

            Loc.Current = Loc.IsChinese
                ? AppLanguage.English
                : AppLanguage.Chinese;

            // 语言切换后重刷一次，让错误消息等动态文本也更新。
            await UsageService.RefreshAsync(true).ConfigureAwait(false);
        }

        internal void SwitchAccount(string id)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            AccountStore.SetActive(id);
            _ = UsageService.RefreshAsync(true);
        }

        private void ScheduleRefresh()
        {
            _refreshTimer?.Dispose();
            var interval = TimeSpan.FromSeconds(Config.GetRefreshIntervalSeconds());
            _refreshTimer = new System.Threading.Timer(
                _ => _ = UsageService.RefreshAsync(false),
                null, interval, interval);
        }

        // ---- UI 提示辅助 ----

        private string PromptForAccountLabel()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var dialog = new InputDialog(
                Loc.T("Dlg.AddAccountTitle"),
                Loc.T("Dlg.AccountLabel"),
                string.Empty,
                value => string.IsNullOrWhiteSpace(value) ? Loc.T("Dlg.AccountLabelRequired") : null);
            return dialog.ShowDialog() == true ? dialog.Value : null;
        }

        private string PromptForApiKey()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var dialog = new InputDialog(
                Loc.T("Dlg.AddAccountTitle"),
                Loc.T("Dlg.ApiKey"),
                string.Empty,
                value => string.IsNullOrWhiteSpace(value) ? Loc.T("Dlg.ApiKeyRequired") : null,
                isPassword: true);
            return dialog.ShowDialog() == true ? dialog.Value : null;
        }

        private int? PromptForInterval()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var current = Config.GetRefreshIntervalSeconds();
            var dialog = new InputDialog(
                Loc.T("Dlg.IntervalTitle"),
                Loc.T("Dlg.IntervalPrompt", Config.MinRefreshIntervalSeconds, Config.MaxRefreshIntervalSeconds),
                current.ToString(),
                value =>
                {
                    if (!int.TryParse(value?.Trim(), out var n))
                    {
                        return Loc.T("Dlg.IntervalInvalid");
                    }

                    if (n < Config.MinRefreshIntervalSeconds || n > Config.MaxRefreshIntervalSeconds)
                    {
                        return Loc.T("Dlg.IntervalRange", Config.MinRefreshIntervalSeconds, Config.MaxRefreshIntervalSeconds);
                    }

                    return null;
                });
            if (dialog.ShowDialog() != true)
            {
                return null;
            }

            return int.Parse(dialog.Value.Trim());
        }

        private Account PickAccount(AccountsState state, string title)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var dialog = new AccountPickerDialog(title, state);
            return dialog.ShowDialog() == true ? dialog.Selected : null;
        }
    }
}
