using System;
using WikiViewer.Shared.Uwp.Pages;
using Microsoft.Web.WebView2.Core;
using Windows.Foundation;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace _1809_UWP.Pages
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
        protected override ComboBox ConnectionMethodComboBoxControl => this.ConnectionMethodComboBox;
        protected override TextBlock DetectionStatusTextBlockControl => this.DetectionStatusTextBlock;
        protected override Grid LoadingOverlayControl => this.LoadingOverlay;
        protected override TextBlock LoadingTextControl => this.LoadingText;

        protected override async void ShowVerificationPanel(string url)
        {
            var panel = this.FindName("VerificationPanel") as Grid;
            var webView = this.FindName("VerificationWebView") as Microsoft.UI.Xaml.Controls.WebView2;
            if (panel == null || webView == null) return;

            panel.Visibility = Visibility.Visible;
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