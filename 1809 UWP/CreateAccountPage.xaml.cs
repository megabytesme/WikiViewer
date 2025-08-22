using System;
using Microsoft.UI.Xaml.Controls;
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
            ErrorWebViewControl.NavigateToString(ParseWikitextToHtml(message));
            ErrorWebViewControl.Visibility = Visibility.Visible;
        }

        protected override UIElement CreateFormattedContentPresenter(string header, string content)
        {
            var panel = new StackPanel();
            if (!string.IsNullOrEmpty(header))
                panel.Children.Add(CreateWikitextBlock(header));
            string combinedContent = content ?? header;
            bool isRichContent = System.Text.RegularExpressions.Regex.IsMatch(
                combinedContent,
                @"(\[\[.*?\]\]|<.*?>|&.*?;|\/index\.php\?)"
            );
            if (isRichContent)
            {
                var webView = new WebView2 { Height = 120 };
                string htmlContent = ParseWikitextToHtml(combinedContent);
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

        private TextBlock CreateWikitextBlock(string text)
        {
            var textBlock = new TextBlock { TextWrapping = TextWrapping.Wrap };
            var parts = System.Text.RegularExpressions.Regex.Split(text, @"(\[\[.*?\]\])");
            foreach (var part in parts)
            {
                if (part.StartsWith("[[") && part.EndsWith("]]"))
                {
                    var linkContent = part.Substring(2, part.Length - 4);
                    var linkParts = linkContent.Split('|');
                    var hyperlink = new Windows.UI.Xaml.Documents.Hyperlink();
                    hyperlink.Inlines.Add(
                        new Windows.UI.Xaml.Documents.Run
                        {
                            Text = linkParts.Length > 1 ? linkParts[1] : linkParts[0],
                        }
                    );
                    hyperlink.NavigateUri = new Uri(_wikiForAccountCreation.GetWikiPageUrl(linkParts[0]));
                    hyperlink.Click += async (s, ev) =>
                        await Windows.System.Launcher.LaunchUriAsync(s.NavigateUri);
                    textBlock.Inlines.Add(hyperlink);
                }
                else
                {
                    textBlock.Inlines.Add(new Windows.UI.Xaml.Documents.Run { Text = part });
                }
            }
            return textBlock;
        }

        private string ParseWikitextToHtml(string text)
        {
            string bodyContent = text;
            if (bodyContent.StartsWith("/w/index.php?") && bodyContent.Contains("Captcha/image"))
                bodyContent =
                    $"<img src='{_wikiForAccountCreation.BaseUrl.TrimEnd('/') + bodyContent}' alt='CAPTCHA Image' />";
            else
            {
                bodyContent = System.Net.WebUtility.HtmlDecode(bodyContent);
                bodyContent = System.Text.RegularExpressions.Regex.Replace(
                    bodyContent,
                    @"(src|href)=""//",
                    match => $"{match.Groups[1].Value}=\"https://"
                );
                bodyContent = System.Text.RegularExpressions.Regex.Replace(
                    bodyContent,
                    @"\[\[(.*?)\]\]",
                    match =>
                    {
                        var parts = match.Groups[1].Value.Split('|');
                        return $"<a href='{_wikiForAccountCreation.GetWikiPageUrl(parts[0])}'>{(parts.Length > 1 ? parts[1] : parts[0])}</a>";
                    }
                );
            }
            return $"<html><head><style>body {{ font-family: 'Segoe UI', sans-serif; color: {(Application.Current.RequestedTheme == ApplicationTheme.Dark ? "white" : "black")}; background-color: transparent; margin: 0; padding: 8px; font-size: 14px; text-align: center; display: flex; align-items: center; justify-content: center; height: 100vh; }} img {{ max-width: 100%; height: auto; }} a {{ color: {(Application.Current.RequestedTheme == ApplicationTheme.Dark ? "#85B9F3" : "#0066CC")}; }}</style></head><body><div>{bodyContent}</div></body></html>";
        }
    }
}