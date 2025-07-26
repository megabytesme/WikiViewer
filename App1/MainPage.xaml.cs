using System;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

using Microsoft.Web.WebView2.Core;
using System.Diagnostics;

namespace App1
{
    public sealed partial class MainPage : Page
    {
        private Microsoft.UI.Xaml.Controls.WebView2? ApiWebView;
        private bool _isFetchingApiData = false;

        public MainPage()
        {
            this.InitializeComponent();
        }

        private async void Page_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                ResultTextBlock.Text = "Initializing WebView2 engine...";
                CoreWebView2Environment webViewEnvironment = await CoreWebView2Environment.CreateAsync();
                ApiWebView = new Microsoft.UI.Xaml.Controls.WebView2();
                await ApiWebView.EnsureCoreWebView2Async();
                WebViewContainer.Child = ApiWebView;

                ApiWebView.CoreWebView2.NavigationCompleted += CoreWebView2_NavigationCompleted;
                ApiWebView.CoreWebView2.NewWindowRequested += CoreWebView2_NewWindowRequested;

                ResultTextBlock.Text = "WebView2 ready. Click the button.";
                FetchButton.IsEnabled = true;
            }
            catch (Exception ex)
            {
                ResultTextBlock.Text = "Failed to initialize WebView2: " + ex.Message;
                Debug.WriteLine("WebView2 Init Error: " + ex.ToString());
            }
        }

        private void FetchButton_Click(object sender, RoutedEventArgs e)
        {
            if (ApiWebView?.CoreWebView2 == null) return;

            ResultTextBlock.Text = "Navigating with WebView2...";
            FetchButton.IsEnabled = false;

            string apiUrl = "https://betawiki.net/api.php?action=query&list=random&rnlimit=1&format=json";

            _isFetchingApiData = true;
            ApiWebView.CoreWebView2.Navigate(apiUrl);
        }

        private void CoreWebView2_NewWindowRequested(CoreWebView2 sender, CoreWebView2NewWindowRequestedEventArgs args)
        {
            if (ApiWebView?.CoreWebView2 != null)
            {
                args.Handled = true;
                ApiWebView.CoreWebView2.Navigate(args.Uri);
            }
        }

        private async void CoreWebView2_NavigationCompleted(CoreWebView2 sender, CoreWebView2NavigationCompletedEventArgs args)
        {
            await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, async () =>
            {
                if (!_isFetchingApiData) return;

                if (args.IsSuccess)
                {
                    ResultTextBlock.Text = "Challenge passed! Extracting JSON...";
                    try
                    {
                        if (ApiWebView?.CoreWebView2 != null)
                        {
                            string script = "document.getElementsByTagName('pre')[0].innerText;";
                            string scriptResult = await ApiWebView.CoreWebView2.ExecuteScriptAsync(script);
                            string resultJson = scriptResult.Trim('"');
                            ResultTextBlock.Text = "Success! Raw JSON:\n\n" + resultJson;
                            _isFetchingApiData = false;
                            FetchButton.IsEnabled = true;
                        }
                    }
                    catch (Exception)
                    {
                        ResultTextBlock.Text = "Page loaded, but it's not the API result yet. Please solve the CAPTCHA in the view below.";
                    }
                }
                else
                {
                    ResultTextBlock.Text = "Navigation failed. Error: " + args.WebErrorStatus + ". This can happen during the CAPTCHA process.";
                }
            });
        }
    }
}