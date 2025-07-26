using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.Storage;
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
    public class ImageQueryResponse { public ImageQueryPages query { get; set; } }
    public class ImageQueryPages { public Dictionary<string, ImagePage> pages { get; set; } }
    public class ImagePage { public string title { get; set; } public ImageInfo[] imageinfo { get; set; } }
    public class ImageInfo { public string url { get; set; } }

    public sealed partial class ArticleViewerPage : Page
    {
        private enum FetchStep { Idle, GetRandomTitle, ParseArticleContent }
        private FetchStep _currentFetchStep = FetchStep.Idle;
        private string _pageTitleToFetch = "";
        private const string ApiBaseUrl = "https://betawiki.net/api.php";
        private const string VirtualHostName = "local.betawiki-app.net";
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

                var tempFolder = ApplicationData.Current.LocalFolder.Path;
                ArticleDisplayWebView.CoreWebView2.SetVirtualHostNameToFolderMapping(VirtualHostName, tempFolder, CoreWebView2HostResourceAccessKind.Allow);

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
            }
        }

        private void StartArticleFetch()
        {
            if (!_isInitialized) return;
            LoadingOverlay.Visibility = Visibility.Visible;
            LoadingText.Text = $"Fetching: '{_pageTitleToFetch}'...";
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
            SilentFetchView.CoreWebView2.Navigate(apiUrl);
        }

        private async void CoreWebView2_NavigationCompleted(CoreWebView2 sender, CoreWebView2NavigationCompletedEventArgs args)
        {
            if (_currentFetchStep == FetchStep.Idle) return;

            await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, async () =>
            {
                if (!args.IsSuccess) return;

                try
                {
                    string script = "document.body.innerText;";
                    string scriptResult = await sender.ExecuteScriptAsync(script);

                    string resultJson;
                    try
                    {
                        resultJson = JsonSerializer.Deserialize<string>(scriptResult);
                    }
                    catch (JsonException)
                    {
                        Debug.WriteLine("[LOG] Content is not JSON, assuming Cloudflare interstitial. Waiting for redirect...");
                        return;
                    }

                    if (string.IsNullOrEmpty(resultJson))
                    {
                        throw new Exception("WebView returned empty content after JSON deserialization.");
                    }

                    if (_currentFetchStep == FetchStep.GetRandomTitle)
                    {
                        var randomResponse = JsonSerializer.Deserialize<RandomQueryResponse>(resultJson);
                        string randomTitle = randomResponse?.query?.random?.FirstOrDefault()?.title;
                        if (string.IsNullOrEmpty(randomTitle)) throw new Exception("Failed to get a random title from the API.");

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
                        if (string.IsNullOrEmpty(htmlContent) || string.IsNullOrEmpty(articleTitle)) throw new Exception("API response did not contain valid title or content.");

                        ArticleTitle.Text = articleTitle;
                        string processedHtml = await ProcessHtmlAsync(htmlContent);

                        StorageFolder localFolder = ApplicationData.Current.LocalFolder;
                        StorageFile articleFile = await localFolder.CreateFileAsync("article.html", CreationCollisionOption.ReplaceExisting);
                        await FileIO.WriteTextAsync(articleFile, processedHtml);

                        ArticleDisplayWebView.CoreWebView2.Navigate($"https://{VirtualHostName}/article.html");

                        LoadingOverlay.Visibility = Visibility.Collapsed;
                        _currentFetchStep = FetchStep.Idle;
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[LOG] Process FAILED at step {_currentFetchStep}: {ex.Message}. Assuming manual CAPTCHA.");
                    LoadingOverlay.Visibility = Visibility.Collapsed;
                    SilentFetchView.Visibility = Visibility.Visible;
                    SilentFetchView.CoreWebView2.NavigationCompleted -= CoreWebView2_NavigationCompleted;
                    SilentFetchView.CoreWebView2.NavigationCompleted += CoreWebView2_CaptchaSolved_NavigationCompleted;
                    SilentFetchView.CoreWebView2.Navigate(ApiBaseUrl);
                }
            });
        }

        private void CoreWebView2_CaptchaSolved_NavigationCompleted(CoreWebView2 sender, CoreWebView2NavigationCompletedEventArgs args)
        {
            SilentFetchView.CoreWebView2.NavigationCompleted -= CoreWebView2_CaptchaSolved_NavigationCompleted;
            SilentFetchView.Visibility = Visibility.Collapsed;
            SilentFetchView.CoreWebView2.NavigationCompleted += CoreWebView2_NavigationCompleted;
            StartArticleFetch();
        }

        private async Task<string> NavigateAndFetchImageAsBase64Async(string imageUrl)
        {
            if (string.IsNullOrEmpty(imageUrl)) return null;

            var tcs = new TaskCompletionSource<string>();

            TypedEventHandler<CoreWebView2, CoreWebView2NavigationCompletedEventArgs> navigationHandler = null;
            navigationHandler = async (sender, args) =>
            {
                sender.NavigationCompleted -= navigationHandler;

                if (!args.IsSuccess)
                {
                    tcs.TrySetResult(null);
                    return;
                }

                const string script = @"
                    (function() {
                        const img = document.querySelector('img');
                        if (!img || !img.naturalWidth) return null;
                        const canvas = document.createElement('canvas');
                        canvas.width = img.naturalWidth;
                        canvas.height = img.naturalHeight;
                        const ctx = canvas.getContext('2d');
                        ctx.drawImage(img, 0, 0);
                        return canvas.toDataURL('image/png');
                    })();";

                try
                {
                    string scriptResult = await sender.ExecuteScriptAsync(script);
                    string finalResult = string.IsNullOrEmpty(scriptResult) || scriptResult == "null" ? null : JsonSerializer.Deserialize<string>(scriptResult);
                    tcs.TrySetResult(finalResult);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[LOG] Error extracting Base64 from navigated image {imageUrl}: {ex.Message}");
                    tcs.TrySetResult(null);
                }
            };

            try
            {
                SilentFetchView.CoreWebView2.NavigationCompleted += navigationHandler;
                SilentFetchView.CoreWebView2.Navigate(imageUrl);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[LOG] Immediate navigation exception for {imageUrl}: {ex.Message}");
                SilentFetchView.CoreWebView2.NavigationCompleted -= navigationHandler;
                tcs.TrySetResult(null);
            }

            return await tcs.Task;
        }

        private async Task<string> ProcessHtmlAsync(string rawHtml)
        {
            var doc = new HtmlDocument();
            doc.LoadHtml(rawHtml);
            string baseUrl = "https://betawiki.net";
            var baseUri = new Uri(baseUrl);

            var imageLinks = doc.DocumentNode.SelectNodes("//a[starts-with(@href, '/wiki/File:')]");
            if (imageLinks != null && imageLinks.Any())
            {
                var imageFileNames = imageLinks.Select(link => link.GetAttributeValue("href", "").Substring(6)).Distinct().ToList();
                var titles = string.Join("|", imageFileNames.Select(Uri.EscapeDataString));
                var imageUrlApi = $"{ApiBaseUrl}?action=query&prop=imageinfo&iiprop=url&format=json&titles={titles}";

                SilentFetchView.CoreWebView2.NavigationCompleted -= CoreWebView2_NavigationCompleted;
                try
                {
                    var tcs = new TaskCompletionSource<string>();
                    TypedEventHandler<CoreWebView2, CoreWebView2NavigationCompletedEventArgs> handler = null;
                    handler = async (sender, args) =>
                    {
                        sender.NavigationCompleted -= handler;
                        try
                        {
                            string script = "document.body.innerText;";
                            string scriptResult = await sender.ExecuteScriptAsync(script);
                            string resultJson;
                            try { resultJson = JsonSerializer.Deserialize<string>(scriptResult); }
                            catch (JsonException) { Debug.WriteLine("[LOG] Image API content is not JSON, assuming Cloudflare interstitial. Waiting..."); return; }
                            tcs.TrySetResult(resultJson);
                        }
                        catch (Exception ex) { tcs.TrySetException(ex); }
                    };
                    SilentFetchView.CoreWebView2.NavigationCompleted += handler;
                    SilentFetchView.CoreWebView2.Navigate(imageUrlApi);

                    var imageJsonResponse = await tcs.Task;
                    var imageInfoResponse = JsonSerializer.Deserialize<ImageQueryResponse>(imageJsonResponse);

                    if (imageInfoResponse?.query?.pages != null)
                    {
                        var imageUrlMap = imageInfoResponse.query.pages.Values
                            .Where(p => p.imageinfo?.FirstOrDefault()?.url != null)
                            .ToDictionary(p => p.title, p => p.imageinfo.First().url);

                        if (imageUrlMap.Any())
                        {
                            foreach (var link in imageLinks)
                            {
                                string lookupKey = Uri.UnescapeDataString(link.GetAttributeValue("href", "").Substring(6)).Replace('_', ' ');
                                if (imageUrlMap.ContainsKey(lookupKey))
                                {
                                    var img = link.SelectSingleNode(".//img");
                                    if (img != null) img.SetAttributeValue("src", imageUrlMap[lookupKey]);
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[LOG] Failed to get image URLs: {ex.Message}");
                }
                finally
                {
                    SilentFetchView.CoreWebView2.NavigationCompleted += CoreWebView2_NavigationCompleted;
                }
            }

            SilentFetchView.CoreWebView2.NavigationCompleted -= CoreWebView2_NavigationCompleted;
            try
            {
                var allImages = doc.DocumentNode.SelectNodes("//img");
                if (allImages != null)
                {
                    foreach (var img in allImages)
                    {
                        string originalSrc = img.GetAttributeValue("srcset", null)?.Split(',').FirstOrDefault()?.Trim().Split(' ')[0]
                                            ?? img.GetAttributeValue("src", null);

                        if (string.IsNullOrEmpty(originalSrc) || originalSrc.StartsWith("data:image")) continue;
                        if (!Uri.TryCreate(baseUri, originalSrc, out Uri resultUri)) continue;

                        string base64Data = await NavigateAndFetchImageAsBase64Async(resultUri.AbsoluteUri);
                        if (!string.IsNullOrEmpty(base64Data))
                        {
                            img.SetAttributeValue("src", base64Data);
                            img.Attributes.Remove("srcset");
                        }
                    }
                }
            }
            finally
            {
                SilentFetchView.CoreWebView2.NavigationCompleted += CoreWebView2_NavigationCompleted;
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
            var tableBackgroundColor = isDarkTheme ? "rgba(40, 40, 40, 0.7)" : "rgba(255, 255, 255, 0.5)";

            var style = $@"<style>
                    html, body {{ background-color: transparent !important; color: {textColor}; font-family: 'Segoe UI', sans-serif; margin: 0; padding: 0; }}
                    a {{ color: {linkColor}; text-decoration: none; }}
                    a:hover {{ text-decoration: underline; }}
                    .mw-editsection, .reflist {{ display: none; }}
                    img {{ max-width: 100%; height: auto; }}
                    .infobox, table.wikitable {{ background-color: {tableBackgroundColor} !important; border-radius: 8px; border-collapse: separate; border-spacing: 0; border-color: transparent !important; }}
                    .infobox > tbody > tr > th, .infobox > tbody > tr > td, .wikitable > tbody > tr > th, .wikitable > tbody > tr > td {{ padding: 8px; }}
                    .gallery, .thumb, .infobox > tbody > tr > th, .infobox > tbody > tr > td {{ background-color: transparent !important; }}
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