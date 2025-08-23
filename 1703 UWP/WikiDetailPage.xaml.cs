using System;
using WikiViewer.Shared.Uwp.Pages;
using Windows.Foundation;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace _1703_UWP
{
    public sealed partial class WikiDetailPage : WikiDetailPageBase
    {
        public WikiDetailPage()
        {
            this.InitializeComponent();
        }

        protected override TextBlock PageTitleTextBlockControl => this.PageTitle;
        protected override TextBox WikiNameTextBoxControl => this.WikiNameTextBox;
        protected override TextBox WikiUrlTextBoxControl => this.WikiUrlTextBox;
        protected override TextBox ScriptPathTextBoxControl => this.ScriptPathTextBox;
        protected override TextBox ArticlePathTextBoxControl => this.ArticlePathTextBox;
        protected override ToggleSwitch ConnectionMethodToggleSwitchControl => this.ConnectionMethodToggleSwitch;
        protected override TextBlock DetectionStatusTextBlockControl => this.DetectionStatusTextBlock;
        protected override Grid LoadingOverlayControl => this.LoadingOverlay;
        protected override TextBlock LoadingTextControl => this.LoadingText;

        protected override void ShowVerificationPanel(string url)
        {
            var panel = this.FindName("VerificationPanel") as Grid;
            var webView = this.FindName("VerificationWebView") as WebView;
            if (panel == null || webView == null) return;

            panel.Visibility = Visibility.Visible;

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