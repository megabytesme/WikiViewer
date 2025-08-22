using System;
using Microsoft.Web.WebView2.Core;
using WikiViewer.Shared.Uwp;
using WikiViewer.Shared.Uwp.Pages;
using Windows.Foundation;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace _1809_UWP.Pages
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

        protected override Type GetLoginPageType() => typeof(_1809_UWP.LoginPage);

        protected override async void ShowVerificationPanel(string url)
        {
            VerificationPanelControl.Visibility = Visibility.Visible;
            await VerificationWebView.EnsureCoreWebView2Async();
            TypedEventHandler<CoreWebView2, CoreWebView2NavigationCompletedEventArgs> successHandler = null;
            successHandler = async (sender, args) =>
            {
                if (args.IsSuccess && !sender.Source.Contains("challenges.cloudflare.com"))
                {
                    VerificationWebView.CoreWebView2.NavigationCompleted -= successHandler;
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
            VerificationWebView.CoreWebView2.NavigationCompleted += successHandler;
            VerificationWebView.CoreWebView2.Navigate(url);
        }

        protected override void ResetAppRootFrame()
        {
            App.ResetRootFrame();
        }
    }
}