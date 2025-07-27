using System;
using System.Diagnostics;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace _1809_UWP
{
    public class LoginApiTokenResponse { public QueryLoginToken query { get; set; } }
    public class QueryLoginToken { public TokensLogin tokens { get; set; } }
    public class TokensLogin { public string logintoken { get; set; } }
    public class LoginResultResponse { public LoginData login { get; set; } }
    public class LoginData { public string result { get; set; } public string lgusername { get; set; } }
    public class CsrfTokenResponse { public QueryCsrf query { get; set; } }
    public class QueryCsrf { public TokensCsrf tokens { get; set; } }
    public class TokensCsrf { public string csrftoken { get; set; } }

    public sealed partial class LoginPage : Page
    {
        private const string ApiUrl = "https://betawiki.net/api.php";
        private readonly ApiHelper _apiHelper = (Window.Current.Content as Frame).Content is MainPage mainPage ? new ApiHelper(mainPage.PublicApiWebView.CoreWebView2) : null;

        public LoginPage()
        {
            this.InitializeComponent();
            this.Loaded += (s, e) => {
                if (_apiHelper == null)
                {
                    ErrorTextBlock.Text = "Critical Error: ApiHelper could not be initialized.";
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

            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password) || _apiHelper == null)
            {
                ErrorTextBlock.Text = "Username and password are required.";
                LoadingRing.IsActive = false;
                LoginButton.IsEnabled = true;
                return;
            }

            try
            {
                await AuthService.PerformLoginAsync(username, password, _apiHelper);

                if (RememberMeCheckBox.IsChecked == true)
                {
                    CredentialService.SaveCredentials(username, password);
                    Debug.WriteLine("[LoginPage] Credentials saved.");
                }
                else
                {
                    CredentialService.ClearCredentials();
                    Debug.WriteLine("[LoginPage] Credentials cleared.");
                }

                if (this.Frame.CanGoBack)
                {
                    this.Frame.GoBack();
                }
            }
            catch (Exception ex)
            {
                ErrorTextBlock.Text = $"An error occurred: {ex.Message}";
                Debug.WriteLine($"[LoginPage] EXCEPTION: {ex.ToString()}");
            }
            finally
            {
                LoadingRing.IsActive = false;
                LoginButton.IsEnabled = true;
            }
        }
    }
}