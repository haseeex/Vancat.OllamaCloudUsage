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
            PromptText.Text = "请选择账户：";

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
