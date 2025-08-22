using System;
using Windows.Foundation;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace WikiViewer.Shared.Uwp.Pages
{
    public sealed partial class WikiDetailPage
    {
        partial void ShowVerificationPanel(string url)
        {
            var panel = this.FindName("VerificationPanel") as FrameworkElement;
            if (panel == null) return;

            panel.Visibility = Visibility.Visible;

            var webView = this.FindName("VerificationWebView") as WebView;
            if (webView == null) return;

            TypedEventHandler<WebView, WebViewNavigationCompletedEventArgs> successHandler = null;
            successHandler = async (sender, args) =>
            {
                if (args.IsSuccess && !args.Uri.Host.Contains("challenges.cloudflare.com"))
                {
                    webView.NavigationCompleted -= successHandler;
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
            webView.NavigationCompleted += successHandler;
            webView.Navigate(new Uri(url));
        }
    }
}