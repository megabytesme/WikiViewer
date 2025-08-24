using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using WikiViewer.Core.Services;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace _1703_UWP.Pages
{
    public sealed partial class CreateAccountPage
    {
        public CreateAccountPage() => this.InitializeComponent();

        protected override TextBlock PageTitleTextBlock => PageTitle;
        protected override StackPanel FieldsStackPanel => FieldsPanel;
        protected override Button CreateAccountButton => CreateButton;
        protected override ProgressRing LoadingProgressRing => LoadingRing;
        private WebView ErrorWebViewControl => ErrorWebView;

        protected override void ShowError(string message)
        {
            string html = WikitextParsingService.ParseToFullHtmlDocument(
                message,
                _wikiForAccountCreation,
                Application.Current.RequestedTheme == ApplicationTheme.Dark
            );
            ErrorWebViewControl.NavigateToString(html);
            ErrorWebViewControl.Visibility = Visibility.Visible;
        }

        protected override async Task ShowComplexErrorDialogAsync(
            string title,
            string wikitextContent
        )
        {
            var webView = new WebView { Height = 200 };
            string htmlContent = WikitextParsingService.ParseToFullHtmlDocument(
                wikitextContent,
                _wikiForAccountCreation,
                Application.Current.RequestedTheme == ApplicationTheme.Dark
            );

            webView.NavigationStarting += async (s, ev) =>
            {
                if (ev.Uri != null)
                {
                    ev.Cancel = true;
                    await Windows.System.Launcher.LaunchUriAsync(ev.Uri);
                }
            };
            webView.NavigateToString(htmlContent);

            var dialog = new ContentDialog
            {
                Title = title,
                Content = webView,
                CloseButtonText = "Close",
            };
            await dialog.ShowAsync();
        }

        protected override UIElement CreateFormattedContentPresenter(string header, string content)
        {
            var panel = new StackPanel();
            if (!string.IsNullOrEmpty(header) && string.IsNullOrEmpty(content))
                panel.Children.Add(
                    new TextBlock { Text = header, TextWrapping = TextWrapping.Wrap }
                );

            string combinedContent = content ?? header;
            bool isRichContent = Regex.IsMatch(
                combinedContent,
                @"(\[\[.*?\]\]|<.*?>|&.*?;|\/index\.php\?)"
            );

            if (isRichContent)
            {
                var webView = new WebView { Height = 120 };
                string htmlContent = WikitextParsingService.ParseToFullHtmlDocument(
                    combinedContent,
                    _wikiForAccountCreation,
                    Application.Current.RequestedTheme == ApplicationTheme.Dark
                );
                webView.NavigationStarting += async (s, ev) =>
                {
                    if (ev.Uri != null)
                    {
                        ev.Cancel = true;
                        await Windows.System.Launcher.LaunchUriAsync(ev.Uri);
                    }
                };
                webView.NavigateToString(htmlContent);
                panel.Children.Add(webView);
            }
            else
            {
                panel.Children.Add(
                    new TextBox
                    {
                        Text = combinedContent,
                        IsReadOnly = true,
                        BorderThickness = new Thickness(0),
                        TextWrapping = TextWrapping.Wrap,
                        AcceptsReturn = true,
                        Height = Double.NaN,
                    }
                );
            }
            return panel;
        }
    }
}
