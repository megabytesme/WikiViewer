using HtmlAgilityPack;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.Graphics;
using Windows.Storage;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;
using muxc = Microsoft.UI.Xaml.Controls;

namespace _1809_UWP
{
    public class ArticleCacheItem
    {
        public string Title { get; set; }
        public DateTime LastUpdated { get; set; }
    }

    public static class ArticleCacheManager
    {
        private static StorageFolder _cacheFolder;
        private static StorageFolder _imageCacheFolder;

        public static async Task InitializeAsync()
        {
            if (_cacheFolder == null)
            {
                _cacheFolder = await ApplicationData.Current.LocalFolder.CreateFolderAsync(
                    "ArticleCache",
                    CreationCollisionOption.OpenIfExists
                );
            }
            if (_imageCacheFolder == null)
            {
                _imageCacheFolder = await ApplicationData.Current.LocalFolder.CreateFolderAsync(
                    "cache",
                    CreationCollisionOption.OpenIfExists
                );
            }
        }

        public static async Task<ulong> GetCacheSizeAsync()
        {
            await InitializeAsync();
            ulong totalSize = 0;

            try
            {
                var articleFiles = await _cacheFolder.GetFilesAsync();
                foreach (var file in articleFiles)
                {
                    var properties = await file.GetBasicPropertiesAsync();
                    totalSize += properties.Size;
                }

                var imageFiles = await _imageCacheFolder.GetFilesAsync();
                foreach (var file in imageFiles)
                {
                    var properties = await file.GetBasicPropertiesAsync();
                    totalSize += properties.Size;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[CACHE] Error calculating cache size: {ex.Message}");
            }

            return totalSize;
        }

        public static async Task ClearCacheAsync()
        {
            await InitializeAsync();

            try
            {
                var articleFiles = await _cacheFolder.GetFilesAsync();
                foreach (var file in articleFiles)
                {
                    await file.DeleteAsync(StorageDeleteOption.PermanentDelete);
                }

                var imageFiles = await _imageCacheFolder.GetFilesAsync();
                foreach (var file in imageFiles)
                {
                    await file.DeleteAsync(StorageDeleteOption.PermanentDelete);
                }

                Debug.WriteLine("[CACHE] All cache folders cleared.");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[CACHE] Error clearing cache: {ex.Message}");
            }
        }

        private static string GetHashedFileName(string pageTitle)
        {
            var hash = System
                .Security.Cryptography.SHA1.Create()
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
                catch
                {
                    return null;
                }
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

        public static async Task SaveArticleToCacheAsync(
            string pageTitle,
            string htmlContent,
            DateTime lastUpdated
        )
        {
            await InitializeAsync();
            string baseFileName = GetHashedFileName(pageTitle);

            var metadata = new ArticleCacheItem { Title = pageTitle, LastUpdated = lastUpdated };
            string json = JsonSerializer.Serialize(metadata);
            StorageFile metadataFile = await _cacheFolder.CreateFileAsync(
                baseFileName + ".json",
                CreationCollisionOption.ReplaceExisting
            );
            await FileIO.WriteTextAsync(metadataFile, json);

            StorageFile htmlFile = await _cacheFolder.CreateFileAsync(
                baseFileName + ".html",
                CreationCollisionOption.ReplaceExisting
            );
            await FileIO.WriteTextAsync(htmlFile, htmlContent);

            Debug.WriteLine($"[CACHE] Saved '{pageTitle}' to cache.");
        }
    }

    public sealed partial class ArticleViewerPage : Page
    {
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
            AuthService.AuthenticationStateChanged += OnAuthenticationStateChanged;
        }

        private void OnAuthenticationStateChanged(object sender, EventArgs e)
        {
            _ = Dispatcher.RunAsync(
                Windows.UI.Core.CoreDispatcherPriority.Normal,
                () =>
                {
                    EditButton.Visibility = AuthService.IsLoggedIn
                        ? Visibility.Visible
                        : Visibility.Collapsed;
                }
            );
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            if (e.Parameter is string pageTitle && !string.IsNullOrEmpty(pageTitle))
            {
                _pageTitleToFetch = pageTitle.Replace(' ', '_');
                if (_articleHistory.Count == 0 || _articleHistory.Peek() != _pageTitleToFetch)
                {
                    _articleHistory.Clear();
                    _articleHistory.Push(_pageTitleToFetch);
                }
            }
            EditButton.Visibility = AuthService.IsLoggedIn
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        private async void Page_Loaded(object sender, RoutedEventArgs e)
        {
            ApplyAcrylicToTitleBar();
            if (_isInitialized)
                return;
            try
            {
                Environment.SetEnvironmentVariable("WEBVIEW2_DEFAULT_BACKGROUND_COLOR", "00FFFFFF");
                await VerificationWebView.EnsureCoreWebView2Async();
                await ArticleDisplayWebView.EnsureCoreWebView2Async();
                var tempFolder = ApplicationData.Current.LocalFolder.Path;
                ArticleDisplayWebView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                    VirtualHostName,
                    tempFolder,
                    CoreWebView2HostResourceAccessKind.Allow
                );
                ArticleDisplayWebView.CoreWebView2.NavigationCompleted +=
                    ArticleDisplayWebView_ContentNavigationCompleted;
                ArticleDisplayWebView.CoreWebView2.NavigationStarting +=
                    ArticleDisplayWebView_NavigationStarting;
                this.Unloaded += ArticleViewerPage_Unloaded;
                _isInitialized = true;
                if (!string.IsNullOrEmpty(_pageTitleToFetch))
                {
                    StartArticleFetch();
                }
            }
            catch (Exception ex)
            {
                ArticleTitle.Text = "Error initializing WebView2";
                LoadingText.Text = ex.Message;
                HideLoadingOverlay();
            }
        }

        private void ArticleViewerPage_Unloaded(object sender, RoutedEventArgs e)
        {
            AuthService.AuthenticationStateChanged -= OnAuthenticationStateChanged;
            this.ActualThemeChanged -= (s, ev) => ApplyAcrylicToTitleBar();
            if (ArticleDisplayWebView?.CoreWebView2 != null)
            {
                ArticleDisplayWebView.CoreWebView2.NavigationCompleted -=
                    ArticleDisplayWebView_ContentNavigationCompleted;
                ArticleDisplayWebView.CoreWebView2.NavigationStarting -=
                    ArticleDisplayWebView_NavigationStarting;
            }
            ArticleDisplayWebView?.Close();
            VerificationWebView?.Close();
        }

        private async void StartArticleFetch()
        {
            if (!_isInitialized || VerificationPanel.Visibility == Visibility.Visible)
                return;
            _fetchStopwatch = Stopwatch.StartNew();
            ShowLoadingOverlay();
            LastUpdatedText.Visibility = Visibility.Collapsed;
            ArticleTitle.Text = _pageTitleToFetch.Replace('_', ' ');
            LoadingText.Text = $"Checking for '{ArticleTitle.Text}'...";
            bool isConnected = NetworkInterface.GetIsNetworkAvailable();

            if (AppSettings.IsCachingEnabled)
            {
                ArticleCacheItem cachedMetadata = await ArticleCacheManager.GetCacheMetadataAsync(
                    _pageTitleToFetch
                );
                DateTime? remoteTimestamp = null;
                if (
                    isConnected
                    && !_pageTitleToFetch.Equals("random", StringComparison.OrdinalIgnoreCase)
                )
                {
                    try
                    {
                        remoteTimestamp = await FetchLastUpdatedTimestampAsync(_pageTitleToFetch);
                    }
                    catch (NeedsUserVerificationException ex)
                    {
                        ShowVerificationPanelAndRetry(ex.Url);
                        return;
                    }
                    catch (Exception) { }
                }
                if (
                    cachedMetadata != null
                    && (
                        !isConnected
                        || remoteTimestamp == null
                        || remoteTimestamp.Value.ToUniversalTime()
                            <= cachedMetadata.LastUpdated.ToUniversalTime()
                    )
                )
                {
                    string cachedHtml = await ArticleCacheManager.GetCachedArticleHtmlAsync(
                        _pageTitleToFetch
                    );
                    if (!string.IsNullOrEmpty(cachedHtml))
                    {
                        await DisplayProcessedHtml(cachedHtml);
                        LastUpdatedText.Text =
                            $"Last updated: {cachedMetadata.LastUpdated.ToLocalTime():g} (Cached)";
                        LastUpdatedText.Visibility = Visibility.Visible;
                        HideLoadingOverlay();
                        return;
                    }
                }
            }
            if (!isConnected)
            {
                ArticleTitle.Text = "No Connection";
                LoadingText.Text = "Please check your internet connection.";
                HideLoadingOverlay();
                return;
            }

            LoadingText.Text = $"Fetching '{ArticleTitle.Text}'...";
            try
            {
                if (_pageTitleToFetch.Equals("random", StringComparison.OrdinalIgnoreCase))
                {
                    await FetchRandomArticleAsync();
                }
                else
                {
                    await FetchSpecificArticleAsync(_pageTitleToFetch);
                }
            }
            catch (NeedsUserVerificationException ex)
            {
                ShowVerificationPanelAndRetry(ex.Url);
            }
            catch (Exception ex)
            {
                HideLoadingOverlay();
                ArticleTitle.Text = "An error occurred";
                LoadingText.Text = ex.Message;
            }
        }

        private async Task<string> DownloadAndCacheImageAsync(
            Uri imageUrl,
            StorageFolder cacheFolder,
            WebView2 worker
        )
        {
            if (imageUrl == null || worker == null || worker.CoreWebView2 == null)
                return null;

            var fileName =
                System
                    .Security.Cryptography.SHA1.Create()
                    .ComputeHash(Encoding.UTF8.GetBytes(imageUrl.AbsoluteUri))
                    .Aggregate("", (s, b) => s + b.ToString("x2"))
                + Path.GetExtension(imageUrl.LocalPath);

            var cachedFile = await cacheFolder.TryGetItemAsync(fileName) as StorageFile;
            if (cachedFile != null)
            {
                return $"/cache/{fileName}";
            }

            var tcs = new TaskCompletionSource<string>();
            TypedEventHandler<
                CoreWebView2,
                CoreWebView2NavigationCompletedEventArgs
            > navigationHandler = null;
            navigationHandler = async (sender, args) =>
            {
                sender.NavigationCompleted -= navigationHandler;
                if (!args.IsSuccess)
                {
                    tcs.TrySetResult(null);
                    return;
                }

                const string script =
                    @"(function() {
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
                    tcs.TrySetResult(
                        string.IsNullOrEmpty(scriptResult) || scriptResult == "null"
                            ? null
                            : JsonSerializer.Deserialize<string>(scriptResult)
                    );
                }
                catch (Exception ex)
                {
                    Debug.WriteLine(
                        $"[ImageCache] Script execution failed for {imageUrl.AbsoluteUri}: {ex.Message}"
                    );
                    tcs.TrySetResult(null);
                }
            };

            worker.CoreWebView2.NavigationCompleted += navigationHandler;
            worker.CoreWebView2.Navigate(imageUrl.AbsoluteUri);

            string base64Data = await tcs.Task;
            if (string.IsNullOrEmpty(base64Data))
                return null;

            var newFile = await cacheFolder.CreateFileAsync(
                fileName,
                CreationCollisionOption.ReplaceExisting
            );
            var bytes = Convert.FromBase64String(base64Data);
            await FileIO.WriteBytesAsync(newFile, bytes);

            return $"/cache/{fileName}";
        }

        private async Task<string> WaitForAndValidateContentAsync(
            Func<string, string> validationLogic,
            string requestUrl
        )
        {
            await WebViewApiService.NavigateAsync(requestUrl);
            var webView = WebViewApiService.GetWebView();

            var stopwatch = Stopwatch.StartNew();
            while (stopwatch.Elapsed.TotalSeconds < 15)
            {
                string html = await webView.ExecuteScriptAsync(
                    "document.documentElement.outerHTML"
                );
                string fullHtml = JsonSerializer.Deserialize<string>(html ?? "null");

                if (!string.IsNullOrEmpty(fullHtml))
                {
                    if (
                        fullHtml.Contains("Verifying you are human")
                        || fullHtml.Contains("checking your browser")
                    )
                    {
                        await Task.Delay(500);
                        continue;
                    }

                    string result = validationLogic(fullHtml);
                    if (result != null)
                    {
                        return result;
                    }
                }
                await Task.Delay(500);
            }
            throw new TimeoutException("Request timed out or content validation failed.");
        }

        private async Task FetchRandomArticleAsync()
        {
            string url =
                $"{ApiBaseUrl}?action=query&list=random&rnnamespace=0&rnlimit=1&format=json";
            string json = await GetStringFromUrlAsync(url);
            var randomResponse = JsonSerializer.Deserialize<RandomQueryResponse>(json);
            string randomTitle = randomResponse?.query?.random?.FirstOrDefault()?.title;
            if (string.IsNullOrEmpty(randomTitle))
                throw new Exception("Failed to get a random title from API response.");

            if (
                _articleHistory.Count > 0
                && _articleHistory.Peek().Equals("random", StringComparison.OrdinalIgnoreCase)
            )
            {
                _articleHistory.Pop();
                _articleHistory.Push(randomTitle);
            }
            _pageTitleToFetch = randomTitle;
            ArticleTitle.Text = _pageTitleToFetch.Replace('_', ' ');
            await FetchSpecificArticleAsync(_pageTitleToFetch);
        }

        private async Task FetchSpecificArticleAsync(string title)
        {
            string pageUrl = $"https://betawiki.net/wiki/{Uri.EscapeDataString(title)}";
            string articleHtml = await GetHtmlFromUrlAsync(pageUrl, "//div[@id='mw-content-text']");

            var finalDoc = new HtmlDocument();
            finalDoc.LoadHtml(articleHtml);
            var contentNode = finalDoc.DocumentNode.SelectSingleNode(
                "//div[@id='mw-content-text']"
            );

            string processedHtml = await ProcessHtmlAsync(contentNode.InnerHtml, _fetchStopwatch);
            DateTime? lastUpdated = await FetchLastUpdatedTimestampAsync(title)
                .ContinueWith(t => t.IsFaulted ? (DateTime?)null : t.Result);
            if (AppSettings.IsCachingEnabled && lastUpdated.HasValue)
            {
                await ArticleCacheManager.SaveArticleToCacheAsync(
                    title,
                    processedHtml,
                    lastUpdated.Value
                );
            }
            if (lastUpdated.HasValue)
            {
                LastUpdatedText.Text = $"Last updated: {lastUpdated.Value.ToLocalTime():g}";
                LastUpdatedText.Visibility = Visibility.Visible;
            }
            await DisplayProcessedHtml(processedHtml);
            HideLoadingOverlay();
        }

        private async Task<DateTime?> FetchLastUpdatedTimestampAsync(string pageTitle)
        {
            if (
                string.IsNullOrEmpty(pageTitle)
                || pageTitle.Equals("random", StringComparison.OrdinalIgnoreCase)
            )
                return null;
            string url =
                $"{ApiBaseUrl}?action=query&prop=revisions&titles={Uri.EscapeDataString(pageTitle)}&rvprop=timestamp&rvlimit=1&format=json";
            string json = await GetStringFromUrlAsync(url);
            var response = JsonSerializer.Deserialize<TimestampQueryResponse>(json);
            return response
                ?.query?.pages?.Values.FirstOrDefault()
                ?.Revisions?.FirstOrDefault()
                ?.Timestamp;
        }

        private async Task DisplayProcessedHtml(string html)
        {
            StorageFolder localFolder = ApplicationData.Current.LocalFolder;
            StorageFile articleFile = await localFolder.CreateFileAsync(
                "article.html",
                CreationCollisionOption.ReplaceExisting
            );
            await FileIO.WriteTextAsync(articleFile, html);
            ArticleDisplayWebView.CoreWebView2.Navigate($"https://{VirtualHostName}/article.html");
        }

        private async void ArticleDisplayWebView_NavigationStarting(CoreWebView2 sender, CoreWebView2NavigationStartingEventArgs args)
        {
            Uri uri;
            try { uri = new Uri(args.Uri); }
            catch { args.Cancel = true; return; }

            if (uri.Host.Equals(VirtualHostName, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            args.Cancel = true;

            if (uri.Host.Equals("betawiki.net", StringComparison.OrdinalIgnoreCase))
            {
                string newTitle = null;
                if (uri.AbsolutePath.StartsWith("/wiki/"))
                {
                    newTitle = uri.AbsolutePath.Substring("/wiki/".Length);
                }
                else if (uri.AbsolutePath.StartsWith("/index.php") && uri.Query.Contains("title="))
                {
                    var queryParams = System.Web.HttpUtility.ParseQueryString(uri.Query);
                    newTitle = queryParams["title"];
                }

                if (!string.IsNullOrEmpty(newTitle))
                {
                    _pageTitleToFetch = Uri.UnescapeDataString(newTitle);
                    _articleHistory.Push(_pageTitleToFetch);
                    StartArticleFetch();
                    return;
                }
            }

            await Windows.System.Launcher.LaunchUriAsync(uri);
        }

        public bool GoBackInPage()
        {
            if (CanGoBackInPage)
            {
                _articleHistory.Pop();
                _pageTitleToFetch = _articleHistory.Peek();
                StartArticleFetch();
                return true;
            }
            return false;
        }

        private void ShowLoadingOverlay()
        {
            LoadingOverlay.IsHitTestVisible = true;
            FadeInAnimation.Begin();
        }

        private void HideLoadingOverlay()
        {
            void onAnimationCompleted(object s, object e)
            {
                LoadingOverlay.IsHitTestVisible = false;
                FadeOutAnimation.Completed -= onAnimationCompleted;
            }
            FadeOutAnimation.Completed += onAnimationCompleted;
            FadeOutAnimation.Begin();
        }

        private void EditButton_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(_pageTitleToFetch))
            {
                Frame.Navigate(typeof(EditPage), _pageTitleToFetch);
            }
        }

        private void ApplyAcrylicToTitleBar()
        {
            var acrylicBrush = new AcrylicBrush
            {
                BackgroundSource = AcrylicBackgroundSource.Backdrop,
                TintOpacity = 0.4,
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

        private void TitleBar_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            _titleBarHeight = e.NewSize.Height;
            UpdateWebViewPadding();
        }

        private void ArticleDisplayWebView_ContentNavigationCompleted(
            CoreWebView2 sender,
            CoreWebView2NavigationCompletedEventArgs args
        )
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
                await ArticleDisplayWebView.CoreWebView2.ExecuteScriptAsync(
                    $"document.body.style.paddingTop = '{_titleBarHeight}px';"
                );
            }
        }

        private async Task<string> GetStringFromUrlAsync(string requestUrl)
        {
            return await WaitForAndValidateContentAsync(
                requestUrl,
                (html) =>
                {
                    var doc = new HtmlDocument();
                    doc.LoadHtml(html);
                    string text = doc.DocumentNode.SelectSingleNode("//body/pre")?.InnerText;
                    return (!string.IsNullOrEmpty(text) && text.Trim().StartsWith("{"))
                        ? text
                        : null;
                }
            );
        }

        private async Task<string> GetHtmlFromUrlAsync(string requestUrl, string validationXPath)
        {
            return await WaitForAndValidateContentAsync(
                requestUrl,
                (html) =>
                {
                    var doc = new HtmlDocument();
                    doc.LoadHtml(html);
                    return doc.DocumentNode.SelectSingleNode(validationXPath) != null ? html : null;
                }
            );
        }

        private async Task<string> WaitForAndValidateContentAsync(
            string requestUrl,
            Func<string, string> validationLogic
        )
        {
            await WebViewApiService.NavigateAsync(requestUrl);
            var webView = WebViewApiService.GetWebView();

            var stopwatch = Stopwatch.StartNew();
            while (stopwatch.Elapsed.TotalSeconds < 15)
            {
                string html = await webView.ExecuteScriptAsync(
                    "document.documentElement.outerHTML"
                );
                string fullHtml = JsonSerializer.Deserialize<string>(html ?? "null");

                if (!string.IsNullOrEmpty(fullHtml))
                {
                    if (
                        fullHtml.Contains("g-recaptcha")
                        || fullHtml.Contains("h-captcha")
                        || fullHtml.Contains("turnstile")
                        || fullHtml.Contains("challenge-form")
                    )
                    {
                        throw new NeedsUserVerificationException(
                            "Interactive user verification required.",
                            requestUrl
                        );
                    }

                    if (
                        fullHtml.Contains("Verifying you are human")
                        || fullHtml.Contains("checking your browser")
                    )
                    {
                        await Task.Delay(500);
                        continue;
                    }

                    string result = validationLogic(fullHtml);
                    if (result != null)
                        return result;
                }
                await Task.Delay(500);
            }
            throw new TimeoutException("Request timed out or content validation failed.");
        }

        private void ShowVerificationPanelAndRetry(string url)
        {
            HideLoadingOverlay();
            VerificationPanel.Visibility = Visibility.Visible;
            TypedEventHandler<
                CoreWebView2,
                CoreWebView2NavigationCompletedEventArgs
            > successHandler = null;
            successHandler = async (sender, args) =>
            {
                if (args.IsSuccess)
                {
                    await Dispatcher.RunAsync(
                        Windows.UI.Core.CoreDispatcherPriority.Normal,
                        () =>
                        {
                            VerificationWebView.CoreWebView2.NavigationCompleted -= successHandler;
                            VerificationPanel.Visibility = Visibility.Collapsed;
                            StartArticleFetch();
                        }
                    );
                }
            };
            VerificationWebView.CoreWebView2.NavigationCompleted += successHandler;
            VerificationWebView.CoreWebView2.Navigate(url);
        }

        private async Task<string> ProcessHtmlAsync(string rawHtml, Stopwatch stopwatch)
        {
            Debug.WriteLine(
                $"[PERF] Entering ProcessHtmlAsync at: {stopwatch.ElapsedMilliseconds} ms"
            );
            var doc = new HtmlDocument();
            doc.LoadHtml(rawHtml);
            string baseUrl = "https://betawiki.net";
            var baseUri = new Uri(baseUrl);

            var nodesToRemove = doc.DocumentNode.SelectNodes(
                "//link[contains(@href, 'mw-data:TemplateStyles')] | //style[contains(@data-mw-deduplicate, 'TemplateStyles')]"
            );
            if (nodesToRemove != null)
            {
                foreach (var node in nodesToRemove)
                {
                    node.Remove();
                }
            }
            var legendCells = doc.DocumentNode.SelectNodes(
                "//td[contains(@class, 'table-version')]"
            );
            if (legendCells != null)
            {
                foreach (var cell in legendCells)
                {
                    cell.Attributes.Remove("style");
                }
            }

            StorageFolder localCacheFolder = ApplicationData.Current.LocalFolder;
            StorageFolder imageCacheFolder = await localCacheFolder.CreateFolderAsync(
                "cache",
                CreationCollisionOption.OpenIfExists
            );
            List<WebView2> webViewWorkers = new List<WebView2>();
            SemaphoreSlim workerSemaphore = null;

            try
            {
                var imageLinks = doc.DocumentNode.SelectNodes(
                    "//a[starts-with(@href, '/wiki/File:')]"
                );
                if (imageLinks != null && imageLinks.Any())
                {
                    long startTime = stopwatch.ElapsedMilliseconds;
                    var imageFileNames = imageLinks
                        .Select(link => link.GetAttributeValue("href", "").Substring(6))
                        .Distinct()
                        .ToList();
                    var titles = string.Join("|", imageFileNames.Select(Uri.EscapeDataString));
                    var imageUrlApi =
                        $"{ApiBaseUrl}?action=query&prop=imageinfo&iiprop=url&format=json&titles={titles}";

                    string imageJsonResponse = await GetStringFromUrlAsync(imageUrlApi);
                    Debug.WriteLine(
                        $"[PERF] Image URL lookup API call took: {stopwatch.ElapsedMilliseconds - startTime} ms"
                    );

                    if (!string.IsNullOrEmpty(imageJsonResponse))
                    {
                        var imageInfoResponse = JsonSerializer.Deserialize<ImageQueryResponse>(
                            imageJsonResponse
                        );
                        if (imageInfoResponse?.query?.pages != null)
                        {
                            var imageUrlMap = imageInfoResponse
                                .query.pages.Values.Where(p =>
                                    p.imageinfo?.FirstOrDefault()?.url != null
                                )
                                .ToDictionary(p => p.title, p => p.imageinfo.First().url);

                            if (imageUrlMap.Any())
                            {
                                foreach (var link in imageLinks)
                                {
                                    string lookupKey = Uri.UnescapeDataString(
                                            link.GetAttributeValue("href", "").Substring(6)
                                        )
                                        .Replace('_', ' ');
                                    if (imageUrlMap.TryGetValue(lookupKey, out string fullImageUrl))
                                    {
                                        var img = link.SelectSingleNode(".//img");
                                        if (img != null)
                                            img.SetAttributeValue("src", fullImageUrl);
                                    }
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
                    foreach (var worker in webViewWorkers)
                        availableWorkers.Enqueue(worker);
                    Debug.WriteLine(
                        $"[PERF] Initializing {workersToCreate} workers took: {stopwatch.ElapsedMilliseconds - startTime} ms"
                    );

                    startTime = stopwatch.ElapsedMilliseconds;

                    var uniqueImageUrls = allImages
                        .Select(img =>
                            img.GetAttributeValue("srcset", null)
                                ?.Split(',')
                                .FirstOrDefault()
                                ?.Trim()
                                .Split(' ')[0] ?? img.GetAttributeValue("src", null)
                        )
                        .Where(src => !string.IsNullOrEmpty(src))
                        .Distinct()
                        .ToList();

                    var downloadTasks = uniqueImageUrls
                        .Select(async originalUrl =>
                        {
                            if (!Uri.TryCreate(baseUri, originalUrl, out Uri resultUri))
                            {
                                return new { OriginalUrl = originalUrl, LocalPath = (string)null };
                            }

                            await workerSemaphore.WaitAsync();
                            availableWorkers.TryDequeue(out WebView2 worker);
                            try
                            {
                                string localPath = await DownloadAndCacheImageAsync(
                                    resultUri,
                                    imageCacheFolder,
                                    worker
                                );
                                return new { OriginalUrl = originalUrl, LocalPath = localPath };
                            }
                            finally
                            {
                                if (worker != null)
                                    availableWorkers.Enqueue(worker);
                                workerSemaphore.Release();
                            }
                        })
                        .ToList();

                    var downloadResults = await Task.WhenAll(downloadTasks);
                    var urlToLocalPathMap = downloadResults
                        .Where(r => r.LocalPath != null)
                        .ToDictionary(r => r.OriginalUrl, r => r.LocalPath);

                    await Dispatcher.RunAsync(
                        Windows.UI.Core.CoreDispatcherPriority.Normal,
                        () =>
                        {
                            foreach (var img in allImages)
                            {
                                string originalSrc =
                                    img.GetAttributeValue("srcset", null)
                                        ?.Split(',')
                                        .FirstOrDefault()
                                        ?.Trim()
                                        .Split(' ')[0] ?? img.GetAttributeValue("src", null);
                                if (
                                    urlToLocalPathMap.TryGetValue(
                                        originalSrc,
                                        out string localImagePath
                                    )
                                )
                                {
                                    img.SetAttributeValue(
                                        "src",
                                        $"https://{VirtualHostName}{localImagePath}"
                                    );
                                    img.Attributes.Remove("srcset");
                                }
                            }
                        }
                    );

                    Debug.WriteLine(
                        $"[PERF] Downloading & Caching {uniqueImageUrls.Count} unique images took: {stopwatch.ElapsedMilliseconds - startTime} ms"
                    );
                }
            }
            finally
            {
                foreach (var worker in webViewWorkers)
                {
                    worker?.Close();
                }
                WorkerWebViewHost.Children.Clear();
                workerSemaphore?.Dispose();
            }

            foreach (var link in doc.DocumentNode.SelectNodes("//a[@href]") ?? Enumerable.Empty<HtmlNode>())
            {
                string href = link.GetAttributeValue("href", "");
                if (href.StartsWith("/wiki/") || href.StartsWith("/index.php?"))
                {
                    link.SetAttributeValue("href", baseUrl + href);
                }
            }

            var isDarkTheme = Application.Current.RequestedTheme == ApplicationTheme.Dark;
            string cssVariables = isDarkTheme
                ? @":root {
    --text-primary: #FFFFFF; --text-secondary: #C3C3C3; --link-color: #85B9F3; --card-shadow: rgba(0, 0, 0, 0.4);
    --card-background: rgba(44, 44, 44, 0.7); --card-border: rgba(255, 255, 255, 0.1); --card-header-background: rgba(255, 255, 255, 0.08);
    --item-hover-background: rgba(255, 255, 255, 0.07); --table-row-divider: rgba(255, 255, 255, 0.08);
    --legend-unsupported-tint: linear-gradient(rgba(239, 68, 68, 0.15), rgba(239, 68, 68, 0.15));
    --legend-supported-tint: linear-gradient(rgba(234, 179, 8, 0.15), rgba(234, 179, 8, 0.15));
    --legend-latest-tint: linear-gradient(rgba(34, 197, 94, 0.15), rgba(34, 197, 94, 0.15));
    --legend-preview-tint: linear-gradient(rgba(249, 115, 22, 0.15), rgba(249, 115, 22, 0.15));
    --legend-future-tint: linear-gradient(rgba(59, 130, 246, 0.15), rgba(59, 130, 246, 0.15));
    --legend-na-tint: linear-gradient(rgba(255, 255, 255, 0.04), rgba(255, 255, 255, 0.04));
}"
                : @":root {
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

            var style =
                $@"<style>
{cssVariables}
html, body {{ background-color: transparent !important; color: var(--text-primary); font-family: 'Segoe UI Variable', 'Segoe UI', sans-serif; margin: 0; padding: 0; font-size: 15px; -webkit-font-smoothing: antialiased; }}
.mw-parser-output {{ padding: 0px 16px 12px 16px; }}
a {{ color: var(--link-color); text-decoration: none; }} a:hover {{ text-decoration: underline; }}
a.selflink, a.new {{ color: var(--text-secondary); pointer-events: none; text-decoration: none; }}
img {{ max-width: 100%; height: auto; border-radius: 4px; }} .mw-editsection {{ display: none; }}
h2 {{ border-bottom: 1px solid var(--card-border); padding-bottom: 8px; margin-top: 24px; }}
.reflist {{ font-size: 90%; column-width: 30em; column-gap: 2em; margin-top: 1em; }}
.reflist ol.references {{ margin: 0; padding-left: 1.6em; }}
.reflist li {{ margin-bottom: 0.5em; page-break-inside: avoid; break-inside: avoid-column; }}
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
    <meta name='viewport' content='width=device-width, initial-scale-1.0'>
    {style}
</head>
<body>
    <div class='mw-parser-output'>
        {doc.DocumentNode.OuterHtml}
    </div>
</body>
</html>";
        }
    }
}
