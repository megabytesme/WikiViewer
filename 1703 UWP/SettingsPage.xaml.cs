using System;
using WikiViewer.Shared.Uwp;
using WikiViewer.Shared.Uwp.Pages;
using Windows.Foundation;
using Windows.UI.Xaml.Controls;

namespace _1703_UWP.Pages
{
    public sealed partial class SettingsPage : SettingsPageBase
    {
        public SettingsPage() => this.InitializeComponent();

        protected override StackPanel LoggedInStatePanel => LoggedInState;
        protected override StackPanel LoggedOutStatePanel => LoggedOutState;
        protected override TextBlock UsernameTextBlock => UsernameText;
        protected override HyperlinkButton SignInHyperlink => SignInLink;
        protected override TextBlock DetectionStatusTextBlock => DetectionStatusText;
        protected override ToggleSwitch ConnectionMethodToggleSwitch => ConnectionMethodToggle;
        protected override ToggleSwitch CachingToggleSwitch => CachingToggle;
        protected override Slider ConcurrentDownloadsSliderControl => ConcurrentDownloadsSlider;
        protected override TextBlock ConcurrentDownloadsValueTextBlock =>
            ConcurrentDownloadsValueText;
        protected override TextBlock RamEstimateTextBlock => RamEstimateText;
        protected override TextBlock CacheSizeTextBlock => CacheSizeText;
        protected override Grid LoadingOverlayGrid => LoadingOverlay;
        protected override TextBlock LoadingOverlayTextBlock => LoadingOverlayText;
        protected override Grid VerificationPanelGrid => VerificationPanel;
        protected override TextBlock LoggedOutStateTextBlock => LoggedOutStateTextBlockControl;
        protected override TextBox WikiUrlTextBox => WikiUrlTextBoxControl;
        protected override TextBox ScriptPathTextBox => ScriptPathTextBoxControl;
        protected override TextBox ArticlePathTextBox => ArticlePathTextBoxControl;
        protected override Button ClearCacheButton => ClearCacheButtonControl;

        protected override Type GetLoginPageType() => typeof(_1703_UWP.LoginPage);

        protected override void ShowVerificationPanel(string url)
        {
            VerificationPanel.Visibility = Windows.UI.Xaml.Visibility.Visible;
            var webView = this.FindName("VerificationWebView") as WebView;
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
                            VerificationPanel.Visibility = Windows.UI.Xaml.Visibility.Collapsed;
                            DetectButton_Click(null, null);
                        }
                    );
                }
            };
            webView.NavigationCompleted += successHandler;
            webView.Navigate(new Uri(url));
        }

        protected override void ResetAppRootFrame()
        {
            App.ResetRootFrame();
        }
    }
}
