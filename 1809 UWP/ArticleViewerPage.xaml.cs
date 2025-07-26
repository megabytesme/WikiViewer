using System;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using HtmlAgilityPack;
using Microsoft.Web.WebView2.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;

namespace _1809_UWP
{
    public class RandomQueryResponse { public QueryResult query { get; set; } }
    public class QueryResult { public RandomPage[] random { get; set; } }
    public class RandomPage { public string title { get; set; } }
    public class ApiParseResponse { public ParseResult parse { get; set; } }
    public class ParseResult { public string title { get; set; } public TextContent text { get; set; } }
    public class TextContent { [JsonPropertyName("*")] public string Content { get; set; } }

    public sealed partial class ArticleViewerPage : Page
    {
        private enum FetchStep { Idle, GetRandomTitle, ParseArticleContent }
        private FetchStep _currentFetchStep = FetchStep.Idle;
        private string _pageTitleToFetch = "";
        private const string ApiBaseUrl = "https://betawiki.net/api.php";
        private bool _isInitialized = false;

        public ArticleViewerPage()
        {
            this.InitializeComponent();
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            if (e.Parameter is string pageTitle && !string.IsNullOrEmpty(pageTitle))
            {
                _pageTitleToFetch = pageTitle;
            }
        }

        private async void Page_Loaded(object sender, RoutedEventArgs e)
        {
            if (_isInitialized) return;

            try
            {
                Environment.SetEnvironmentVariable("WEBVIEW2_DEFAULT_BACKGROUND_COLOR", "00FFFFFF");

                await SilentFetchView.EnsureCoreWebView2Async();
                await ArticleDisplayWebView.EnsureCoreWebView2Async();

                SilentFetchView.CoreWebView2.NavigationCompleted += CoreWebView2_NavigationCompleted;
                _isInitialized = true;

                if (!string.IsNullOrEmpty(_pageTitleToFetch))
                {
                    StartArticleFetch();
                }
            }
            catch (Exception ex)
            {
                ArticleTitle.Text = "Error initializing WebView2: " + ex.Message;
                LoadingOverlay.Visibility = Visibility.Collapsed;
            }
        }

        private void StartArticleFetch()
        {
            if (!_isInitialized) return;

            LoadingOverlay.Visibility = Visibility.Visible;
            LoadingText.Text = $"Fetching: '{_pageTitleToFetch}'...";
            Debug.WriteLine($"[LOG] Starting fetch for: '{_pageTitleToFetch}'");

            string apiUrl;
            if (_pageTitleToFetch.Equals("random", StringComparison.OrdinalIgnoreCase))
            {
                _currentFetchStep = FetchStep.GetRandomTitle;
                apiUrl = $"{ApiBaseUrl}?action=query&list=random&rnnamespace=0&rnlimit=1&format=json";
            }
            else
            {
                _currentFetchStep = FetchStep.ParseArticleContent;
                apiUrl = $"{ApiBaseUrl}?action=parse&page={Uri.EscapeDataString(_pageTitleToFetch)}&format=json";
            }

            Debug.WriteLine($"[LOG] Navigating WebView to: {apiUrl}");
            SilentFetchView.CoreWebView2.Navigate(apiUrl);
        }

        private async void CoreWebView2_NavigationCompleted(CoreWebView2 sender, CoreWebView2NavigationCompletedEventArgs args)
        {
            if (_currentFetchStep == FetchStep.Idle) return;

            await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, async () =>
            {
                if (!args.IsSuccess)
                {
                    LoadingOverlay.Visibility = Visibility.Collapsed;
                    return;
                }

                try
                {
                    string script = "document.body.innerText;";
                    string scriptResult = await SilentFetchView.CoreWebView2.ExecuteScriptAsync(script);
                    string resultJson = JsonSerializer.Deserialize<string>(scriptResult);

                    if (string.IsNullOrEmpty(resultJson))
                    {
                        throw new Exception("WebView returned empty content.");
                    }

                    if (_currentFetchStep == FetchStep.GetRandomTitle)
                    {
                        var randomResponse = JsonSerializer.Deserialize<RandomQueryResponse>(resultJson);
                        string randomTitle = randomResponse?.query?.random?.FirstOrDefault()?.title;

                        if (string.IsNullOrEmpty(randomTitle))
                        {
                            throw new Exception("Failed to get a random title from the API.");
                        }

                        _pageTitleToFetch = randomTitle;
                        _currentFetchStep = FetchStep.ParseArticleContent;
                        LoadingText.Text = $"Parsing: '{_pageTitleToFetch}'...";
                        string parseUrl = $"{ApiBaseUrl}?action=parse&page={Uri.EscapeDataString(_pageTitleToFetch)}&format=json";
                        SilentFetchView.CoreWebView2.Navigate(parseUrl);
                    }
                    else if (_currentFetchStep == FetchStep.ParseArticleContent)
                    {
                        var apiResponse = JsonSerializer.Deserialize<ApiParseResponse>(resultJson);
                        string htmlContent = apiResponse?.parse?.text?.Content;
                        string articleTitle = apiResponse?.parse?.title;

                        if (string.IsNullOrEmpty(htmlContent) || string.IsNullOrEmpty(articleTitle))
                        {
                            throw new Exception("API response did not contain valid title or content.");
                        }

                        ArticleTitle.Text = articleTitle;
                        string processedHtml = ProcessHtmlForWebView(htmlContent);
                        ArticleDisplayWebView.NavigateToString(processedHtml);

                        LoadingOverlay.Visibility = Visibility.Collapsed;
                        _currentFetchStep = FetchStep.Idle;
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[LOG] JSON Parse FAILED: {ex.Message}. Assuming it's a CAPTCHA page. Waiting for user interaction.");
                    LoadingOverlay.Visibility = Visibility.Collapsed;
                }
            });
        }

        private string ProcessHtmlForWebView(string rawHtml)
        {
            var doc = new HtmlDocument();
            doc.LoadHtml(rawHtml);

            string baseUrl = "https://betawiki.net";

            foreach (var img in doc.DocumentNode.SelectNodes("//img[@src]") ?? Enumerable.Empty<HtmlNode>())
            {
                string src = img.GetAttributeValue("src", "");
                if (src.StartsWith("/")) img.SetAttributeValue("src", baseUrl + src);
            }

            foreach (var link in doc.DocumentNode.SelectNodes("//a[@href]") ?? Enumerable.Empty<HtmlNode>())
            {
                string href = link.GetAttributeValue("href", "");
                if (href.StartsWith("/wiki/"))
                {
                    link.SetAttributeValue("href", baseUrl + href);
                    link.SetAttributeValue("target", "_blank");
                }
            }

            var isDarkTheme = Application.Current.RequestedTheme == ApplicationTheme.Dark;
            var textColor = isDarkTheme ? "#E8E8E8" : "#202020";
            var linkColor = isDarkTheme ? "#78B2F3" : "#0066CC";

            var style = $@"
                <style>
                    html, body {{ 
                        background-color: transparent !important; 
                        color: {textColor}; 
                        font-family: 'Segoe UI', sans-serif;
                        margin: 0; padding: 0;
                    }}
                    a {{ color: {linkColor}; text-decoration: none; }}
                    a:hover {{ text-decoration: underline; }}
                    .mw-editsection, .reflist, .gallery, .thumb, .infobox, table.infobox, table.infobox > tbody > tr > th, table.infobox > tbody > tr > td {{ 
                        background-color: transparent !important; 
                    }}
                    table.infobox {{ border-color: gray; }}
                </style>";

            return $@"
                <!DOCTYPE html>
                <html>
                <head>
                    <meta charset='UTF-8'>
                    <meta name='viewport' content='width=device-width, initial-scale=1.0'>
                    {style}
                </head>
                <body>
                    {doc.DocumentNode.OuterHtml}
                </body>
                </html>";
        }
    }
}