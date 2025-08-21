using System;
using Microsoft.Web.WebView2.Core;
using WikiViewer.Shared.Uwp;
using Windows.Foundation;
using Windows.UI.Xaml.Controls;

namespace _1809_UWP.Pages
{
    public sealed partial class SettingsPage
    {
        public SettingsPage() => this.InitializeComponent();

        protected override StackPanel LoggedInStatePanel => LoggedInState;
        protected override StackPanel LoggedOutStatePanel => LoggedOutState;
        protected override TextBlock UsernameTextBlock => UsernameText;
        protected override TextBlock LoggedOutStateTextBlock => LoggedOutStateTextBlockControl;
        protected override HyperlinkButton SignInHyperlink => SignInLink;
        protected override TextBox WikiUrlTextBox => WikiUrlTextBoxControl;
        protected override TextBox ScriptPathTextBox => ScriptPathTextBoxControl;
        protected override TextBox ArticlePathTextBox => ArticlePathTextBoxControl;
        protected override TextBlock DetectionStatusTextBlock => DetectionStatusText;
        protected override ToggleSwitch ConnectionMethodToggleSwitch => ConnectionMethodToggle;
        protected override ToggleSwitch CachingToggleSwitch => CachingToggle;
        protected override Slider ConcurrentDownloadsSliderControl => ConcurrentDownloadsSlider;
        protected override TextBlock ConcurrentDownloadsValueTextBlock =>
            ConcurrentDownloadsValueText;
        protected override TextBlock RamEstimateTextBlock => RamEstimateText;
        protected override TextBlock CacheSizeTextBlock => CacheSizeText;
        protected override Button ClearCacheButton => ClearCacheButtonControl;
        protected override Grid LoadingOverlayGrid => LoadingOverlay;
        protected override TextBlock LoadingOverlayTextBlock => LoadingOverlayText;
        protected override Grid VerificationPanelGrid => VerificationPanel;

        protected override Type GetLoginPageType() => typeof(_1809_UWP.LoginPage);

        protected override async void ShowVerificationPanel(string url)
        {
            VerificationPanel.Visibility = Windows.UI.Xaml.Visibility.Visible;
            await VerificationWebView.EnsureCoreWebView2Async();
            TypedEventHandler<
                CoreWebView2,
                CoreWebView2NavigationCompletedEventArgs
            > successHandler = null;
            successHandler = async (sender, args) =>
            {
                if (args.IsSuccess && !sender.Source.Contains("challenges.cloudflare.com"))
                {
                    VerificationWebView.CoreWebView2.NavigationCompleted -= successHandler;
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
            VerificationWebView.CoreWebView2.NavigationCompleted += successHandler;
            VerificationWebView.CoreWebView2.Navigate(url);
        }

        protected override void ResetAppRootFrame()
        {
            App.ResetRootFrame();
        }
    }
}
