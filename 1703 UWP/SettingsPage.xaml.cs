using System;
using WikiViewer.Shared.Uwp;
using WikiViewer.Shared.Uwp.Pages;
using Windows.Foundation;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace _1703_UWP.Pages
{
    public sealed partial class SettingsPage : SettingsPageBase
    {
        public SettingsPage() => this.InitializeComponent();

        protected override ListView WikiListView => WikiListViewControl;
        protected override Panel DetailPanel => DetailPanelControl;
        protected override Button AddWikiButton => AddWikiButtonControl;
        protected override Button RemoveWikiButton => RemoveWikiButtonControl;
        protected override TextBox WikiNameTextBox => WikiNameTextBoxControl;
        protected override TextBox WikiUrlTextBox => WikiUrlTextBoxControl;
        protected override TextBox ScriptPathTextBox => ScriptPathTextBoxControl;
        protected override TextBox ArticlePathTextBox => ArticlePathTextBoxControl;
        protected override ToggleSwitch ConnectionMethodToggleSwitch => ConnectionMethodToggleSwitchControl;
        protected override TextBlock DetectionStatusTextBlock => DetectionStatusTextControl;
        protected override Button ApplyWikiChangesButton => ApplyWikiChangesButtonControl;
        protected override Button DetectPathsButton => DetectPathsButtonControl;
        protected override ListView AccountListView => AccountListViewControl;
        protected override Button AddAccountButton => AddAccountButtonControl;
        protected override Grid LoadingOverlay => LoadingOverlayControl;
        protected override TextBlock LoadingText => LoadingTextControl;
        protected override Grid VerificationPanel => VerificationPanelControl;

        protected override Type GetLoginPageType() => typeof(_1703_UWP.LoginPage);

        protected override void ShowVerificationPanel(string url)
        {
            VerificationPanelControl.Visibility = Visibility.Visible;
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
                            VerificationPanelControl.Visibility = Visibility.Collapsed;
                            DetectPathsButton_Click(null, null);
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