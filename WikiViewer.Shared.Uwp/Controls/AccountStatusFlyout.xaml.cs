using System;
using System.Linq;
using WikiViewer.Core.Models;
using WikiViewer.Core.Services;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Data;

namespace WikiViewer.Shared.Uwp.Controls
{
    public sealed partial class AccountStatusFlyout : UserControl
    {
        public event Action<Guid> RequestSignIn;
        public event Action<Guid> RequestSignOut;

        public AccountStatusFlyout()
        {
            this.InitializeComponent();
            this.Loaded += OnLoaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            AccountListView.ItemsSource = WikiManager.GetWikis();
        }

        private void SignInOutButton_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is WikiInstance wiki)
            {
                var account = AccountManager.GetAccountsForWiki(wiki.Id).FirstOrDefault(a => a.IsLoggedIn);
                if (account != null)
                {
                    RequestSignOut?.Invoke(account.Id);
                }
                else
                {
                    RequestSignIn?.Invoke(wiki.Id);
                }
            }
        }
    }

    public class AccountStatusConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is WikiInstance wiki && parameter is string param)
            {
                var account = AccountManager.GetAccountsForWiki(wiki.Id).FirstOrDefault(a => a.IsLoggedIn);
                if (param == "Username")
                {
                    return account?.Username ?? "Not signed in";
                }
                if (param == "ButtonText")
                {
                    return account != null ? "Sign Out" : "Sign In";
                }
            }
            return "";
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }
}