using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.Storage;
using HtmlAgilityPack;
using Microsoft.UI.Xaml.Controls;
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
        private readonly int _maxWorkerCount;

        public ArticleViewerPage()
        {
            this.InitializeComponent();
            _maxWorkerCount = Environment.ProcessorCount;
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
                this.Unloaded += ArticleViewerPage_Unloaded;

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

        private void ArticleViewerPage_Unloaded(object sender, RoutedEventArgs e)
        {
            ArticleDisplayWebView?.Close();
            SilentFetchView?.Close();

            ArticleDisplayWebView = null;
            SilentFetchView = null;
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
                    try { resultJson = JsonSerializer.Deserialize<string>(scriptResult); }
                    catch (JsonException) { Debug.WriteLine("[LOG] Content is not JSON, assuming Cloudflare interstitial. Waiting for redirect..."); return; }

                    if (string.IsNullOrEmpty(resultJson)) throw new Exception("WebView returned empty content after JSON deserialization.");

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

        private async Task<string> DownloadAndCacheImageAsync(Uri imageUrl, StorageFolder cacheFolder, WebView2 worker)
        {
            if (imageUrl == null || worker == null) return null;

            var fileName = System.Security.Cryptography.SHA1.Create()
                .ComputeHash(Encoding.UTF8.GetBytes(imageUrl.AbsoluteUri))
                .Aggregate("", (s, b) => s + b.ToString("x2")) + Path.GetExtension(imageUrl.LocalPath);

            var cachedFile = await cacheFolder.TryGetItemAsync(fileName) as StorageFile;
            if (cachedFile != null) return $"/cache/{fileName}";

            var tcs = new TaskCompletionSource<string>();
            TypedEventHandler<CoreWebView2, CoreWebView2NavigationCompletedEventArgs> navigationHandler = null;
            navigationHandler = async (sender, args) =>
            {
                sender.NavigationCompleted -= navigationHandler;
                if (!args.IsSuccess) { tcs.TrySetResult(null); return; }

                const string script = @"(function() {
                    const img = document.querySelector('img');
                    if (!img || !img.naturalWidth) return null;
                    const canvas = document.createElement('canvas');
                    canvas.width = img.naturalWidth; canvas.height = img.naturalHeight;
                    const ctx = canvas.getContext('2d');
                    ctx.drawImage(img, 0, 0);
                    return canvas.toDataURL('image/png').split(',')[1];
                })();";

                try
                {
                    string scriptResult = await sender.ExecuteScriptAsync(script);
                    tcs.TrySetResult(string.IsNullOrEmpty(scriptResult) || scriptResult == "null" ? null : JsonSerializer.Deserialize<string>(scriptResult));
                }
                catch (Exception) { tcs.TrySetResult(null); }
            };

            worker.CoreWebView2.NavigationCompleted += navigationHandler;
            worker.CoreWebView2.Navigate(imageUrl.AbsoluteUri);

            string base64Data = await tcs.Task;
            if (string.IsNullOrEmpty(base64Data)) return null;

            var newFile = await cacheFolder.CreateFileAsync(fileName, CreationCollisionOption.ReplaceExisting);
            var bytes = Convert.FromBase64String(base64Data);
            await FileIO.WriteBytesAsync(newFile, bytes);

            return $"/cache/{fileName}";
        }

        private async Task<string> ProcessHtmlAsync(string rawHtml)
        {
            var doc = new HtmlDocument();
            doc.LoadHtml(rawHtml);
            string baseUrl = "https://betawiki.net";
            var baseUri = new Uri(baseUrl);

            StorageFolder localCacheFolder = ApplicationData.Current.LocalFolder;
            StorageFolder imageCacheFolder = await localCacheFolder.CreateFolderAsync("cache", CreationCollisionOption.OpenIfExists);

            var nodesToRemove = doc.DocumentNode.SelectNodes("//link[contains(@href, 'mw-data:TemplateStyles')] | //style[contains(@data-mw-deduplicate, 'TemplateStyles')]");
            if (nodesToRemove != null) { foreach (var node in nodesToRemove) { node.Remove(); } }
            var legendCells = doc.DocumentNode.SelectNodes("//td[contains(@class, 'table-version')]");
            if (legendCells != null) { foreach (var cell in legendCells) { cell.Attributes.Remove("style"); } }

            SilentFetchView.CoreWebView2.NavigationCompleted -= CoreWebView2_NavigationCompleted;

            List<WebView2> webViewWorkers = new List<WebView2>();
            SemaphoreSlim workerSemaphore = null;

            try
            {
                var imageLinks = doc.DocumentNode.SelectNodes("//a[starts-with(@href, '/wiki/File:')]");
                if (imageLinks != null && imageLinks.Any())
                {
                    var imageFileNames = imageLinks.Select(link => link.GetAttributeValue("href", "").Substring(6)).Distinct().ToList();
                    var titles = string.Join("|", imageFileNames.Select(Uri.EscapeDataString));
                    var imageUrlApi = $"{ApiBaseUrl}?action=query&prop=imageinfo&iiprop=url&format=json&titles={titles}";
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
                            catch (JsonException) { return; }
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
                        var imageUrlMap = imageInfoResponse.query.pages.Values.Where(p => p.imageinfo?.FirstOrDefault()?.url != null).ToDictionary(p => p.title, p => p.imageinfo.First().url);
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

                var allImages = doc.DocumentNode.SelectNodes("//img");
                if (allImages != null && allImages.Count > 0)
                {
                    int workersToCreate = Math.Min(allImages.Count, _maxWorkerCount);
                    workerSemaphore = new SemaphoreSlim(workersToCreate, workersToCreate);
                    var availableWorkers = new ConcurrentQueue<WebView2>();

                    var coreInitTasks = new List<Task>();
                    for (int i = 0; i < workersToCreate; i++)
                    {
                        var worker = new WebView2();
                        webViewWorkers.Add(worker);
                        WorkerWebViewHost.Children.Add(worker);
                        coreInitTasks.Add(worker.EnsureCoreWebView2Async().AsTask());
                    }
                    await Task.WhenAll(coreInitTasks);
                    foreach (var worker in webViewWorkers) availableWorkers.Enqueue(worker);

                    var imageDownloadTasks = allImages.Select(async img =>
                    {
                        string originalSrc = img.GetAttributeValue("srcset", null)?.Split(',').FirstOrDefault()?.Trim().Split(' ')[0] ?? img.GetAttributeValue("src", null);
                        if (string.IsNullOrEmpty(originalSrc) || !Uri.TryCreate(baseUri, originalSrc, out Uri resultUri))
                        {
                            return;
                        }

                        await workerSemaphore.WaitAsync();
                        availableWorkers.TryDequeue(out WebView2 worker);
                        try
                        {
                            string localImagePath = await DownloadAndCacheImageAsync(resultUri, imageCacheFolder, worker);
                            if (!string.IsNullOrEmpty(localImagePath))
                            {
                                await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                                {
                                    img.SetAttributeValue("src", $"https://{VirtualHostName}{localImagePath}");
                                    img.Attributes.Remove("srcset");
                                });
                            }
                        }
                        finally
                        {
                            availableWorkers.Enqueue(worker);
                            workerSemaphore.Release();
                        }
                    }).ToList();

                    await Task.WhenAll(imageDownloadTasks);
                }
            }
            finally
            {
                SilentFetchView.CoreWebView2.NavigationCompleted += CoreWebView2_NavigationCompleted;

                foreach (var worker in webViewWorkers) worker?.Close();
                WorkerWebViewHost.Children.Clear();
                workerSemaphore?.Dispose();
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
            string cssVariables;
            if (isDarkTheme)
            {
                cssVariables = @":root {
            --text-primary: #FFFFFF; --text-secondary: #C3C3C3; --link-color: #85B9F3; --card-shadow: rgba(0, 0, 0, 0.4);
            --card-background: rgba(44, 44, 44, 0.7); --card-border: rgba(255, 255, 255, 0.1); --card-header-background: rgba(255, 255, 255, 0.08);
            --item-hover-background: rgba(255, 255, 255, 0.07); --table-row-divider: rgba(255, 255, 255, 0.08);
            --legend-unsupported-tint: linear-gradient(rgba(239, 68, 68, 0.15), rgba(239, 68, 68, 0.15));
            --legend-supported-tint: linear-gradient(rgba(234, 179, 8, 0.15), rgba(234, 179, 8, 0.15));
            --legend-latest-tint: linear-gradient(rgba(34, 197, 94, 0.15), rgba(34, 197, 94, 0.15));
            --legend-preview-tint: linear-gradient(rgba(249, 115, 22, 0.15), rgba(249, 115, 22, 0.15));
            --legend-future-tint: linear-gradient(rgba(59, 130, 246, 0.15), rgba(59, 130, 246, 0.15));
            --legend-na-tint: linear-gradient(rgba(255, 255, 255, 0.04), rgba(255, 255, 255, 0.04));
        }";
            }
            else
            {
                cssVariables = @":root {
            --text-primary: #000000; --text-secondary: #505050; --link-color: #0066CC; --card-shadow: rgba(0, 0, 0, 0.13);
            --card-background: rgba(249, 249, 249, 0.7); --card-border: rgba(0, 0, 0, 0.1); --card-header-background: rgba(0, 0, 0, 0.05);
            --item-hover-background: rgba(0, 0, 0, 0.05); --table-row-divider: rgba(0, 0, 0, 0.08);
            --legend-unsupported-tint: linear-gradient(rgba(239, 68, 68, 0.1), rgba(239, 68, 68, 0.1));
            --legend-supported-tint: linear-gradient(rgba(234, 179, 8, 0.1), rgba(234, 179, 8, 0.1));
            --legend-latest-tint: linear-gradient(rgba(34, 197, 94, 0.1), rgba(34, 197, 94, 0.1));
            --legend-preview-tint: linear-gradient(rgba(249, 115, 22, 0.1), rgba(249, 115, 22, 0.1));
            --legend-future-tint: linear-gradient(rgba(59, 130, 246, 0.1), rgba(59, 130, 246, 0.1));
            --legend-na-tint: linear-gradient(rgba(0, 0, 0, 0.04), rgba(0, 0, 0, 0.04));
        }";
            }

            var style = $@"<style>
        {cssVariables}
        html, body {{ background-color: transparent !important; color: var(--text-primary); font-family: 'Segoe UI Variable', 'Segoe UI', sans-serif; margin: 0; padding: 12px; font-size: 15px; -webkit-font-smoothing: antialiased; }}
        a {{ color: var(--link-color); text-decoration: none; }} a:hover {{ text-decoration: underline; }}
        a.selflink, a.new {{ color: var(--text-secondary); pointer-events: none; text-decoration: none; }}
        img {{ max-width: 100%; height: auto; border-radius: 4px; }} .mw-editsection, .reflist {{ display: none; }}
        .infobox {{ float: right; margin: 0 0 1em 1.5em; width: 22em; }}
        .hlist ul {{ padding: 0; margin: 0; list-style: none; }} .hlist li {{ display: inline; white-space: nowrap; }}
        .hlist li:not(:first-child)::before {{ content: ' \00B7 '; font-weight: bold; }} .hlist dl, .hlist ol, .hlist ul {{ display: inline; }}
        .infobox, table.wikitable, .navbox {{ background-color: var(--card-background) !important; border: 1px solid var(--card-border); border-radius: 8px; box-shadow: 0 4px 12px var(--card-shadow); border-collapse: separate; border-spacing: 0; margin-bottom: 16px; overflow: hidden; }}
        .infobox > tbody > tr > *, .wikitable > tbody > tr > * {{ vertical-align: middle; }}
        .infobox > tbody > tr > th, .infobox > tbody > tr > td, .wikitable > tbody > tr > th, .wikitable > tbody > tr > td {{ padding: 12px 16px; text-align: left; border: none; }}
        .infobox > tbody > tr:not(:last-child) > *, .wikitable > tbody > tr:not(:last-child) > * {{ border-bottom: 1px solid var(--table-row-divider); }}
        .infobox > tbody > tr > th, .wikitable > tbody > tr > th {{ font-weight: 600; color: var(--text-secondary); }}
        .wikitable .table-version-unsupported {{ background-image: var(--legend-unsupported-tint); }}
        .wikitable .table-version-supported {{ background-image: var(--legend-supported-tint); }}
        .wikitable .table-version-latest {{ background-image: var(--legend-latest-tint); }}
        .wikitable .table-version-preview {{ background-image: var(--legend-preview-tint); }}
        .wikitable .table-version-future {{ background-image: var(--legend-future-tint); }}
        .wikitable .table-na {{ background-image: var(--legend-na-tint); color: var(--text-secondary) !important; }}
        .version-legend-horizontal {{ padding: 8px 16px; font-size: 13px; color: var(--text-secondary); text-align: center; }}
        .version-legend-square {{ display: inline-block; width: 1em; height: 1em; margin-right: 0.5em; border: 1px solid var(--card-border); vertical-align: -0.1em; }}
        .version-legend-horizontal .version-unsupported.version-legend-square {{ background-image: var(--legend-unsupported-tint); }}
        .version-legend-horizontal .version-supported.version-legend-square {{ background-image: var(--legend-supported-tint); }}
        .version-legend-horizontal .version-latest.version-legend-square {{ background-image: var(--legend-latest-tint); }}
        .version-legend-horizontal .version-preview.version-legend-square {{ background-image: var(--legend-preview-tint); }}
        .version-legend-horizontal .version-future.version-legend-square {{ background-image: var(--legend-future-tint); }}
        .navbox-title, .navbox-group {{ background: var(--card-header-background); padding: 12px 16px; font-weight: 600; }}
        .navbox-title {{ border-bottom: 1px solid var(--card-border); font-size: 16px; }}
        .navbox-group {{ border-top: 1px solid var(--card-border); font-size: 12px; text-transform: uppercase; }}
        .navbox-title a, .navbox-title a:link, .navbox-title a:visited {{ color: var(--text-primary); text-decoration: none; }}
        .navbox-group a, .navbox-group a:link, .navbox-group a:visited {{ color: var(--text-secondary); text-decoration: none; }}
        .navbox-inner {{ padding: 8px; }} .navbox-list li a {{ padding: 4px 6px; border-radius: 4px; transition: background-color 0.15s ease-in-out; }}
        .navbox-list li a:hover {{ background: var(--item-hover-background); text-decoration: none; }}
        .navbox-image {{ float: right; margin: 16px; }}
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