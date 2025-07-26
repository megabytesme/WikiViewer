using System;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;
using Microsoft.Web.WebView2.Core;
using System.Diagnostics;

namespace App1
{
    public sealed partial class VerificationPage : Page
    {
        private string? _targetUrl;

        public VerificationPage()
        {
            this.InitializeComponent();
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            if (e.Parameter is string url)
            {
                _targetUrl = url;
            }
        }

        private async void Page_Loaded(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_targetUrl))
            {
                Frame.GoBack();
                return;
            }

            try
            {
                await CaptchaWebView.EnsureCoreWebView2Async();
                CaptchaWebView.CoreWebView2.NavigationCompleted += CoreWebView2_NavigationCompleted;
                CaptchaWebView.CoreWebView2.NewWindowRequested += CoreWebView2_NewWindowRequested;
                CaptchaWebView.CoreWebView2.Navigate(_targetUrl);
            }
            catch (Exception ex)
            {
                App.VerificationResult = "ERROR: " + ex.Message;
                Frame.GoBack();
            }
        }

        private async void CoreWebView2_NavigationCompleted(CoreWebView2 sender, CoreWebView2NavigationCompletedEventArgs args)
        {
            await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, async () =>
            {
                LoadingRing.IsActive = false;

                if (args.IsSuccess)
                {
                    try
                    {
                        string script = "document.getElementsByTagName('pre')[0].innerText;";
                        string scriptResult = await CaptchaWebView.CoreWebView2.ExecuteScriptAsync(script);
                        string resultJson = scriptResult.Trim('"');
                        App.VerificationResult = resultJson;
                        Frame.GoBack();
                    }
                    catch (Exception)
                    {
                        Debug.WriteLine("Script failed, likely on CAPTCHA page. Waiting for user.");
                    }
                }
            });
        }

        private void CoreWebView2_NewWindowRequested(CoreWebView2 sender, CoreWebView2NewWindowRequestedEventArgs args)
        {
            args.Handled = true;
            CaptchaWebView.CoreWebView2.Navigate(args.Uri);
        }
    }
}