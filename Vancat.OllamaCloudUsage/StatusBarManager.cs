using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Microsoft.VisualStudio.Shell;
using Vancat.OllamaCloudUsage.Services;
using Vancat.OllamaCloudUsage.Views;

namespace Vancat.OllamaCloudUsage
{
    /// <summary>
    /// 状态栏注入器：把 WPF 控件插入 VS 主窗口状态栏的视觉树。
    ///
    /// 相比 IVsStatusbar 文本 API，这种方式支持：
    /// - 鼠标悬停浮窗（详细用量信息）
    /// - 点击交互（打开用量面板）
    /// - 图标与富文本
    /// </summary>
    internal sealed class StatusBarManager : IDisposable
    {
        private readonly OllamaCloudUsagePackage _package;
        private OllamaStatusBarControl _control;
        private StatusBarItem _item;
        private StatusBar _statusBar;
        private bool _injected;
        private bool _disposed;

        public StatusBarManager(OllamaCloudUsagePackage package)
        {
            _package = package;
        }

        /// <summary>当前是否已成功注入状态栏。</summary>
        public bool IsInjected => _injected;

        /// <summary>
        /// 检查注入的控件是否仍然挂在状态栏上；若被移除（如窗口重载）则允许重新注入。
        /// 使用缓存的 StatusBar 引用，避免每次遍历视觉树。
        /// </summary>
        public bool IsStillAttached()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (!_injected || _item == null || _statusBar == null)
            {
                return false;
            }

            try
            {
                return _statusBar.Items.Contains(_item);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 尝试注入状态栏控件。主窗口可能尚未就绪，因此调用方应支持重试。
        /// </summary>
        public bool TryInject()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (_injected || _disposed)
            {
                return _injected;
            }

            var mainWindow = Application.Current?.MainWindow;
            if (mainWindow == null || !mainWindow.IsLoaded)
            {
                return false;
            }

            if (_statusBar == null)
            {
                _statusBar = FindChild<StatusBar>(mainWindow);
            }

            if (_statusBar == null)
            {
                return false;
            }

            try
            {
                _control = new OllamaStatusBarControl();
                _item = new StatusBarItem
                {
                    Content = _control,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Padding = new Thickness(0),
                    VerticalContentAlignment = VerticalAlignment.Stretch,
                    HorizontalContentAlignment = HorizontalAlignment.Stretch,
                };

                DockPanel.SetDock(_item, Dock.Right);
                _statusBar.Items.Add(_item);

                _injected = true;
                _control.Render(_package.UsageService.Snapshot());
                return true;
            }
            catch
            {
                _control = null;
                _item = null;
                return false;
            }
        }

        /// <summary>推送最新用量数据到状态栏控件。</summary>
        public void Update(UsageUpdatedEventArgs data)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            _control?.Render(data);
        }

        /// <summary>移除注入的控件。</summary>
        public void Remove()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            try
            {
                _statusBar?.Items.Remove(_item);
            }
            catch
            {
                // 主窗口已关闭时忽略。
            }

            _item = null;
            _control = null;
            _statusBar = null;
            _injected = false;
        }

        /// <summary>在视觉树中查找指定类型的子元素。</summary>
        private static T FindChild<T>(DependencyObject parent) where T : DependencyObject
        {
            if (parent == null)
            {
                return null;
            }

            var count = VisualTreeHelper.GetChildrenCount(parent);
            for (var i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T match)
                {
                    return match;
                }

                var result = FindChild<T>(child);
                if (result != null)
                {
                    return result;
                }
            }

            return null;
        }

        public void Dispose()
        {
            _disposed = true;
        }
    }
}
