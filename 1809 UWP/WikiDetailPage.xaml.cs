using System;
using Microsoft.Web.WebView2.Core;
using Windows.Foundation;
using Windows.UI.Xaml;

namespace WikiViewer.Shared.Uwp.Pages
{
    public sealed partial class WikiDetailPage
    {
        async partial void ShowVerificationPanel(string url)
        {
            var panel = this.FindName("VerificationPanel") as FrameworkElement;
            if (panel == null) return;

            panel.Visibility = Visibility.Visible;

            var webView = this.FindName("VerificationWebView") as Microsoft.UI.Xaml.Controls.WebView2;
            if (webView == null) return;

            await webView.EnsureCoreWebView2Async();

            TypedEventHandler<CoreWebView2, CoreWebView2NavigationCompletedEventArgs> successHandler = null;
            successHandler = async (sender, args) =>
            {
                if (args.IsSuccess && !sender.Source.Contains("challenges.cloudflare.com"))
                {
                    webView.CoreWebView2.NavigationCompleted -= successHandler;
                    await Dispatcher.RunAsync(
                        Windows.UI.Core.CoreDispatcherPriority.Normal,
                        () =>
                        {
                            panel.Visibility = Visibility.Collapsed;
                            DetectPathsButton_Click(null, null);
                        }
                    );
                }
            };
            webView.CoreWebView2.NavigationCompleted += successHandler;
            webView.CoreWebView2.Navigate(url);
        }
    }
}