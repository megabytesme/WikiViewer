using Microsoft.UI.Xaml.Controls;
using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using WikiViewer.Core.Services;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace _1809_UWP.Pages
{
    public sealed partial class CreateAccountPage
    {
        public CreateAccountPage() => this.InitializeComponent();

        protected override TextBlock PageTitleTextBlock => PageTitle;
        protected override StackPanel FieldsStackPanel => FieldsPanel;
        protected override Button CreateAccountButton => CreateButton;
        protected override Windows.UI.Xaml.Controls.ProgressRing LoadingProgressRing => LoadingRing;
        private WebView2 ErrorWebViewControl => ErrorWebView;

        protected override async void ShowError(string message)
        {
            await ErrorWebViewControl.EnsureCoreWebView2Async();
            string html = WikitextParsingService.ParseToFullHtmlDocument(message, _wikiForAccountCreation, Application.Current.RequestedTheme == ApplicationTheme.Dark);
            ErrorWebViewControl.NavigateToString(html);
            ErrorWebViewControl.Visibility = Visibility.Visible;
        }

        protected override async Task ShowComplexErrorDialogAsync(string title, string wikitextContent)
        {
            var webView = new WebView2 { Height = 200 };
            string htmlContent = WikitextParsingService.ParseToFullHtmlDocument(wikitextContent, _wikiForAccountCreation, Application.Current.RequestedTheme == ApplicationTheme.Dark);

            webView.Loaded += async (s, ev) =>
            {
                var wv = s as WebView2;
                await wv.EnsureCoreWebView2Async();
                wv.CoreWebView2.NavigationStarting += async (c, args) =>
                {
                    if (!string.IsNullOrEmpty(args.Uri) && args.Uri.StartsWith("http"))
                    {
                        args.Cancel = true;
                        await Windows.System.Launcher.LaunchUriAsync(new Uri(args.Uri));
                    }
                };
                wv.NavigateToString(htmlContent);
            };

            var dialog = new ContentDialog
            {
                Title = title,
                Content = webView,
                CloseButtonText = "Close"
            };
            await dialog.ShowAsync();
        }

        protected override UIElement CreateFormattedContentPresenter(string header, string content)
        {
            var panel = new StackPanel();
            if (!string.IsNullOrEmpty(header) && string.IsNullOrEmpty(content))
                panel.Children.Add(new TextBlock { Text = header, TextWrapping = TextWrapping.Wrap });

            string combinedContent = content ?? header;
            bool isRichContent = Regex.IsMatch(combinedContent, @"(\[\[.*?\]\]|<.*?>|&.*?;|\/index\.php\?)");

            if (isRichContent)
            {
                var webView = new WebView2 { Height = 120 };
                string htmlContent = WikitextParsingService.ParseToFullHtmlDocument(combinedContent, _wikiForAccountCreation, Application.Current.RequestedTheme == ApplicationTheme.Dark);
                webView.Loaded += async (s, ev) =>
                {
                    var wv = s as WebView2;
                    await wv.EnsureCoreWebView2Async();
                    wv.CoreWebView2.NavigationStarting += async (c, args) =>
                    {
                        if (!string.IsNullOrEmpty(args.Uri) && args.Uri.StartsWith("http"))
                        {
                            args.Cancel = true;
                            await Windows.System.Launcher.LaunchUriAsync(new Uri(args.Uri));
                        }
                    };
                    wv.NavigateToString(htmlContent);
                };
                panel.Children.Add(webView);
            }
            else
            {
                panel.Children.Add(new TextBox { Text = combinedContent, IsReadOnly = true, BorderThickness = new Thickness(0), TextWrapping = TextWrapping.Wrap, AcceptsReturn = true, Height = Double.NaN });
            }
            return panel;
        }
    }
}