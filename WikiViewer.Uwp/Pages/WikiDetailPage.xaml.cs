using System;
using WikiViewer.Uwp.Pages;
using WikiViewer.Uwp.Controls;
using Windows.Foundation;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace WikiViewer.Uwp.Pages
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
        protected override Image IconPreviewImageControl => this.IconPreviewImage;
        protected override Button SetIconButtonControl => this.SetIconButton;
        protected override Button RemoveIconButtonControl => this.RemoveIconButton;

        protected override async void ShowVerificationPanel(string url)
        {
            var panel = this.FindName("VerificationPanel") as Grid;
            var webView = this.FindName("VerificationWebView") as WikiBrowser;
            if (panel == null || webView == null) return;

            panel.Visibility = Visibility.Visible;
            await webView.EnsureInitializedAsync();

            EventHandler<WikiNavigationCompletedEventArgs> successHandler = null;
            successHandler = async (sender, args) =>
            {
                if (args.IsSuccess && args.Uri != null && args.Uri.Host != "challenges.cloudflare.com")
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
            await webView.NavigateAsync(new Uri(url));
        }
    }
}