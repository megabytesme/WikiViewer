using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Threading;
using System.Threading.Tasks;
using HtmlAgilityPack;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using WikiViewer.Core.Interfaces;
using WikiViewer.Core.Managers;
using WikiViewer.Core.Models;

namespace WikiViewer.Core.Services
{
    public static class ArticleProcessingService
    {
        public static async Task<(
            string HtmlContent,
            string ResolvedTitle
        )> FetchAndProcessArticleAsync(
            string pageTitle,
            Stopwatch stopwatch,
            IApiWorker worker,
            WikiInstance wiki,
            bool forceRefresh = false,
            SemaphoreSlim semaphore = null
        )
        {
            if (worker == null)
                throw new ArgumentNullException(nameof(worker));
            if (wiki == null)
                throw new ArgumentNullException(nameof(wiki));

            string resolvedTitle = pageTitle;

            if (
                string.IsNullOrEmpty(pageTitle)
                || pageTitle.Equals("Special:Random", StringComparison.OrdinalIgnoreCase)
            )
            {
                resolvedTitle = await GetRandomPageTitleAsync(worker, wiki);
                if (string.IsNullOrEmpty(resolvedTitle))
                    throw new Exception("Failed to get a random title from API response.");
            }

            bool isConnected = NetworkInterface.GetIsNetworkAvailable();

            if (AppSettings.IsCachingEnabled && !forceRefresh)
            {
                var cachedMetadata = await ArticleCacheManager.GetCacheMetadataAsync(
                    resolvedTitle,
                    wiki.Id
                );
                if (cachedMetadata != null)
                {
                    DateTime? remoteTimestamp = isConnected
                        ? await FetchLastUpdatedTimestampAsync(resolvedTitle, worker, wiki)
                        : null;
                    if (
                        !isConnected
                        || (
                            remoteTimestamp.HasValue
                            && cachedMetadata.LastUpdated.ToUniversalTime()
                                >= remoteTimestamp.Value.ToUniversalTime()
                        )
                    )
                    {
                        string cachedHtml = await ArticleCacheManager.GetCachedArticleHtmlAsync(
                            resolvedTitle,
                            wiki.Id
                        );
                        if (!string.IsNullOrEmpty(cachedHtml))
                        {
                            return (cachedHtml, resolvedTitle);
                        }
                    }
                }
            }

            if (!isConnected)
            {
                throw new Exception("No network connection and this article has not been cached.");
            }

            try
            {
                string freshHtmlContent = await FetchParsedHtmlFromApiAsync(
                    resolvedTitle,
                    worker,
                    wiki
                );
                await ArticleCacheManager.SaveArticleHtmlAsync(
                    resolvedTitle,
                    wiki.Id,
                    freshHtmlContent,
                    DateTime.UtcNow
                );
                return (freshHtmlContent, resolvedTitle);
            }
            catch (Exception ex)
            {
                string finalFallbackHtml = await ArticleCacheManager.GetCachedArticleHtmlAsync(
                    resolvedTitle,
                    wiki.Id
                );
                if (!string.IsNullOrEmpty(finalFallbackHtml))
                {
                    return (finalFallbackHtml, resolvedTitle);
                }
                throw;
            }
        }

        private static async Task<string> FetchParsedHtmlFromApiAsync(
            string title,
            IApiWorker worker,
            WikiInstance wiki
        )
        {
            var url =
                $"{wiki.ApiEndpoint}?action=parse&page={Uri.EscapeDataString(title)}&prop=text&format=json&disableeditsection=true";
            var json = await worker.GetJsonFromApiAsync(url);
            if (string.IsNullOrEmpty(json))
                throw new Exception("API returned an empty response for parsed HTML.");
            var response = JObject.Parse(json);
            if (response["error"] != null)
                throw new Exception(
                    response["error"]["info"]?.ToString() ?? "Unknown API error during page parse."
                );
            var htmlContent = response["parse"]?["text"]?["*"]?.ToString();
            if (string.IsNullOrEmpty(htmlContent))
                throw new Exception("API response did not contain page HTML.");
            return htmlContent;
        }

        public static async Task<string> BuildArticleHtmlAsync(
            string articleContentHtml,
            string pageTitle,
            WikiInstance wiki
        )
        {
            string themeCss = await ThemeManager.GetThemeCssAsync();
            string decodedCss = WebUtility.HtmlDecode(themeCss);
            string contentWithAbsoluteUrls = WikitextParsingService.FixRelativeUrls(
                articleContentHtml,
                wiki
            );

            var finalDoc = new HtmlDocument();
            var htmlNode = finalDoc.CreateElement("html");
            finalDoc.DocumentNode.AppendChild(htmlNode);

            var headNode = finalDoc.CreateElement("head");
            headNode.InnerHtml =
                $@"
<meta charset='UTF-8'>
<title>{WebUtility.HtmlEncode(pageTitle)}</title>
<meta name='viewport' content='width=device-width, initial-scale=1.0'>
<base href='{wiki.BaseUrl}'>
<style id='custom-theme-style'>{decodedCss}</style>";
            htmlNode.AppendChild(headNode);

            var bodyNode = finalDoc.CreateElement("body");
            var wrapperDiv = finalDoc.CreateElement("div");
            wrapperDiv.SetAttributeValue("class", "mw-parser-output");
            wrapperDiv.InnerHtml = contentWithAbsoluteUrls;
            bodyNode.AppendChild(wrapperDiv);

            htmlNode.AppendChild(bodyNode);
            return finalDoc.DocumentNode.OuterHtml;
        }

        public static async Task<DateTime?> FetchLastUpdatedTimestampAsync(
            string pageTitle,
            IApiWorker worker,
            WikiInstance wiki
        )
        {
            if (string.IsNullOrEmpty(pageTitle))
                return null;
            string url =
                $"{wiki.ApiEndpoint}?action=query&prop=revisions&titles={Uri.EscapeDataString(pageTitle)}&rvprop=timestamp&rvlimit=1&format=json";
            try
            {
                string json = await worker.GetJsonFromApiAsync(url);
                if (string.IsNullOrEmpty(json))
                    return null;
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
                    $"[PROCESSOR-TIMESTAMP] Failed to get timestamp for '{pageTitle}': {ex.Message}"
                );
                return null;
            }
        }

        private static async Task<string> GetRandomPageTitleAsync(
            IApiWorker worker,
            WikiInstance wiki
        )
        {
            string randomTitleJson = await worker.GetJsonFromApiAsync(
                $"{wiki.ApiEndpoint}?action=query&list=random&rnnamespace=0&rnlimit=1&format=json"
            );
            if (string.IsNullOrEmpty(randomTitleJson))
                throw new Exception(
                    "Failed to retrieve a response for a random page from the API."
                );
            var randomResponse = JsonConvert.DeserializeObject<RandomQueryResponse>(
                randomTitleJson
            );
            return randomResponse?.query?.random?.FirstOrDefault()?.title;
        }

        public static async Task<bool> PageExistsAsync(
            string pageTitle,
            IApiWorker worker,
            WikiInstance wiki
        )
        {
            if (string.IsNullOrEmpty(pageTitle) || worker == null || wiki == null)
                return false;
            var escapedTitle = Uri.EscapeDataString(pageTitle);
            var url = $"{wiki.ApiEndpoint}?action=query&titles={escapedTitle}&format=json";
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
            catch (Exception ex)
            {
                Debug.WriteLine(
                    $"[PROCESSOR-EXISTS] PageExistsAsync check failed for '{pageTitle}': {ex.Message}"
                );
                throw new Exception($"Page check failed for '{pageTitle}'", ex);
            }
        }
    }
}
