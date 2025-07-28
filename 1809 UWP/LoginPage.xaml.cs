using System;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace _1809_UWP
{
    public sealed partial class LoginPage : Page
    {
        public LoginPage()
        {
            this.InitializeComponent();
            this.Loaded += (s, e) =>
            {
                if (!WebViewApiService.IsInitialized)
                {
                    ErrorTextBlock.Text = "Critical Error: API Service could not be initialized.";
                    LoginButton.IsEnabled = false;
                }
            };
        }

        private async void LoginButton_Click(object sender, RoutedEventArgs e)
        {
            ErrorTextBlock.Text = "";
            LoadingRing.IsActive = true;
            LoginButton.IsEnabled = false;
            string username = UsernameBox.Text;
            string password = PasswordBox.Password;

            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            {
                ErrorTextBlock.Text = "Username and password are required.";
                LoadingRing.IsActive = false; LoginButton.IsEnabled = true; return;
            }

            try
            {
                await AuthService.PerformLoginAsync(username, password);
                if (RememberMeCheckBox.IsChecked == true) { CredentialService.SaveCredentials(username, password); }
                else { CredentialService.ClearCredentials(); }
                if (this.Frame.CanGoBack) { this.Frame.GoBack(); }
            }
            catch (NeedsUserVerificationException)
            {
                ErrorTextBlock.Text = "Verification needed. Please go back and try another action first to solve the security check.";
            }
            catch (Exception ex)
            {
                ErrorTextBlock.Text = $"An error occurred: {ex.Message}";
            }
            finally
            {
                LoadingRing.IsActive = false;
                LoginButton.IsEnabled = true;
            }
        }
    }
}