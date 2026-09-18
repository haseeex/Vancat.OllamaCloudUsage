using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.VisualStudio.PlatformUI;

namespace Vancat.OllamaCloudUsage
{
    /// <summary>通用输入对话框（账户名称 / API 密钥 / 刷新间隔）。</summary>
    public partial class InputDialog : DialogWindow
    {
        private readonly Func<string, string> _validate;
        private readonly bool _isPassword;

        public InputDialog(string title, string prompt, string initialValue, Func<string, string> validate, bool isPassword = false)
        {
            InitializeComponent();
            Title = title;
            PromptText.Text = prompt;
            _validate = validate;
            _isPassword = isPassword;

            if (isPassword)
            {
                PasswordBox.Visibility = Visibility.Visible;
                ValueBox.Visibility = Visibility.Collapsed;
            }
            else
            {
                ValueBox.Text = initialValue ?? string.Empty;
            }

            Loaded += (s, e) =>
            {
                if (_isPassword)
                {
                    PasswordBox.Focus();
                }
                else
                {
                    ValueBox.Focus();
                    ValueBox.SelectAll();
                }
            };
        }

        public string Value => _isPassword ? PasswordBox.Password : ValueBox.Text;

        private void OnOk(object sender, RoutedEventArgs e)
        {
            var error = _validate?.Invoke(Value);
            if (error != null)
            {
                ErrorText.Text = error;
                ErrorText.Visibility = Visibility.Visible;
                return;
            }

            DialogResult = true;
        }

        private void OnCancel(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }

        private void OnKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                OnOk(sender, e);
            }
        }
    }
}
