using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using HtmlAgilityPack;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
using Windows.ApplicationModel.Core;
using Windows.Storage;
using Windows.UI.Core;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace _1809_UWP
{
    public static class ArticleProcessingService
    {
        public static async Task<(string Html, string ResolvedTitle)> FetchAndCacheArticleAsync(
            string pageTitle,
            Stopwatch stopwatch,
            bool forceRefresh = false,
            WebView2 worker = null,
            SemaphoreSlim semaphore = null
        )
        {
            long startTime = stopwatch.ElapsedMilliseconds;
            Debug.WriteLine(
                $"[FETCHER] Starting fetch process for '{pageTitle}' at {startTime}ms."
            );

            var workerToUse = worker ?? MainPage.ApiWorker;

            string resolvedTitle = pageTitle;
            if (string.IsNullOrEmpty(pageTitle))
            {
                throw new ArgumentNullException(nameof(pageTitle));
            }

            if (pageTitle.Equals("random", StringComparison.OrdinalIgnoreCase))
            {
                string randomTitleJson = await ApiRequestService.GetJsonFromApiAsync(
                    $"{AppSettings.ApiEndpoint}?action=query&list=random&rnnamespace=0&rnlimit=1&format=json",
                    workerToUse
                );
                if (string.IsNullOrEmpty(randomTitleJson))
                    throw new Exception(
                        "Failed to retrieve a response for a random page from the API."
                    );
                var randomResponse = JsonConvert.DeserializeObject<RandomQueryResponse>(
                    randomTitleJson
                );
                resolvedTitle = randomResponse?.query?.random?.FirstOrDefault()?.title;
                if (string.IsNullOrEmpty(resolvedTitle))
                    throw new Exception("Failed to get a random title from API response.");
            }

            bool isConnected = NetworkInterface.GetIsNetworkAvailable();

            if (AppSettings.IsCachingEnabled && !forceRefresh)
            {
                var cachedMetadata = await ArticleCacheManager.GetCacheMetadataAsync(resolvedTitle);
                DateTime? remoteTimestamp = isConnected
                    ? await FetchLastUpdatedTimestampAsync(resolvedTitle, workerToUse)
                    : null;

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
                        resolvedTitle
                    );
                    if (!string.IsNullOrEmpty(cachedHtml))
                    {
                        long cacheHitTime = stopwatch.ElapsedMilliseconds;
                        Debug.WriteLine(
                            $"[FETCHER] Cache hit for '{resolvedTitle}' at {cacheHitTime}ms. Duration: {cacheHitTime - startTime}ms."
                        );
                        return (cachedHtml, resolvedTitle);
                    }
                }
            }

            if (!isConnected)
            {
                throw new Exception(
                    "No network connection and a fresh copy of the article is needed."
                );
            }

            string pageUrl = AppSettings.GetWikiPageUrl(resolvedTitle);

            long downloadStartTime = stopwatch.ElapsedMilliseconds;
            Debug.WriteLine(
                $"[FETCHER] Starting raw HTML download for '{resolvedTitle}' at {downloadStartTime}ms."
            );

            var freshHtml = await ApiRequestService.GetRawHtmlFromUrlAsync(pageUrl, workerToUse);

            long downloadEndTime = stopwatch.ElapsedMilliseconds;
            Debug.WriteLine(
                $"[FETCHER] Finished raw HTML download. Duration: {downloadEndTime - downloadStartTime}ms."
            );

            var processedHtml = await ProcessHtmlAsync(
                freshHtml,
                stopwatch,
                workerToUse,
                semaphore
            );

            var lastUpdated = await FetchLastUpdatedTimestampAsync(resolvedTitle, workerToUse);
            if (AppSettings.IsCachingEnabled && lastUpdated.HasValue)
            {
                await ArticleCacheManager.SaveArticleToCacheAsync(
                    resolvedTitle,
                    processedHtml,
                    lastUpdated.Value
                );
            }

            long endTime = stopwatch.ElapsedMilliseconds;
            Debug.WriteLine(
                $"[FETCHER] Finished fetch process for '{resolvedTitle}' at {endTime}ms. Total Duration: {endTime - startTime}ms."
            );

            return (processedHtml, resolvedTitle);
        }

        public static async Task<string> ProcessHtmlAsync(
            string rawHtml,
            Stopwatch stopwatch,
            WebView2 worker,
            SemaphoreSlim semaphore = null
        )
        {
            long startTime = stopwatch.ElapsedMilliseconds;
            Debug.WriteLine($"[PROCESSOR] Starting HTML processing at {startTime}ms.");

            var doc = new HtmlDocument();
            doc.LoadHtml(rawHtml);
            var contentNode =
                doc.DocumentNode.SelectSingleNode("//div[@id='mw-content-text']")
                ?? doc.DocumentNode;

            await ProcessImagesInDocument(doc, worker, semaphore);

            foreach (
                var link in doc.DocumentNode.SelectNodes("//a[@href]")
                    ?? Enumerable.Empty<HtmlNode>()
            )
            {
                string href = link.GetAttributeValue("href", "");
                if (href.StartsWith($"/{AppSettings.ArticlePath}") || href.StartsWith($"/{AppSettings.ScriptPath}index.php?"))
                {
                    link.SetAttributeValue("href", AppSettings.BaseUrl.TrimEnd('/') + href);
                }
            }

            string styleBlock = GetCssForTheme();

            long endTime = stopwatch.ElapsedMilliseconds;
            Debug.WriteLine(
                $"[PROCESSOR] Finished HTML processing at {endTime}ms. Duration: {endTime - startTime}ms."
            );

            return $@"
    <!DOCTYPE html><html><head><meta charset='UTF-8'><meta name='viewport' content='width=device-width, initial-scale=1.0'>{styleBlock}</head>
    <body><div class='mw-parser-output'>{contentNode.InnerHtml}</div></body></html>";
        }

        private static async Task ProcessImagesInDocument(
            HtmlDocument doc,
            WebView2 worker,
            SemaphoreSlim semaphore = null
        )
        {
            var imageLinks = doc.DocumentNode.SelectNodes($"//a[starts-with(@href, '/{AppSettings.ArticlePath}File:')]");
            if (imageLinks != null && imageLinks.Any())
            {
                var imageFileNames = imageLinks
                    .Select(link => link.GetAttributeValue("href", "").Substring(AppSettings.ArticlePath.Length + 1))
                    .Distinct()
                    .ToList();
                var titles = string.Join("|", imageFileNames.Select(Uri.EscapeDataString));
                var imageUrlApi =
                    $"{AppSettings.ApiEndpoint}?action=query&prop=imageinfo&iiprop=url&format=json&titles={titles}";

                try
                {
                    string imageJsonResponse = await ApiRequestService.GetJsonFromApiAsync(
                        imageUrlApi,
                        worker
                    );
                    if (!string.IsNullOrEmpty(imageJsonResponse))
                    {
                        var imageInfoResponse = JsonConvert.DeserializeObject<ImageQueryResponse>(
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
                                            link.GetAttributeValue("href", "").Substring(AppSettings.ArticlePath.Length + 1)
                                        )
                                        .Replace('_', ' ');
                                    if (imageUrlMap.TryGetValue(lookupKey, out string fullImageUrl))
                                    {
                                        var img = link.SelectSingleNode(".//img");
                                        img?.SetAttributeValue("src", fullImageUrl);
                                    }
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[PROCESSOR] Failed image URL lookup: {ex.Message}");
                }
            }

            var allImages = doc.DocumentNode.SelectNodes("//img");
            if (allImages == null || !allImages.Any())
                return;

            var semaphoreToUse = semaphore ?? new SemaphoreSlim(AppSettings.MaxConcurrentDownloads);

            var uniqueImageUrls = allImages
                .Select(img =>
                    img.GetAttributeValue("srcset", null)
                        ?.Split(',')
                        .FirstOrDefault()
                        ?.Trim()
                        .Split(' ')[0] ?? img.GetAttributeValue("src", null)
                )
                .Where(src => !string.IsNullOrEmpty(src) && !src.StartsWith("data:"))
                .Distinct()
                .ToList();

            var downloadTasks = uniqueImageUrls
                .Select(async originalUrl =>
                {
                    await semaphoreToUse.WaitAsync();
                    try
                    {
                        string localPath = await DownloadAndCacheImageAsync(originalUrl);
                        return new { OriginalUrl = originalUrl, LocalPath = localPath };
                    }
                    finally
                    {
                        semaphoreToUse.Release();
                    }
                })
                .ToList();

            var downloadResults = await Task.WhenAll(downloadTasks);
            var urlToLocalPathMap = downloadResults
                .Where(r => r.LocalPath != null)
                .ToDictionary(r => r.OriginalUrl, r => r.LocalPath);

            foreach (var img in allImages)
            {
                string originalSrc =
                    img.GetAttributeValue("srcset", null)
                        ?.Split(',')
                        .FirstOrDefault()
                        ?.Trim()
                        .Split(' ')[0] ?? img.GetAttributeValue("src", null);
                if (urlToLocalPathMap.TryGetValue(originalSrc, out string localImagePath))
                {
                    img.SetAttributeValue(
                        "src",
                        $"https://{ArticleViewerPage.GetVirtualHostName()}{localImagePath}"
                    );
                    img.Attributes.Remove("srcset");
                }
            }
        }

        private static async Task<string> DownloadAndCacheImageAsync(string originalUrl)
        {
            if (!Uri.TryCreate(new Uri(AppSettings.BaseUrl), originalUrl, out Uri imageUrl))
                return null;

            var extension = Path.GetExtension(imageUrl.LocalPath).ToLowerInvariant();
            var hash = System
                .Security.Cryptography.SHA1.Create()
                .ComputeHash(Encoding.UTF8.GetBytes(imageUrl.AbsoluteUri));
            var baseFileName = hash.Aggregate("", (s, b) => s + b.ToString("x2"));
            var finalFileName = baseFileName + extension;

            var imageCacheFolder = await ApplicationData.Current.LocalFolder.CreateFolderAsync(
                "cache",
                CreationCollisionOption.OpenIfExists
            );

            if (await imageCacheFolder.TryGetItemAsync(finalFileName) is StorageFile cachedFile)
                return $"/cache/{finalFileName}";

            var tcs = new TaskCompletionSource<bool>();

            _ = CoreApplication.MainView.CoreWindow.Dispatcher.RunAsync(
                CoreDispatcherPriority.Normal,
                async () =>
                {
                    if (App.UIHost == null)
                    {
                        tcs.TrySetResult(false);
                        return;
                    }

                    var tempWorker = new WebView2();
                    try
                    {
                        App.UIHost.Children.Add(tempWorker);
                        await tempWorker.EnsureCoreWebView2Async();

                        if (MainPage.ApiWorker?.CoreWebView2 != null)
                        {
                            await ApiRequestService.CopyApiCookiesAsync(
                                MainPage.ApiWorker.CoreWebView2,
                                tempWorker.CoreWebView2
                            );
                        }

                        if (extension == ".svg")
                        {
                            string svgContent = await ApiRequestService.GetRawHtmlFromUrlAsync(
                                imageUrl.AbsoluteUri,
                                tempWorker
                            );
                            if (string.IsNullOrEmpty(svgContent))
                                throw new Exception("Downloaded SVG content was null or empty.");

                            var newFile = await imageCacheFolder.CreateFileAsync(
                                finalFileName,
                                CreationCollisionOption.ReplaceExisting
                            );
                            await FileIO.WriteTextAsync(newFile, svgContent);
                        }
                        else
                        {
                            var navTcs = new TaskCompletionSource<bool>();
                            void navHandler(
                                CoreWebView2 s,
                                CoreWebView2NavigationCompletedEventArgs e
                            )
                            {
                                s.NavigationCompleted -= navHandler;
                                navTcs.TrySetResult(e.IsSuccess);
                            }

                            tempWorker.CoreWebView2.NavigationCompleted += navHandler;
                            tempWorker.CoreWebView2.Navigate(imageUrl.AbsoluteUri);
                            if (!await navTcs.Task)
                                throw new Exception(
                                    $"Image navigation failed for {imageUrl.AbsoluteUri}"
                                );

                            const string rasterImageScript =
                                @"(function() { const img = document.querySelector('img'); if (img && img.naturalWidth > 0) { const canvas = document.createElement('canvas'); canvas.width = img.naturalWidth; canvas.height = img.naturalHeight; const ctx = canvas.getContext('2d'); ctx.drawImage(img, 0, 0); return canvas.toDataURL('image/png').split(',')[1]; } return null; })();";
                            string scriptResult = await tempWorker.CoreWebView2.ExecuteScriptAsync(
                                rasterImageScript
                            );

                            if (string.IsNullOrEmpty(scriptResult) || scriptResult == "null")
                                throw new Exception("Failed to get Base64 data from script.");
                            var base64Data = JsonConvert.DeserializeObject<string>(scriptResult);
                            var bytes = Convert.FromBase64String(base64Data);

                            var newFile = await imageCacheFolder.CreateFileAsync(
                                finalFileName,
                                CreationCollisionOption.ReplaceExisting
                            );
                            await FileIO.WriteBytesAsync(newFile, bytes);
                        }

                        Debug.WriteLine(
                            $"[ImageDownloader] Cached image {imageUrl} as {finalFileName} in {imageCacheFolder.Path}"
                        );
                        tcs.TrySetResult(true);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine(
                            $"[ImageDownloader] Critical failure for {imageUrl}. Reason: {ex.Message}"
                        );
                        tcs.TrySetResult(false);
                    }
                    finally
                    {
                        App.UIHost.Children.Remove(tempWorker);
                        tempWorker.Close();
                    }
                }
            );

            bool success = await tcs.Task;
            return success ? $"/cache/{finalFileName}" : null;
        }

        public static async Task<DateTime?> FetchLastUpdatedTimestampAsync(
            string pageTitle,
            WebView2 worker
        )
        {
            if (
                string.IsNullOrEmpty(pageTitle)
                || pageTitle.Equals("random", StringComparison.OrdinalIgnoreCase)
            )
                return null;
            string url =
                $"{AppSettings.ApiEndpoint}?action=query&prop=revisions&titles={Uri.EscapeDataString(pageTitle)}&rvprop=timestamp&rvlimit=1&format=json";
            try
            {
                string json = await ApiRequestService.GetJsonFromApiAsync(url, worker);
                var response = JsonConvert.DeserializeObject<TimestampQueryResponse>(json);
                return response
                    ?.query?.pages?.Values.FirstOrDefault()
                    ?.Revisions?.FirstOrDefault()
                    ?.Timestamp;
            }
            catch (Exception ex)
            {
                Debug.WriteLine(
                    $"[PROCESSOR] Failed to get timestamp for '{pageTitle}': {ex.Message}"
                );
                return null;
            }
        }

        public static async Task<bool> PageExistsAsync(string pageTitle, WebView2 worker)
        {
            if (string.IsNullOrEmpty(pageTitle))
                return false;

            var workerToUse = worker ?? MainPage.ApiWorker;
            var escapedTitle = Uri.EscapeDataString(pageTitle);
            var url = $"{AppSettings.ApiEndpoint}?action=query&titles={escapedTitle}&format=json";

            try
            {
                string json = await ApiRequestService.GetJsonFromApiAsync(url, workerToUse);
                if (string.IsNullOrEmpty(json))
                    return false;

                JObject root = JObject.Parse(json);
                var pages = root?["query"]?["pages"];
                if (pages == null) return false;

                var firstPage = pages.First as JProperty;
                if (firstPage?.Value?["missing"] != null || firstPage?.Name == "-1")
                {
                    return false;
                }
                return true;
            }
            catch (NeedsUserVerificationException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Debug.WriteLine(
                    $"[PROCESSOR] PageExistsAsync check failed for '{pageTitle}': {ex.Message}"
                );
                throw new Exception($"Page check failed for '{pageTitle}'", ex);
            }
        }

        public static async Task<string> ProcessHtmlAsync(string rawHtml, WebView2 worker)
        {
            var doc = new HtmlDocument();
            doc.LoadHtml(rawHtml);
            var contentNode =
                doc.DocumentNode.SelectSingleNode("//div[@id='mw-content-text']")
                ?? doc.DocumentNode;

            await ProcessImagesInDocument(doc, worker);

            foreach (
                var link in doc.DocumentNode.SelectNodes("//a[@href]")
                    ?? Enumerable.Empty<HtmlNode>()
            )
            {
                string href = link.GetAttributeValue("href", "");
                if (href.StartsWith($"/{AppSettings.ArticlePath}") || href.StartsWith($"/{AppSettings.ScriptPath}index.php?"))
                {
                    link.SetAttributeValue("href", AppSettings.BaseUrl.TrimEnd('/') + href);
                }
            }

            return contentNode.OuterHtml;
        }

        public static string GetCssForTheme()
        {
            return @"<style>
                    :root { /* Light Theme (default) */
                        --text-primary: #000000; --text-secondary: #505050; --link-color: #0066CC; --card-shadow: rgba(0, 0, 0, 0.13);
                        --card-background: rgba(249, 249, 249, 0.7); --card-border: rgba(0, 0, 0, 0.1); --card-header-background: rgba(0, 0, 0, 0.05);
                        --item-hover-background: rgba(0, 0, 0, 0.05); --table-row-divider: rgba(0, 0, 0, 0.08);
                        --legend-unsupported-tint: linear-gradient(rgba(239, 68, 68, 0.1), rgba(239, 68, 68, 0.1));
                        --legend-supported-tint: linear-gradient(rgba(234, 179, 8, 0.1), rgba(234, 179, 8, 0.1));
                        --legend-latest-tint: linear-gradient(rgba(34, 197, 94, 0.1), rgba(34, 197, 94, 0.1));
                        --legend-preview-tint: linear-gradient(rgba(249, 115, 22, 0.1), rgba(249, 115, 22, 0.1));
                        --legend-future-tint: linear-gradient(rgba(59, 130, 246, 0.1), rgba(59, 130, 246, 0.1));
                        --legend-na-tint: linear-gradient(rgba(0, 0, 0, 0.04), rgba(0, 0, 0, 0.04));
                    }

                    @media (prefers-color-scheme: dark) {
                        :root { /* Dark Theme overrides */
                            --text-primary: #FFFFFF; --text-secondary: #C3C3C3; --link-color: #85B9F3; --card-shadow: rgba(0, 0, 0, 0.4);
                            --card-background: rgba(44, 44, 44, 0.7); --card-border: rgba(255, 255, 255, 0.1); --card-header-background: rgba(255, 255, 255, 0.08);
                            --item-hover-background: rgba(255, 255, 255, 0.07); --table-row-divider: rgba(255, 255, 255, 0.08);
                            --legend-unsupported-tint: linear-gradient(rgba(239, 68, 68, 0.15), rgba(239, 68, 68, 0.15));
                            --legend-supported-tint: linear-gradient(rgba(234, 179, 8, 0.15), rgba(234, 179, 8, 0.15));
                            --legend-latest-tint: linear-gradient(rgba(34, 197, 94, 0.15), rgba(34, 197, 94, 0.15));
                            --legend-preview-tint: linear-gradient(rgba(249, 115, 22, 0.15), rgba(249, 115, 22, 0.15));
                            --legend-future-tint: linear-gradient(rgba(59, 130, 246, 0.15), rgba(59, 130, 246, 0.15));
                            --legend-na-tint: linear-gradient(rgba(255, 255, 255, 0.04), rgba(255, 255, 255, 0.04));
                        }
                    }

                    html, body { background-color: transparent !important; color: var(--text-primary); font-family: 'Segoe UI Variable', 'Segoe UI', sans-serif; margin: 0; padding: 0; font-size: 15px; -webkit-font-smoothing: antialiased; }
                    .mw-parser-output { padding: 30px 16px 30px 16px; }
                    a { color: var(--link-color); text-decoration: none; } a:hover { text-decoration: underline; }
                    a.selflink, a.new { color: var(--text-secondary); pointer-events: none; text-decoration: none; }
                    img { max-width: 100%; height: auto; border-radius: 4px; } .mw-editsection { display: none; }
                    h2 { border-bottom: 1px solid var(--card-border); padding-bottom: 8px; margin-top: 24px; }
                    .reflist { font-size: 90%; column-width: 30em; column-gap: 2em; margin-top: 1em; }
                    .reflist ol.references { margin: 0; padding-left: 1.6em; }
                    .reflist li { margin-bottom: 0.5em; page-break-inside: avoid; break-inside: avoid-column; }
                    .infobox { float: right; margin: 0 0 1em 1.5em; width: 22em; }
                    .hlist ul { padding: 0; margin: 0; list-style: none; } .hlist li { display: inline; white-space: nowrap; }
                    .hlist li:not(:first-child)::before { content: ' \00B7 '; font-weight: bold; } .hlist dl, .hlist ol, .hlist ul { display: inline; }
                    .infobox, table.wikitable, .navbox { background-color: var(--card-background) !important; border: 1px solid var(--card-border); border-radius: 8px; box-shadow: 0 4px 12px var(--card-shadow); border-collapse: separate; border-spacing: 0; margin-bottom: 16px; overflow: hidden; }
                    .infobox > tbody > tr > *, .wikitable > tbody > tr > * { vertical-align: middle; }
                    .infobox > tbody > tr > th, .infobox > tbody > tr > td, .wikitable > tbody > tr > th, .wikitable > tbody > tr > td { padding: 12px 16px; text-align: left; border: none; }
                    .infobox > tbody > tr:not(:last-child) > *, .wikitable > tbody > tr:not(:last-child) > * { border-bottom: 1px solid var(--table-row-divider); }
                    .infobox > tbody > tr > th, .wikitable > tbody > tr > th { font-weight: 600; color: var(--text-secondary); }
                    .wikitable .table-version-unsupported { background-image: var(--legend-unsupported-tint); }
                    .wikitable .table-version-supported { background-image: var(--legend-supported-tint); }
                    .wikitable .table-version-latest { background-image: var(--legend-latest-tint); }
                    .wikitable .table-version-preview { background-image: var(--legend-preview-tint); }
                    .wikitable .table-version-future { background-image: var(--legend-future-tint); }
                    .wikitable .table-na { background-image: var(--legend-na-tint); color: var(--text-secondary) !important; }
                    .version-legend-horizontal { padding: 8px 16px; font-size: 13px; color: var(--text-secondary); text-align: center; }
                    .version-legend-square { display: inline-block; width: 1em; height: 1em; margin-right: 0.5em; border: 1px solid var(--card-border); vertical-align: -0.1em; }
                    .version-legend-horizontal .version-unsupported.version-legend-square { background-image: var(--legend-unsupported-tint); }
                    .version-legend-horizontal .version-supported.version-legend-square { background-image: var(--legend-supported-tint); }
                    .version-legend-horizontal .version-latest.version-legend-square { background-image: var(--legend-latest-tint); }
                    .version-legend-horizontal .version-preview.version-legend-square { background-image: var(--legend-preview-tint); }
                    .version-legend-horizontal .version-future.version-legend-square { background-image: var(--legend-future-tint); }
                    .navbox-title, .navbox-group { background: var(--card-header-background); padding: 12px 16px; font-weight: 600; }
                    .navbox-title { border-bottom: 1px solid var(--card-border); font-size: 16px; }
                    .navbox-group { border-top: 1px solid var(--card-border); font-size: 12px; text-transform: uppercase; }
                    .navbox-title a, .navbox-title a:link, .navbox-title a:visited { color: var(--text-primary); text-decoration: none; }
                    .navbox-group a, .navbox-group a:link, .navbox-group a:visited { color: var(--text-secondary); text-decoration: none; }
                    .navbox-inner { padding: 8px; } .navbox-list li a { padding: 4px 6px; border-radius: 4px; transition: background-color 0.15s ease-in-out; }
                    .navbox-list li a:hover { background: var(--item-hover-background); text-decoration: none; }
                    .navbox-image { float: right; margin: 16px; }
                    </style>";
        }
    }
}