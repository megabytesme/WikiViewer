using System;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using HtmlAgilityPack;
using Microsoft.Web.WebView2.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Documents;
using Windows.UI.Xaml.Navigation;
using System.Text.Json.Serialization;

namespace _1809_UWP
{
    public class ApiParseResponse { public ParseResult parse { get; set; } }

    public class ParseResult
    {
        public string title { get; set; }
        public TextContent text { get; set; }
    }

    public class TextContent
    {
        [JsonPropertyName("*")]
        public string Content { get; set; }
    }

    public class RandomQueryResponse { public QueryResult query { get; set; } }
    public class QueryResult { public RandomPage[] random { get; set; } }
    public class RandomPage { public string title { get; set; } }

    public sealed partial class ArticleViewerPage : Page
    {
        private enum FetchStep { Idle, GetRandomTitle, ParseArticleContent }
        private FetchStep _currentFetchStep = FetchStep.Idle;

        private string _pageTitleToFetch = "";
        private const string ApiBaseUrl = "https://betawiki.net/api.php";

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
                StartArticleFetch();
            }
        }

        private async void StartArticleFetch()
        {
            LoadingOverlay.Visibility = Visibility.Visible;
            LoadingText.Text = $"Fetching: '{_pageTitleToFetch}'...";
            Debug.WriteLine($"[LOG] Starting fetch for: '{_pageTitleToFetch}'");

            try
            {
                await FetchAndVerifyWebView.EnsureCoreWebView2Async();
                FetchAndVerifyWebView.CoreWebView2.NavigationCompleted -= CoreWebView2_NavigationCompleted;
                FetchAndVerifyWebView.CoreWebView2.NavigationCompleted += CoreWebView2_NavigationCompleted;

                string apiUrl;
                if (_pageTitleToFetch.Equals("random", StringComparison.OrdinalIgnoreCase))
                {
                    _currentFetchStep = FetchStep.GetRandomTitle;
                    Debug.WriteLine("[LOG] Step 1: Getting a random title.");
                    apiUrl = $"{ApiBaseUrl}?action=query&list=random&rnnamespace=0&rnlimit=1&format=json";
                }
                else
                {
                    _currentFetchStep = FetchStep.ParseArticleContent;
                    Debug.WriteLine($"[LOG] Step 1 (and only): Parsing page '{_pageTitleToFetch}'.");
                    apiUrl = $"{ApiBaseUrl}?action=parse&page={Uri.EscapeDataString(_pageTitleToFetch)}&format=json";
                }

                Debug.WriteLine($"[LOG] Navigating WebView to: {apiUrl}");
                FetchAndVerifyWebView.CoreWebView2.Navigate(apiUrl);
            }
            catch (Exception ex)
            {
            }
        }

        private async void CoreWebView2_NavigationCompleted(CoreWebView2 sender, CoreWebView2NavigationCompletedEventArgs args)
        {
            Debug.WriteLine($"[LOG] NavigationCompleted fired for step: {_currentFetchStep}");
            if (_currentFetchStep == FetchStep.Idle) return;

            await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, async () =>
            {
                if (!args.IsSuccess)
                {
                    LoadingOverlay.Visibility = Visibility.Collapsed;
                    Debug.WriteLine($"[LOG] Navigation FAILED. Status: {args.WebErrorStatus}");
                    return;
                }

                try
                {
                    string script = "document.body.innerText;";
                    string scriptResult = await FetchAndVerifyWebView.CoreWebView2.ExecuteScriptAsync(script);

                    string resultJson = JsonSerializer.Deserialize<string>(scriptResult);

                    if (string.IsNullOrEmpty(resultJson))
                    {
                        throw new Exception("WebView returned empty content.");
                    }

                    if (_currentFetchStep == FetchStep.GetRandomTitle)
                    {
                        var randomResponse = JsonSerializer.Deserialize<RandomQueryResponse>(resultJson);
                        string randomTitle = null;
                        if (randomResponse != null && randomResponse.query != null && randomResponse.query.random != null && randomResponse.query.random.Length > 0)
                        {
                            randomTitle = randomResponse.query.random.FirstOrDefault()?.title;
                        }

                        if (string.IsNullOrEmpty(randomTitle))
                        {
                            throw new Exception("Failed to get a random title from the API.");
                        }

                        Debug.WriteLine($"[LOG] Got random title: '{randomTitle}'.");
                        _pageTitleToFetch = randomTitle;

                        _currentFetchStep = FetchStep.ParseArticleContent;
                        LoadingText.Text = $"Parsing: '{_pageTitleToFetch}'...";
                        Debug.WriteLine($"[LOG] Step 2: Parsing page '{_pageTitleToFetch}'.");
                        string parseUrl = $"{ApiBaseUrl}?action=parse&page={Uri.EscapeDataString(_pageTitleToFetch)}&format=json";
                        FetchAndVerifyWebView.CoreWebView2.Navigate(parseUrl);
                    }
                    else if (_currentFetchStep == FetchStep.ParseArticleContent)
                    {
                        var apiResponse = JsonSerializer.Deserialize<ApiParseResponse>(resultJson);
                        string htmlContent = null;
                        string articleTitle = null;
                        if (apiResponse != null && apiResponse.parse != null)
                        {
                            articleTitle = apiResponse.parse.title;
                            if (apiResponse.parse.text != null)
                            {
                                htmlContent = apiResponse.parse.text.Content;
                            }
                        }

                        if (string.IsNullOrEmpty(htmlContent) || string.IsNullOrEmpty(articleTitle))
                        {
                            throw new Exception("API response did not contain valid title or content.");
                        }

                        Debug.WriteLine($"[LOG] SUCCESS! Parsed article '{articleTitle}'.");
                        ArticleTitle.Text = articleTitle;
                        HtmlToRichTextBlock(htmlContent);

                        FetchAndVerifyWebView.Visibility = Visibility.Collapsed;
                        ContentScrollViewer.Visibility = Visibility.Visible;
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

        private void HtmlToRichTextBlock(string html)
        {
            ArticleContent.Blocks.Clear();
            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            var paragraph = new Paragraph();

            foreach (var node in doc.DocumentNode.Descendants())
            {
                if (node.NodeType == HtmlNodeType.Text && node.ParentNode.Name != "a")
                {
                    paragraph.Inlines.Add(new Run { Text = HtmlEntity.DeEntitize(node.InnerText) });
                }
                else if (node.Name == "p")
                {
                    if (paragraph.Inlines.Count > 0) ArticleContent.Blocks.Add(paragraph);
                    paragraph = new Paragraph();
                }
                else if (node.Name == "b" || node.Name == "strong")
                {
                    paragraph.Inlines.Add(new Bold { Inlines = { new Run { Text = HtmlEntity.DeEntitize(node.InnerText) } } });
                }
            }

            if (paragraph.Inlines.Count > 0)
            {
                ArticleContent.Blocks.Add(paragraph);
            }
        }
    }
}