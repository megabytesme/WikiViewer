using System;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;
using Microsoft.Web.WebView2.Core;

namespace App1
{
    public sealed partial class MainPage : Page
    {
        private bool _isAttemptingSilentFetch = false;
        private const string ApiUrl = "https://betawiki.net/api.php?action=query&list=random&rnlimit=1&format=json";

        public MainPage()
        {
            this.InitializeComponent();
            InitializeSilentWebViewAsync();
        }

        private async void InitializeSilentWebViewAsync()
        {
            try
            {
                await SilentWebView.EnsureCoreWebView2Async();
                SilentWebView.CoreWebView2.NavigationCompleted += CoreWebView2_SilentNavigationCompleted;
            }
            catch (Exception ex)
            {
                ResultTextBlock.Text = "Critical Error: Could not initialize WebView2 engine. " + ex.Message;
                FetchButton.IsEnabled = false;
            }
        }

        private void FetchButton_Click(object sender, RoutedEventArgs e)
        {
            LoadingRing.IsActive = true;
            FetchButton.IsEnabled = false;
            ResultTextBlock.Text = "Loading...";

            _isAttemptingSilentFetch = true;
            SilentWebView.CoreWebView2.Navigate(ApiUrl);
        }

        private async void CoreWebView2_SilentNavigationCompleted(CoreWebView2 sender, CoreWebView2NavigationCompletedEventArgs args)
        {
            if (!_isAttemptingSilentFetch) return;

            await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, async () =>
            {
                try
                {
                    string script = "document.getElementsByTagName('pre')[0].innerText;";
                    string scriptResult = await SilentWebView.CoreWebView2.ExecuteScriptAsync(script);
                    string resultJson = scriptResult.Trim('"');
                    ResultTextBlock.Text = resultJson;
                    ResetUi();
                }
                catch (Exception)
                {
                    ResultTextBlock.Text = "Verification needed. Opening verification page...";
                    Frame.Navigate(typeof(VerificationPage), ApiUrl);
                }
            });
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

            if (e.NavigationMode == NavigationMode.Back && App.VerificationResult != null)
            {
                ResultTextBlock.Text = App.VerificationResult;
                App.VerificationResult = null;
            }

            ResetUi();
        }

        private void ResetUi()
        {
            _isAttemptingSilentFetch = false;
            LoadingRing.IsActive = false;
            FetchButton.IsEnabled = true;
        }
    }
}