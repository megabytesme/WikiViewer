using WikiViewer.Uwp.Controls;
using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using WikiViewer.Core.Services;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace WikiViewer.Uwp.Pages
{
    public sealed partial class CreateAccountPage
    {
        public CreateAccountPage() => this.InitializeComponent();

        protected override TextBlock PageTitleTextBlock => PageTitle;
        protected override Panel FieldsStackPanel => FieldsPanel;
        protected override Button CreateAccountButton => CreateButton;
        protected override Windows.UI.Xaml.Controls.ProgressRing LoadingProgressRing => LoadingRing;
        private WikiBrowser ErrorWebViewControl => ErrorWebView;

        protected override async void ShowError(string message)
        {
            await ErrorWebViewControl.EnsureInitializedAsync();
            string html = WikitextParsingService.ParseToFullHtmlDocument(message, _wikiForAccountCreation, Application.Current.RequestedTheme == ApplicationTheme.Dark);
            await ErrorWebViewControl.ShowHtmlAsync(html);
            ErrorWebViewControl.Visibility = Visibility.Visible;
        }

        protected override async Task ShowComplexErrorDialogAsync(string title, string wikitextContent)
        {
            var webView = new WikiBrowser { Height = 200 };
            string htmlContent = WikitextParsingService.ParseToFullHtmlDocument(wikitextContent, _wikiForAccountCreation, Application.Current.RequestedTheme == ApplicationTheme.Dark);

            webView.Loaded += async (s, ev) =>
            {
                var wv = s as WikiBrowser;
                await wv.EnsureInitializedAsync();
                wv.NavigationStarting += async (c, args) =>
                {
                    if (args.Uri != null && (args.Uri.Scheme == "https" || args.Uri.Scheme == "http"))
                    {
                        args.Cancel = true;
                        await Windows.System.Launcher.LaunchUriAsync(args.Uri);
                    }
                };
                await wv.ShowHtmlAsync(htmlContent);
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
                var webView = new WikiBrowser { Height = 120 };
                string htmlContent = WikitextParsingService.ParseToFullHtmlDocument(combinedContent, _wikiForAccountCreation, Application.Current.RequestedTheme == ApplicationTheme.Dark);
                webView.Loaded += async (s, ev) =>
                {
                    var wv = s as WikiBrowser;
                    await wv.EnsureInitializedAsync();
                    wv.NavigationStarting += async (c, args) =>
                    {
                        if (args.Uri != null && (args.Uri.Scheme == "https" || args.Uri.Scheme == "http"))
                        {
                            args.Cancel = true;
                            await Windows.System.Launcher.LaunchUriAsync(args.Uri);
                        }
                    };
                    await wv.ShowHtmlAsync(htmlContent);
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