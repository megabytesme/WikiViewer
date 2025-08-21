using System;
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

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            LoginTitleTextBlock.Text = $"Log In to {AppSettings.Host}";
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

            try
            {
                await AuthService.PerformLoginAsync(username, password);
                if (RememberMeCheckBoxControl.IsChecked == true)
                    CredentialService.SaveCredentials(username, password);
                else
                    CredentialService.ClearCredentials();
                if (this.Frame.CanGoBack)
                    this.Frame.GoBack();
            }
            catch (NeedsUserVerificationException)
            {
                ErrorTextBlockControl.Text =
                    "Verification needed. Please go back and try another action first to solve the security check.";
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
