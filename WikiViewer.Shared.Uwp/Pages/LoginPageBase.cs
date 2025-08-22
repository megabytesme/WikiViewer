using System;
using System.Threading.Tasks;
using WikiViewer.Core;
using WikiViewer.Core.Models;
using WikiViewer.Core.Services;
using WikiViewer.Shared.Uwp.Services;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;

namespace WikiViewer.Shared.Uwp.Pages
{
    public abstract class LoginPageBase : Page
    {
        protected abstract TextBlock LoginTitleTextBlock { get; }
        protected abstract TextBox UsernameTextBox { get; }
        protected abstract PasswordBox UserPasswordBox { get; }
        protected abstract CheckBox RememberMeCheckBoxControl { get; }
        protected abstract Button LoginButtonControl { get; }
        protected abstract ProgressRing LoadingProgressRing { get; }
        protected abstract TextBlock ErrorTextBlockControl { get; }
        protected abstract Type GetCreateAccountPageType();
        protected abstract Task ShowInteractiveLoginAsync(
            AuthUiRequiredException authException,
            Account account
        );

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            LoginTitleTextBlock.Text = $"Log In to {SessionManager.CurrentWiki.Host}";
        }

        protected async void LoginButton_Click(object sender, RoutedEventArgs e)
        {
            ErrorTextBlockControl.Text = "";
            LoadingProgressRing.IsActive = true;
            LoginButtonControl.IsEnabled = false;
            string username = UsernameTextBox.Text;
            string password = UserPasswordBox.Password;

            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            {
                ErrorTextBlockControl.Text = "Username and password are required.";
                LoadingProgressRing.IsActive = false;
                LoginButtonControl.IsEnabled = true;
                return;
            }

            var account = new Account
            {
                Username = username,
                WikiInstanceId = SessionManager.CurrentWiki.Id,
            };

            var authService = new AuthenticationService(
                account,
                SessionManager.CurrentWiki,
                App.ApiWorkerFactory
            );

            try
            {
                await authService.LoginAsync(password);

                await AccountManager.AddAccountAsync(account, password);

                if (RememberMeCheckBoxControl.IsChecked == false)
                {
                    CredentialService.ClearCredentials(account.Id);
                }

                if (this.Frame.CanGoBack)
                {
                    this.Frame.GoBack();
                }
            }
            catch (AuthUiRequiredException ex)
            {
                ErrorTextBlockControl.Text = "An additional verification step is required.";
                await ShowInteractiveLoginAsync(ex, account);
            }
            catch (Exception ex)
            {
                ErrorTextBlockControl.Text = $"An error occurred: {ex.Message}";
            }
            finally
            {
                LoadingProgressRing.IsActive = false;
                LoginButtonControl.IsEnabled = true;
            }
        }

        protected void CreateAccountButton_Click(object sender, RoutedEventArgs e)
        {
            Frame.Navigate(GetCreateAccountPageType());
        }
    }
}
