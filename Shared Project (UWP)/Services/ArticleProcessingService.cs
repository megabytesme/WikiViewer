using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Threading;
using System.Threading.Tasks;
using HtmlAgilityPack;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Windows.Storage;

namespace Shared_Code
{
    public static class ArticleProcessingService
    {
        public static async Task<(string Html, string ResolvedTitle)> FetchAndCacheArticleAsync(
            string pageTitle,
            Stopwatch stopwatch,
            IApiWorker worker,
            bool forceRefresh = false,
            SemaphoreSlim semaphore = null
        )
        {
            if (worker == null)
                throw new ArgumentNullException(nameof(worker));

            string resolvedTitle = pageTitle;
            if (string.IsNullOrEmpty(pageTitle))
                throw new ArgumentNullException(nameof(pageTitle));
            if (pageTitle.Equals("random", StringComparison.OrdinalIgnoreCase))
            {
                string randomTitleJson = await worker.GetJsonFromApiAsync(
                    $"{AppSettings.ApiEndpoint}?action=query&list=random&rnnamespace=0&rnlimit=1&format=json"
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
                    ? await FetchLastUpdatedTimestampAsync(resolvedTitle, worker)
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
                        return (cachedHtml, resolvedTitle);
                }
            }
            if (!isConnected)
                throw new Exception(
                    "No network connection and a fresh copy of the article is needed."
                );
            string pageUrl = AppSettings.GetWikiPageUrl(resolvedTitle);
            var freshHtml = await worker.GetRawHtmlFromUrlAsync(pageUrl);
            var processedHtml = await ProcessHtmlAsync(freshHtml, worker, semaphore);
            var lastUpdated = await FetchLastUpdatedTimestampAsync(resolvedTitle, worker);
            if (AppSettings.IsCachingEnabled && lastUpdated.HasValue)
            {
                await ArticleCacheManager.SaveArticleToCacheAsync(
                    resolvedTitle,
                    processedHtml,
                    lastUpdated.Value
                );
            }
            return (processedHtml, resolvedTitle);
        }

        public static async Task<string> ProcessHtmlAsync(
            string rawHtml,
            IApiWorker worker,
            SemaphoreSlim semaphore = null
        )
        {
            var doc = new HtmlDocument();
            doc.LoadHtml(rawHtml);
            var contentNode =
                doc.DocumentNode.SelectSingleNode("//div[@id='mw-content-text']")
                ?? doc.DocumentNode;
            await ProcessMediaInDocument(doc, worker, semaphore);
            string styleBlock = GetCssForTheme();
            return $@"<!DOCTYPE html><html><head><meta charset='UTF-8'><meta name='viewport' content='width=device-width, initial-scale=1.0'>{styleBlock}</head><body><div class='mw-parser-output'>{contentNode.InnerHtml}</div></body></html>";
        }

        private static async Task ProcessMediaInDocument(
            HtmlDocument doc,
            IApiWorker worker,
            SemaphoreSlim semaphore = null
        )
        {
            string fileLinkPrefix = $"/{AppSettings.ArticlePath}File:";
            var imageLinks = doc.DocumentNode.SelectNodes(
                $"//a[starts-with(@href, '{fileLinkPrefix}')]"
            );
            if (imageLinks != null && imageLinks.Any())
            {
                var imageFileNames = imageLinks
                    .Select(link =>
                        link.GetAttributeValue("href", "").Substring(fileLinkPrefix.Length - 1)
                    )
                    .Distinct()
                    .ToList();
                var titles = string.Join("|", imageFileNames.Select(Uri.EscapeDataString));
                var imageUrlApi =
                    $"{AppSettings.ApiEndpoint}?action=query&prop=imageinfo&iiprop=url&format=json&titles={titles}";
                try
                {
                    string imageJsonResponse = await worker.GetJsonFromApiAsync(imageUrlApi);
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
                                            link.GetAttributeValue("href", "")
                                                .Substring(fileLinkPrefix.Length - 1)
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
            var semaphoreToUse = semaphore ?? new SemaphoreSlim(AppSettings.MaxConcurrentDownloads);
            var mediaNodes = doc.DocumentNode.SelectNodes(
                "//img | //audio/source | //video/source"
            );
            if (mediaNodes != null && mediaNodes.Any())
            {
                var uniqueMediaUrls = mediaNodes
                    .Select(node =>
                        node.GetAttributeValue("srcset", null)
                            ?.Split(',')
                            .FirstOrDefault()
                            ?.Trim()
                            .Split(' ')[0] ?? node.GetAttributeValue("src", null)
                    )
                    .Where(src => !string.IsNullOrEmpty(src) && !src.StartsWith("data:"))
                    .Distinct()
                    .ToList();
                var downloadTasks = uniqueMediaUrls
                    .Select(async originalUrl =>
                    {
                        await semaphoreToUse.WaitAsync();
                        try
                        {
                            return new
                            {
                                OriginalUrl = originalUrl,
                                LocalPath = await DownloadAndCacheMediaAsync(originalUrl, worker),
                            };
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
                foreach (var node in mediaNodes)
                {
                    string originalSrc =
                        node.GetAttributeValue("srcset", null)
                            ?.Split(',')
                            .FirstOrDefault()
                            ?.Trim()
                            .Split(' ')[0] ?? node.GetAttributeValue("src", null);
                    if (urlToLocalPathMap.TryGetValue(originalSrc, out string localPath))
                    {
                        node.SetAttributeValue("src", localPath);
                        node.Attributes.Remove("srcset");
                    }
                }
            }
        }

        private static async Task<string> DownloadAndCacheMediaAsync(
            string originalUrl,
            IApiWorker worker
        )
        {
            if (!Uri.TryCreate(new Uri(AppSettings.BaseUrl), originalUrl, out Uri mediaUrl))
                return null;
            var extension = Path.GetExtension(mediaUrl.AbsolutePath).ToLowerInvariant();
            if (string.IsNullOrEmpty(extension))
                extension = ".dat";
            var hash = System
                .Security.Cryptography.SHA1.Create()
                .ComputeHash(System.Text.Encoding.UTF8.GetBytes(mediaUrl.AbsoluteUri));
            var baseFileName = hash.Aggregate("", (s, b) => s + b.ToString("x2"));
            var finalFileName = baseFileName + extension;
            var cacheFolder = await ApplicationData.Current.LocalFolder.CreateFolderAsync(
                "cache",
                CreationCollisionOption.OpenIfExists
            );
            var relativePath = $"/cache/{finalFileName}";
            if (await cacheFolder.TryGetItemAsync(finalFileName) is StorageFile)
                return relativePath;
            byte[] mediaBytes = null;
            try
            {
                mediaBytes = await worker.GetRawBytesFromUrlAsync(mediaUrl.AbsoluteUri);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MediaDownloader] Download FAILED for {mediaUrl}: {ex.Message}");
                return null;
            }
            if (mediaBytes == null || mediaBytes.Length == 0)
                return null;
            try
            {
                StorageFile newFile = await cacheFolder.CreateFileAsync(
                    finalFileName,
                    CreationCollisionOption.ReplaceExisting
                );
                await FileIO.WriteBytesAsync(newFile, mediaBytes);
                return relativePath;
            }
            catch (Exception ex)
            {
                Debug.WriteLine(
                    $"[MediaDownloader] FAILED to save file {finalFileName}: {ex.Message}"
                );
                return null;
            }
        }

        public static async Task<DateTime?> FetchLastUpdatedTimestampAsync(
            string pageTitle,
            IApiWorker worker
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
                string json = await worker.GetJsonFromApiAsync(url);
                JObject root = JObject.Parse(json);
                JToken pagesToken = root?["query"]?["pages"];
                if (pagesToken != null && pagesToken.Type == JTokenType.Object)
                {
                    var pages = pagesToken.ToObject<Dictionary<string, TimestampPage>>();
                    return pages?.Values.FirstOrDefault()?.Revisions?.FirstOrDefault()?.Timestamp;
                }
                return null;
            }
            catch (Exception ex)
            {
                Debug.WriteLine(
                    $"[PROCESSOR] Failed to get timestamp for '{pageTitle}': {ex.Message}"
                );
                return null;
            }
        }

        public static async Task<bool> PageExistsAsync(string pageTitle, IApiWorker worker)
        {
            if (string.IsNullOrEmpty(pageTitle) || worker == null)
                return false;
            var escapedTitle = Uri.EscapeDataString(pageTitle);
            var url = $"{AppSettings.ApiEndpoint}?action=query&titles={escapedTitle}&format=json";
            try
            {
                string json = await worker.GetJsonFromApiAsync(url);
                if (string.IsNullOrEmpty(json))
                    return false;
                JObject root = JObject.Parse(json);
                var pages = root?["query"]?["pages"];
                if (pages == null)
                    return false;
                var firstPage = pages.First as JProperty;
                if (firstPage?.Value?["missing"] != null || firstPage?.Name == "-1")
                    return false;
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

        public static string GetCssForTheme()
        {
            return @"<style>
:root { --text-primary: #000000; --text-secondary: #505050; --link-color: #0066CC; --card-shadow: rgba(0, 0, 0, 0.13); --card-background: rgba(249, 249, 249, 0.7); --card-border: rgba(0, 0, 0, 0.1); --card-header-background: rgba(0, 0, 0, 0.05); --item-hover-background: rgba(0, 0, 0, 0.05); --table-row-divider: rgba(0, 0, 0, 0.08); --legend-unsupported-tint: linear-gradient(rgba(239, 68, 68, 0.1), rgba(239, 68, 68, 0.1)); --legend-supported-tint: linear-gradient(rgba(234, 179, 8, 0.1), rgba(234, 179, 8, 0.1)); --legend-latest-tint: linear-gradient(rgba(34, 197, 94, 0.1), rgba(34, 197, 94, 0.1)); --legend-preview-tint: linear-gradient(rgba(249, 115, 22, 0.1), rgba(249, 115, 22, 0.1)); --legend-future-tint: linear-gradient(rgba(59, 130, 246, 0.1), rgba(59, 130, 246, 0.1)); --legend-na-tint: linear-gradient(rgba(0, 0, 0, 0.04), rgba(0, 0, 0, 0.04)); }
@media (prefers-color-scheme: dark) { :root { --text-primary: #FFFFFF; --text-secondary: #C3C3C3; --link-color: #85B9F3; --card-shadow: rgba(0, 0, 0, 0.4); --card-background: rgba(44, 44, 44, 0.7); --card-border: rgba(255, 255, 255, 0.1); --card-header-background: rgba(255, 255, 255, 0.08); --item-hover-background: rgba(255, 255, 255, 0.07); --table-row-divider: rgba(255, 255, 255, 0.08); --legend-unsupported-tint: linear-gradient(rgba(239, 68, 68, 0.15), rgba(239, 68, 68, 0.15)); --legend-supported-tint: linear-gradient(rgba(234, 179, 8, 0.15), rgba(234, 179, 8, 0.15)); --legend-latest-tint: linear-gradient(rgba(34, 197, 94, 0.15), rgba(34, 197, 94, 0.15)); --legend-preview-tint: linear-gradient(rgba(249, 115, 22, 0.15), rgba(249, 115, 22, 0.15)); --legend-future-tint: linear-gradient(rgba(59, 130, 246, 0.15), rgba(59, 130, 246, 0.15)); --legend-na-tint: linear-gradient(rgba(255, 255, 255, 0.04), rgba(255, 255, 255, 0.04)); } }
html, body { background-color: transparent !important; color: var(--text-primary); font-family: 'Segoe UI Variable', 'Segoe UI', sans-serif; margin: 0; padding: 0; font-size: 15px; -webkit-font-smoothing: antialiased; } .mw-parser-output { padding: 30px 16px 30px 16px; } a { color: var(--link-color); text-decoration: none; } a:hover { text-decoration: underline; } a.selflink, a.new { color: var(--text-secondary); pointer-events: none; text-decoration: none; } img { max-width: 100%; height: auto; border-radius: 4px; } .mw-editsection { display: none; } h2 { border-bottom: 1px solid var(--card-border); padding-bottom: 8px; margin-top: 24px; } .reflist { font-size: 90%; column-width: 30em; column-gap: 2em; margin-top: 1em; } .reflist ol.references { margin: 0; padding-left: 1.6em; } .reflist li { margin-bottom: 0.5em; page-break-inside: avoid; break-inside: avoid-column; } .infobox { float: right; margin: 0 0 1em 1.5em; width: 22em; } .hlist ul { padding: 0; margin: 0; list-style: none; } .hlist li { display: inline; white-space: nowrap; } .hlist li:not(:first-child)::before { content: ' \00B7 '; font-weight: bold; } .hlist dl, .hlist ol, .hlist ul { display: inline; } .infobox, table.wikitable, .navbox { background-color: var(--card-background) !important; border: 1px solid var(--card-border); border-radius: 8px; box-shadow: 0 4px 12px var(--card-shadow); border-collapse: separate; border-spacing: 0; margin-bottom: 16px; overflow: hidden; } .infobox > tbody > tr > *, .wikitable > tbody > tr > * { vertical-align: middle; } .infobox > tbody > tr > th, .infobox > tbody > tr > td, .wikitable > tbody > tr > th, .wikitable > tbody > tr > td { padding: 12px 16px; text-align: left; border: none; } .infobox > tbody > tr:not(:last-child) > *, .wikitable > tbody > tr:not(:last-child) > * { border-bottom: 1px solid var(--table-row-divider); } .infobox > tbody > tr > th, .wikitable > tbody > tr > th { font-weight: 600; color: var(--text-secondary); } .wikitable .table-version-unsupported { background-image: var(--legend-unsupported-tint); } .wikitable .table-version-supported { background-image: var(--legend-supported-tint); } .wikitable .table-version-latest { background-image: var(--legend-latest-tint); } .wikitable .table-version-preview { background-image: var(--legend-preview-tint); } .wikitable .table-version-future { background-image: var(--legend-future-tint); } .wikitable .table-na { background-image: var(--legend-na-tint); color: var(--text-secondary) !important; } .version-legend-horizontal { padding: 8px 16px; font-size: 13px; color: var(--text-secondary); text-align: center; } .version-legend-square { display: inline-block; width: 1em; height: 1em; margin-right: 0.5em; border: 1px solid var(--card-border); vertical-align: -0.1em; } .version-legend-horizontal .version-unsupported.version-legend-square { background-image: var(--legend-unsupported-tint); } .version-legend-horizontal .version-supported.version-legend-square { background-image: var(--legend-supported-tint); } .version-legend-horizontal .version-latest.version-legend-square { background-image: var(--legend-latest-tint); } .version-legend-horizontal .version-preview.version-legend-square { background-image: var(--legend-preview-tint); } .version-legend-horizontal .version-future.version-legend-square { background-image: var(--legend-future-tint); } .navbox-title, .navbox-group { background: var(--card-header-background); padding: 12px 16px; font-weight: 600; } .navbox-title { border-bottom: 1px solid var(--card-border); font-size: 16px; } .navbox-group { border-top: 1px solid var(--card-border); font-size: 12px; text-transform: uppercase; } .navbox-title a, .navbox-title a:link, .navbox-title a:visited { color: var(--text-primary); text-decoration: none; } .navbox-group a, .navbox-group a:link, .navbox-group a:visited { color: var(--text-secondary); text-decoration: none; } .navbox-inner { padding: 8px; } .navbox-list li a { padding: 4px 6px; border-radius: 4px; transition: background-color 0.15s ease-in-out; } .navbox-list li a:hover { background: var(--item-hover-background); text-decoration: none; } .navbox-image { float: right; margin: 16px; }
</style>";
        }
    }
}
