using System;
using System.Runtime.InteropServices;
using Microsoft.VisualStudio.Shell;
using Vancat.OllamaCloudUsage.Services;

namespace Vancat.OllamaCloudUsage.ToolWindows
{
    /// <summary>用量面板工具窗口。</summary>
    [Guid("7C2A5E91-3B6D-4F8A-A1E5-9D4B7C2E6F83")]
    public sealed class UsageToolWindow : ToolWindowPane
    {
        private UsageToolWindowControl _control;

        public UsageToolWindow() : base(null)
        {
            Caption = Loc.T("Window.Title");
        }

        protected override void Initialize()
        {
            base.Initialize();

            _control = new UsageToolWindowControl();
            Content = _control;

            var package = OllamaCloudUsagePackage.Instance;
            if (package != null)
            {
                // 立即渲染当前快照，随后订阅更新。
                _control.Render(package.UsageService.Snapshot());
                package.UsageService.Updated += OnUsageUpdated;
            }
        }

        private void OnUsageUpdated(object sender, UsageUpdatedEventArgs e)
        {
            ThreadHelper.JoinableTaskFactory.Run(async () =>
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                _control?.Render(e);
            });
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                var package = OllamaCloudUsagePackage.Instance;
                if (package != null)
                {
                    package.UsageService.Updated -= OnUsageUpdated;
                }

                _control = null;
            }

            base.Dispose(disposing);
        }
    }
}
