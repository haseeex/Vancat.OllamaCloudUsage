using System.Windows;
using System.Windows.Input;
using Microsoft.VisualStudio.PlatformUI;
using Vancat.OllamaCloudUsage.Services;

namespace Vancat.OllamaCloudUsage
{
    /// <summary>账户选择对话框。</summary>
    public partial class AccountPickerDialog : DialogWindow
    {
        public AccountPickerDialog(string title, AccountsState state)
        {
            InitializeComponent();
            Title = title;
            PromptText.Text = Loc.T("Dlg.PickAccountPrompt");

            // 按钮文本跟随当前语言。
            OkButton.Content = Loc.T("Dlg.Ok");
            CancelButton.Content = Loc.T("Dlg.Cancel");

            foreach (var account in state.Accounts)
            {
                AccountList.Items.Add(account);
            }

            AccountList.SelectedItem = state.Active;
            if (AccountList.SelectedIndex < 0 && AccountList.Items.Count > 0)
            {
                AccountList.SelectedIndex = 0;
            }
        }

        public Account Selected => AccountList.SelectedItem as Account;

        private void OnOk(object sender, RoutedEventArgs e)
        {
            if (Selected == null)
            {
                return;
            }

            DialogResult = true;
        }

        private void OnCancel(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }

        private void OnDoubleClick(object sender, MouseButtonEventArgs e)
        {
            OnOk(sender, e);
        }
    }
}
