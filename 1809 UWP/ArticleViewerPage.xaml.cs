using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.Storage;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;
using HtmlAgilityPack;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
using muxc = Microsoft.UI.Xaml.Controls;

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
    public class TimestampQueryResponse { public TimestampQueryPages query { get; set; } }
    public class TimestampQueryPages { public Dictionary<string, TimestampPage> pages { get; set; } }
    public class TimestampPage { [JsonPropertyName("revisions")] public List<RevisionInfo> Revisions { get; set; } }
    public class RevisionInfo { [JsonPropertyName("timestamp")] public DateTime Timestamp { get; set; } }

    public class ArticleCacheItem
    {
        public string Title { get; set; }
        public DateTime LastUpdated { get; set; }
    }

    public static class ArticleCacheManager
    {
        private static StorageFolder _cacheFolder;

        public static async Task InitializeAsync()
        {
            if (_cacheFolder != null) return;
            _cacheFolder = await ApplicationData.Current.LocalFolder.CreateFolderAsync("ArticleCache", CreationCollisionOption.OpenIfExists);
        }

        private static string GetHashedFileName(string pageTitle)
        {
            var hash = System.Security.Cryptography.SHA1.Create()
                .ComputeHash(Encoding.UTF8.GetBytes(pageTitle.ToLowerInvariant()));
            return hash.Aggregate("", (s, b) => s + b.ToString("x2"));
        }

        public static async Task<ArticleCacheItem> GetCacheMetadataAsync(string pageTitle)
        {
            await InitializeAsync();
            string fileName = GetHashedFileName(pageTitle) + ".json";
            var item = await _cacheFolder.TryGetItemAsync(fileName);
            if (item is StorageFile file)
            {
                try
                {
                    string json = await FileIO.ReadTextAsync(file);
                    return JsonSerializer.Deserialize<ArticleCacheItem>(json);
                }
                catch { return null; }
            }
            return null;
        }

        public static async Task<string> GetCachedArticleHtmlAsync(string pageTitle)
        {
            await InitializeAsync();
            string fileName = GetHashedFileName(pageTitle) + ".html";
            var item = await _cacheFolder.TryGetItemAsync(fileName);
            if (item is StorageFile file)
            {
                return await FileIO.ReadTextAsync(file);
            }
            return null;
        }

        public static async Task SaveArticleToCacheAsync(string pageTitle, string htmlContent, DateTime lastUpdated)
        {
            await InitializeAsync();
            string baseFileName = GetHashedFileName(pageTitle);

            var metadata = new ArticleCacheItem { Title = pageTitle, LastUpdated = lastUpdated };
            string json = JsonSerializer.Serialize(metadata);
            StorageFile metadataFile = await _cacheFolder.CreateFileAsync(baseFileName + ".json", CreationCollisionOption.ReplaceExisting);
            await FileIO.WriteTextAsync(metadataFile, json);

            StorageFile htmlFile = await _cacheFolder.CreateFileAsync(baseFileName + ".html", CreationCollisionOption.ReplaceExisting);
            await FileIO.WriteTextAsync(htmlFile, htmlContent);

            Debug.WriteLine($"[CACHE] Saved '{pageTitle}' to cache.");
        }
    }

    public sealed partial class ArticleViewerPage : Page
    {
        private enum FetchStep { Idle, GetRandomTitle, ParseArticleContent }
        private FetchStep _currentFetchStep = FetchStep.Idle;
        private string _pageTitleToFetch = "";
        private const string ApiBaseUrl = "https://betawiki.net/api.php";
        private const string VirtualHostName = "local.betawiki-app.net";
        private bool _isInitialized = false;
        private readonly int _maxWorkerCount;
        private Stopwatch _fetchStopwatch;
        private readonly Stack<string> _articleHistory = new Stack<string>();
        private double _titleBarHeight = 0;
        public bool CanGoBackInPage => _articleHistory.Count > 1;

        public ArticleViewerPage()
        {
            this.InitializeComponent();
            _maxWorkerCount = Environment.ProcessorCount;
            this.ActualThemeChanged += (s, e) => ApplyAcrylicToTitleBar();
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            if (e.Parameter is string pageTitle && !string.IsNullOrEmpty(pageTitle))
            {
                _pageTitleToFetch = pageTitle;
                _articleHistory.Clear();
                _articleHistory.Push(_pageTitleToFetch);
            }
        }

        private async void Page_Loaded(object sender, RoutedEventArgs e)
        {
            ApplyAcrylicToTitleBar();
            if (_isInitialized) return;
            try
            {
                Environment.SetEnvironmentVariable("WEBVIEW2_DEFAULT_BACKGROUND_COLOR", "00FFFFFF");
                await SilentFetchView.EnsureCoreWebView2Async();
                await ArticleDisplayWebView.EnsureCoreWebView2Async();
                var tempFolder = ApplicationData.Current.LocalFolder.Path;
                ArticleDisplayWebView.CoreWebView2.SetVirtualHostNameToFolderMapping(VirtualHostName, tempFolder, CoreWebView2HostResourceAccessKind.Allow);
                ArticleDisplayWebView.CoreWebView2.NavigationCompleted += ArticleDisplayWebView_ContentNavigationCompleted;
                ArticleDisplayWebView.CoreWebView2.NavigationStarting += ArticleDisplayWebView_NavigationStarting;
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

        private void ApplyAcrylicToTitleBar()
        {
            var acrylicBrush = new AcrylicBrush
            {
                BackgroundSource = AcrylicBackgroundSource.Backdrop,
                TintOpacity = 0.4
            };
            if (this.ActualTheme == ElementTheme.Dark)
            {
                acrylicBrush.TintColor = Colors.Black;
            }
            else
            {
                acrylicBrush.TintColor = Colors.White;
            }
            TitleBarBackground.Background = acrylicBrush;
        }

        private void ArticleViewerPage_Unloaded(object sender, RoutedEventArgs e)
        {
            if (ArticleDisplayWebView?.CoreWebView2 != null)
            {
                ArticleDisplayWebView.CoreWebView2.NavigationCompleted -= ArticleDisplayWebView_ContentNavigationCompleted;
                ArticleDisplayWebView.CoreWebView2.NavigationStarting -= ArticleDisplayWebView_NavigationStarting;
            }
        }

        private async Task<string> DownloadAndCacheImageAsync(Uri imageUrl, StorageFolder cacheFolder, WebView2 worker)
        {
            if (imageUrl == null || worker == null || worker.CoreWebView2 == null) return null;

            var fileName = System.Security.Cryptography.SHA1.Create()
                .ComputeHash(Encoding.UTF8.GetBytes(imageUrl.AbsoluteUri))
                .Aggregate("", (s, b) => s + b.ToString("x2")) + Path.GetExtension(imageUrl.LocalPath);

            var cachedFile = await cacheFolder.TryGetItemAsync(fileName) as StorageFile;
            if (cachedFile != null)
            {
                return $"/cache/{fileName}";
            }

            var tcs = new TaskCompletionSource<string>();
            TypedEventHandler<CoreWebView2, CoreWebView2NavigationCompletedEventArgs> navigationHandler = null;
            navigationHandler = async (sender, args) =>
            {
                sender.NavigationCompleted -= navigationHandler;
                if (!args.IsSuccess) { tcs.TrySetResult(null); return; }

                const string script = @"(function() {
            let base64Data = null;
            const img = document.querySelector('img');
            if (img && img.naturalWidth > 0) {
                const canvas = document.createElement('canvas');
                canvas.width = img.naturalWidth;
                canvas.height = img.naturalHeight;
                const ctx = canvas.getContext('2d');
                ctx.drawImage(img, 0, 0);
                base64Data = canvas.toDataURL('image/png').split(',')[1];
            } else if (document.documentElement && document.documentElement.tagName.toLowerCase() === 'svg') {
                const svgText = new XMLSerializer().serializeToString(document.documentElement);
                base64Data = window.btoa(unescape(encodeURIComponent(svgText)));
            }
            return base64Data;
        })();";

                try
                {
                    string scriptResult = await sender.ExecuteScriptAsync(script);
                    tcs.TrySetResult(string.IsNullOrEmpty(scriptResult) || scriptResult == "null" ? null : JsonSerializer.Deserialize<string>(scriptResult));
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[ImageCache] Script execution failed for {imageUrl.AbsoluteUri}: {ex.Message}");
                    tcs.TrySetResult(null);
                }
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

        private async void StartArticleFetch()
        {
            if (!_isInitialized) return;

            _pageTitleToFetch = _pageTitleToFetch.Replace(' ', '_');

            _fetchStopwatch = Stopwatch.StartNew();
            LoadingOverlay.Visibility = Visibility.Visible;
            LastUpdatedText.Visibility = Visibility.Collapsed;
            ArticleTitle.Text = _pageTitleToFetch.Replace('_', ' ');
            LoadingText.Text = $"Checking for '{ArticleTitle.Text}'...";

            bool isConnected = NetworkInterface.GetIsNetworkAvailable();

            if (AppSettings.IsCachingEnabled)
            {
                ArticleCacheItem cachedMetadata = await ArticleCacheManager.GetCacheMetadataAsync(_pageTitleToFetch);
                DateTime? remoteTimestamp = null;
                if (isConnected && !_pageTitleToFetch.Equals("random", StringComparison.OrdinalIgnoreCase))
                {
                    remoteTimestamp = await FetchLastUpdatedTimestampAsync(_pageTitleToFetch);
                }

                if (cachedMetadata != null && (!isConnected || remoteTimestamp == null || remoteTimestamp.Value.ToUniversalTime() <= cachedMetadata.LastUpdated.ToUniversalTime()))
                {
                    Debug.WriteLine($"[CACHE] Loading '{_pageTitleToFetch}' from cache. Freshness: {(!isConnected ? "OFFLINE" : "UP-TO-DATE")}");
                    LoadingText.Text = $"Loading '{ArticleTitle.Text}' from cache...";
                    string cachedHtml = await ArticleCacheManager.GetCachedArticleHtmlAsync(_pageTitleToFetch);
                    if (!string.IsNullOrEmpty(cachedHtml))
                    {
                        await DisplayProcessedHtml(cachedHtml);
                        LastUpdatedText.Text = $"Last updated: {cachedMetadata.LastUpdated.ToLocalTime():g} (Cached)";
                        LastUpdatedText.Visibility = Visibility.Visible;
                        LoadingOverlay.Visibility = Visibility.Collapsed;
                        _fetchStopwatch.Stop();
                        Debug.WriteLine($"[PERF] ===== Total operation time (from cache): {_fetchStopwatch.ElapsedMilliseconds} ms =====");
                        return;
                    }
                }
            }

            if (!isConnected)
            {
                ArticleTitle.Text = "No Connection";
                LoadingText.Text = $"Cannot fetch '{_pageTitleToFetch}'. Please check your internet connection.";
                return;
            }

            Debug.WriteLine($"[NETWORK] Fetching fresh version of '{_pageTitleToFetch}'.");
            LoadingText.Text = $"Fetching: '{ArticleTitle.Text}'...";

            string urlToFetch;
            if (_pageTitleToFetch.Equals("random", StringComparison.OrdinalIgnoreCase))
            {
                _currentFetchStep = FetchStep.GetRandomTitle;
                urlToFetch = $"{ApiBaseUrl}?action=query&list=random&rnnamespace=0&rnlimit=1&format=json";
            }
            else
            {
                _currentFetchStep = FetchStep.ParseArticleContent;
                urlToFetch = $"https://betawiki.net/wiki/{Uri.EscapeDataString(_pageTitleToFetch)}";
            }
            SilentFetchView.CoreWebView2.Navigate(urlToFetch);
        }

        private async Task<DateTime?> FetchLastUpdatedTimestampAsync(string pageTitle)
        {
            if (string.IsNullOrEmpty(pageTitle) || pageTitle.Equals("random", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            try
            {
                string url = $"{ApiBaseUrl}?action=query&prop=revisions&titles={Uri.EscapeDataString(pageTitle)}&rvprop=timestamp&rvlimit=1&format=json";
                var tcs = new TaskCompletionSource<string>();
                TypedEventHandler<CoreWebView2, CoreWebView2NavigationCompletedEventArgs> handler = null;
                handler = async (sender, args) =>
                {
                    sender.NavigationCompleted -= handler;
                    if (!args.IsSuccess)
                    {
                        tcs.TrySetResult(null);
                        return;
                    }
                    try
                    {
                        string scriptResult = await sender.ExecuteScriptAsync("document.body.innerText");
                        tcs.TrySetResult(JsonSerializer.Deserialize<string>(scriptResult));
                    }
                    catch { tcs.TrySetResult(null); }
                };
                SilentFetchView.CoreWebView2.NavigationCompleted += handler;
                SilentFetchView.CoreWebView2.Navigate(url);
                string json = await tcs.Task;
                if (string.IsNullOrEmpty(json)) return null;
                var response = JsonSerializer.Deserialize<TimestampQueryResponse>(json);
                var page = response?.query?.pages?.Values.FirstOrDefault();
                var revision = page?.Revisions?.FirstOrDefault();
                return revision?.Timestamp;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to fetch timestamp: {ex.Message}");
                return null;
            }
        }

        private async Task DisplayProcessedHtml(string html)
        {
            StorageFolder localFolder = ApplicationData.Current.LocalFolder;
            StorageFile articleFile = await localFolder.CreateFileAsync("article.html", CreationCollisionOption.ReplaceExisting);
            await FileIO.WriteTextAsync(articleFile, html);
            Debug.WriteLine($"[PERF] Wrote article.html to disk at: {_fetchStopwatch.ElapsedMilliseconds} ms");
            ArticleDisplayWebView.CoreWebView2.Navigate($"https://{VirtualHostName}/article.html");
        }

        private async void CoreWebView2_NavigationCompleted(CoreWebView2 sender, CoreWebView2NavigationCompletedEventArgs args)
        {
            if (_currentFetchStep == FetchStep.Idle) return;

            await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, async () =>
            {
                if (!args.IsSuccess)
                {
                    LoadingOverlay.Visibility = Visibility.Collapsed;
                    ArticleTitle.Text = "Failed to load page";
                    return;
                }

                try
                {
                    if (_currentFetchStep == FetchStep.GetRandomTitle)
                    {
                        string script = "document.body.innerText;";
                        string scriptResult = await sender.ExecuteScriptAsync(script);
                        string resultJson = JsonSerializer.Deserialize<string>(scriptResult);
                        var randomResponse = JsonSerializer.Deserialize<RandomQueryResponse>(resultJson);
                        string randomTitle = randomResponse?.query?.random?.FirstOrDefault()?.title;
                        if (string.IsNullOrEmpty(randomTitle)) throw new Exception("Failed to get a random title from the API.");

                        if (_articleHistory.Count > 0 && _articleHistory.Peek().Equals("random", StringComparison.OrdinalIgnoreCase))
                        {
                            _articleHistory.Pop();
                            _articleHistory.Push(randomTitle);
                        }

                        _pageTitleToFetch = randomTitle;
                        _currentFetchStep = FetchStep.ParseArticleContent;
                        ArticleTitle.Text = _pageTitleToFetch.Replace('_', ' ');
                        LoadingText.Text = $"Parsing: '{ArticleTitle.Text}'...";
                        string pageUrl = $"https://betawiki.net/wiki/{Uri.EscapeDataString(randomTitle)}";
                        SilentFetchView.CoreWebView2.Navigate(pageUrl);
                    }
                    else if (_currentFetchStep == FetchStep.ParseArticleContent)
                    {
                        string fullHtml = await sender.ExecuteScriptAsync("document.documentElement.outerHTML");
                        fullHtml = JsonSerializer.Deserialize<string>(fullHtml);
                        var doc = new HtmlDocument();
                        doc.LoadHtml(fullHtml);
                        var contentNode = doc.DocumentNode.SelectSingleNode("//div[@id='mw-content-text']");
                        if (contentNode == null) throw new Exception("Could not find main content element.");

                        string processedHtml = await ProcessHtmlAsync(contentNode.InnerHtml, _fetchStopwatch);
                        DateTime? lastUpdated = await FetchLastUpdatedTimestampAsync(_pageTitleToFetch);

                        if (AppSettings.IsCachingEnabled && lastUpdated.HasValue)
                        {
                            await ArticleCacheManager.SaveArticleToCacheAsync(_pageTitleToFetch, processedHtml, lastUpdated.Value);
                        }

                        if (lastUpdated.HasValue)
                        {
                            LastUpdatedText.Text = $"Last updated: {lastUpdated.Value.ToLocalTime():g}";
                        }

                        await DisplayProcessedHtml(processedHtml);

                        if (!string.IsNullOrEmpty(LastUpdatedText.Text))
                        {
                            LastUpdatedText.Visibility = Visibility.Visible;
                        }

                        LoadingOverlay.Visibility = Visibility.Collapsed;
                        _currentFetchStep = FetchStep.Idle;
                        _fetchStopwatch.Stop();
                        Debug.WriteLine($"[PERF] ===== Total operation time (from network): {_fetchStopwatch.ElapsedMilliseconds} ms =====");
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[ERROR] NavigationCompleted failed: {ex.Message}");
                    LoadingOverlay.Visibility = Visibility.Collapsed;
                    ArticleTitle.Text = "An error occurred";
                }
            });
        }

        private void TitleBar_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            _titleBarHeight = e.NewSize.Height;
            UpdateWebViewPadding();
        }

        private void ArticleDisplayWebView_ContentNavigationCompleted(CoreWebView2 sender, CoreWebView2NavigationCompletedEventArgs args)
        {
            if (args.IsSuccess)
            {
                UpdateWebViewPadding();
            }
        }

        private async void UpdateWebViewPadding()
        {
            if (ArticleDisplayWebView?.CoreWebView2 != null && _titleBarHeight > 0)
            {
                string script = $"document.querySelector('.mw-parser-output').style.paddingTop = '{_titleBarHeight}px';";
                await ArticleDisplayWebView.CoreWebView2.ExecuteScriptAsync(script);
            }
        }

        private async void ArticleDisplayWebView_NavigationStarting(CoreWebView2 sender, CoreWebView2NavigationStartingEventArgs args)
        {
            Uri uri = new Uri(args.Uri);
            if (uri.Host.Equals(VirtualHostName, StringComparison.OrdinalIgnoreCase)) return;
            args.Cancel = true;
            if (uri.Host.Equals("betawiki.net", StringComparison.OrdinalIgnoreCase) && uri.AbsolutePath.StartsWith("/wiki/"))
            {
                string newTitle = uri.AbsolutePath.Substring("/wiki/".Length);
                _pageTitleToFetch = Uri.UnescapeDataString(newTitle);
                _articleHistory.Push(_pageTitleToFetch);
                StartArticleFetch();
            }
            else
            {
                await Windows.System.Launcher.LaunchUriAsync(uri);
            }
        }

        private async Task<string> ProcessHtmlAsync(string rawHtml, Stopwatch stopwatch)
        {
            Debug.WriteLine($"[PERF] Entering ProcessHtmlAsync at: {stopwatch.ElapsedMilliseconds} ms");
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
                    long startTime = stopwatch.ElapsedMilliseconds;
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
                    Debug.WriteLine($"[PERF] Image URL lookup API call took: {stopwatch.ElapsedMilliseconds - startTime} ms");

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
                    long startTime = stopwatch.ElapsedMilliseconds;
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
                    Debug.WriteLine($"[PERF] Initializing {workersToCreate} workers took: {stopwatch.ElapsedMilliseconds - startTime} ms");

                    startTime = stopwatch.ElapsedMilliseconds;
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
                    Debug.WriteLine($"[PERF] Downloading & Caching {allImages.Count} images took: {stopwatch.ElapsedMilliseconds - startTime} ms");
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
                html, body {{ background-color: transparent !important; color: var(--text-primary); font-family: 'Segoe UI Variable', 'Segoe UI', sans-serif; margin: 0; padding: 0 12px 12px 12px; font-size: 15px; -webkit-font-smoothing: antialiased; padding-top: 10px; }}
                a {{ color: var(--link-color); text-decoration: none; }} a:hover {{ text-decoration: underline; }}
                a.selflink, a.new {{ color: var(--text-secondary); pointer-events: none; text-decoration: none; }}
                img {{ max-width: 100%; height: auto; border-radius: 4px; }} .mw-editsection {{ display: none; }}
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
                h2 {{ border-bottom: 1px solid var(--card-border); padding-bottom: 8px; margin-top: 24px; }}
                .reflist {{ font-size: 90%; column-width: 30em; column-gap: 2em; margin-top: 1em; }}
                .reflist ol.references {{ margin: 0; padding-left: 1.6em; }}
                .reflist li {{ margin-bottom: 0.5em; page-break-inside: avoid; break-inside: avoid-column; }}
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

        public bool GoBackInPage()
        {
            if (this.CanGoBackInPage)
            {
                _articleHistory.Pop();
                string previousPageTitle = _articleHistory.Peek();
                _pageTitleToFetch = previousPageTitle.Replace('_', ' ');
                StartArticleFetch();
                return true;
            }
            return false;
        }
    }
}