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
using WikiViewer.Core.Interfaces;
using WikiViewer.Core.Managers;
using WikiViewer.Core.Models;

namespace WikiViewer.Core.Services
{
    public static class ArticleProcessingService
    {
        public static async Task<(string Html, string ResolvedTitle)> FetchAndProcessArticleAsync(
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
                        Debug.WriteLine(
                            $"[ArticleProcessing] Loading '{resolvedTitle}' from fresh cache."
                        );
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
                Debug.WriteLine(
                    $"[ArticleProcessing] Fetching fresh copy of '{resolvedTitle}' from API."
                );
                string freshHtml = await FetchParsedHtmlFromApiAsync(resolvedTitle, worker, wiki);
                await ArticleCacheManager.SaveArticleHtmlAsync(
                    resolvedTitle,
                    wiki.Id,
                    freshHtml,
                    DateTime.UtcNow
                );

                return (freshHtml, resolvedTitle);
            }
            catch (Exception ex)
            {
                Debug.WriteLine(
                    $"[ArticleProcessing] API fetch failed for '{resolvedTitle}', trying final cache fallback. Error: {ex.Message}"
                );
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

        public static async Task<string> ProcessHtmlAsync(
            string rawHtml,
            string pageTitle,
            IApiWorker worker,
            WikiInstance wiki,
            SemaphoreSlim semaphore = null
        )
        {
            var doc = new HtmlDocument();
            doc.LoadHtml(rawHtml);

            var contentNode = doc.DocumentNode;

            string styleBlock = GetCssForTheme();
            string headContent =
                $@"<head><meta charset='UTF-8'><title>{System.Net.WebUtility.HtmlEncode(pageTitle)}</title><meta name='viewport' content='width=device-width, initial-scale=1.0'><base href='{wiki.BaseUrl}' />{styleBlock}</head>";

            return $@"<!DOCTYPE html><html>{headContent}<body><div class='mw-parser-output'>{contentNode.InnerHtml}</div></body></html>";
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
            catch (JsonReaderException ex)
            {
                Debug.WriteLine(
                    $"[PROCESSOR-TIMESTAMP] Failed to parse timestamp JSON for '{pageTitle}'. Raw response was not valid JSON. Error: {ex.Message}"
                );
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

            try
            {
                var randomResponse = JsonConvert.DeserializeObject<RandomQueryResponse>(
                    randomTitleJson
                );
                return randomResponse?.query?.random?.FirstOrDefault()?.title;
            }
            catch (JsonReaderException ex)
            {
                Debug.WriteLine(
                    $"[ArticleProcessingService] FAILED to parse random page JSON. The API returned non-JSON content. Raw response was:\n-----\n{randomTitleJson}\n-----"
                );
                throw new Exception(
                    "The API did not return a valid response for a random article. A security check may be required.",
                    ex
                );
            }
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
            catch (JsonReaderException ex)
            {
                Debug.WriteLine(
                    $"[PROCESSOR-EXISTS] Failed to parse PageExists JSON for '{pageTitle}'. Raw response was not valid JSON."
                );
                throw new Exception(
                    $"Page check failed for '{pageTitle}' because the API returned an invalid response.",
                    ex
                );
            }
            catch (NeedsUserVerificationException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Debug.WriteLine(
                    $"[PROCESSOR-EXISTS] PageExistsAsync check failed for '{pageTitle}': {ex.Message}"
                );
                throw new Exception($"Page check failed for '{pageTitle}'", ex);
            }
        }

        public static string GetCssForTheme()
        {
            return @"<style>
:root { --text-primary: #000000; --text-secondary: #505050; --link-color: #0066CC; --card-shadow: rgba(0, 0, 0, 0.13); --card-background: rgba(249, 249, 249, 0.7); --card-border: rgba(0, 0, 0, 0.1); --card-header-background: rgba(0, 0, 0, 0.05); --item-hover-background: rgba(0, 0, 0, 0.05); --table-row-divider: rgba(0, 0, 0, 0.08); --legend-unsupported-tint: linear-gradient(rgba(239, 68, 68, 0.1), rgba(239, 68, 68, 0.1)); --legend-supported-tint: linear-gradient(rgba(234, 179, 8, 0.1), rgba(234, 179, 8, 0.1)); --legend-latest-tint: linear-gradient(rgba(34, 197, 94, 0.1), rgba(34, 197, 94, 0.1)); --legend-preview-tint: linear-gradient(rgba(249, 115, 22, 0.1), rgba(249, 115, 22, 0.1)); --legend-future-tint: linear-gradient(rgba(59, 130, 246, 0.1), rgba(59, 130, 246, 0.1)); --legend-na-tint: linear-gradient(rgba(0, 0, 0, 0.04), rgba(0, 0, 0, 0.04)); }
@media (prefers-color-scheme: dark) { :root { --text-primary: #FFFFFF; --text-secondary: #C3C3C3; --link-color: #85B9F3; --card-shadow: rgba(0, 0, 0, 0.4); --card-background: rgba(44, 44, 44, 0.7); --card-border: rgba(255, 255, 255, 0.1); --card-header-background: rgba(255, 255, 255, 0.08); --item-hover-background: rgba(255, 255, 255, 0.07); --table-row-divider: rgba(255, 255, 255, 0.08); --legend-unsupported-tint: linear-gradient(rgba(239, 68, 68, 0.15), rgba(239, 68, 68, 0.15)); --legend-supported-tint: linear-gradient(rgba(234, 179, 8, 0.15), rgba(234, 179, 8, 0.15)); --legend-latest-tint: linear-gradient(rgba(34, 197, 94, 0.15), rgba(34, 197, 94, 0.15)); --legend-preview-tint: linear-gradient(rgba(249, 115, 22, 0.15), rgba(249, 115, 22, 0.15)); --legend-future-tint: linear-gradient(rgba(59, 130, 246, 0.15), rgba(59, 130, 246, 0.15)); --legend-na-tint: linear-gradient(rgba(255, 255, 255, 0.04), rgba(255, 255, 255, 0.04)); } }
html.dark-theme { --text-primary: #FFFFFF; --text-secondary: #C3C3C3; --link-color: #85B9F3; --card-shadow: rgba(0, 0, 0, 0.4); --card-background: rgba(44, 44, 44, 0.7); --card-border: rgba(255, 255, 255, 0.1); --card-header-background: rgba(255, 255, 255, 0.08); --item-hover-background: rgba(255, 255, 255, 0.07); --table-row-divider: rgba(255, 255, 255, 0.08); --legend-unsupported-tint: linear-gradient(rgba(239, 68, 68, 0.15), rgba(239, 68, 68, 0.15)); --legend-supported-tint: linear-gradient(rgba(234, 179, 8, 0.15), rgba(234, 179, 8, 0.15)); --legend-latest-tint: linear-gradient(rgba(34, 197, 94, 0.15), rgba(34, 197, 94, 0.15)); --legend-preview-tint: linear-gradient(rgba(249, 115, 22, 0.15), rgba(249, 115, 22, 0.15)); --legend-future-tint: linear-gradient(rgba(59, 130, 246, 0.15), rgba(59, 130, 246, 0.15)); --legend-na-tint: linear-gradient(rgba(255, 255, 255, 0.04), rgba(255, 255, 255, 0.04)); }
html, body { background-color: transparent !important; color: var(--text-primary); font-family: 'Segoe UI Variable', 'Segoe UI', sans-serif; margin: 0; padding: 0; font-size: 15px; -webkit-font-smoothing: antialiased; } .mw-parser-output { padding: 30px 16px 30px 16px; } a { color: var(--link-color); text-decoration: none; } a:hover { text-decoration: underline; } a.selflink, a.new { color: var(--text-secondary); pointer-events: none; text-decoration: none; } img { max-width: 100%; height: auto; border-radius: 4px; } .mw-editsection { display: none; } h2 { border-bottom: 1px solid var(--card-border); padding-bottom: 8px; margin-top: 24px; } .reflist { font-size: 90%; column-width: 30em; column-gap: 2em; margin-top: 1em; } .reflist ol.references { margin: 0; padding-left: 1.6em; } .reflist li { margin-bottom: 0.5em; page-break-inside: avoid; break-inside: avoid-column; } .infobox { float: right; margin: 0 0 1em 1.5em; width: 22em; } .hlist ul { padding: 0; margin: 0; list-style: none; } .hlist li { display: inline; white-space: nowrap; } .hlist li:not(:first-child)::before { content: ' \00B7 '; font-weight: bold; } .hlist dl, .hlist ol, .hlist ul { display: inline; } .infobox, table.wikitable, .navbox { background-color: var(--card-background) !important; border: 1px solid var(--card-border); border-radius: 8px; box-shadow: 0 4px 12px var(--card-shadow); border-collapse: separate; border-spacing: 0; margin-bottom: 16px; overflow: hidden; } .infobox > tbody > tr > *, .wikitable > tbody > tr > * { vertical-align: middle; } .infobox > tbody > tr > th, .infobox > tbody > tr > td, .wikitable > tbody > tr > th, .wikitable > tbody > tr > td { padding: 12px 16px; text-align: left; border: none; } .infobox > tbody > tr:not(:last-child) > *, .wikitable > tbody > tr:not(:last-child) > * { border-bottom: 1px solid var(--table-row-divider); } .infobox > tbody > tr > th, .wikitable > tbody > tr > th { font-weight: 600; color: var(--text-secondary); } .wikitable .table-version-unsupported { background-image: var(--legend-unsupported-tint); } .wikitable .table-version-supported { background-image: var(--legend-supported-tint); } .wikitable .table-version-latest { background-image: var(--legend-latest-tint); } .wikitable .table-version-preview { background-image: var(--legend-preview-tint); } .wikitable .table-version-future { background-image: var(--legend-future-tint); } .wikitable .table-na { background-image: var(--legend-na-tint); color: var(--text-secondary) !important; } .version-legend-horizontal { padding: 8px 16px; font-size: 13px; color: var(--text-secondary); text-align: center; } .version-legend-square { display: inline-block; width: 1em; height: 1em; margin-right: 0.5em; border: 1px solid var(--card-border); vertical-align: -0.1em; } .version-legend-horizontal .version-unsupported.version-legend-square { background-image: var(--legend-unsupported-tint); } .version-legend-horizontal .version-supported.version-legend-square { background-image: var(--legend-supported-tint); } .version-legend-horizontal .version-latest.version-legend-square { background-image: var(--legend-latest-tint); } .version-legend-horizontal .version-preview.version-legend-square { background-image: var(--legend-preview-tint); } .version-legend-horizontal .version-future.version-legend-square { background-image: var(--legend-future-tint); } .navbox-title, .navbox-group { background: var(--card-header-background); padding: 12px 16px; font-weight: 600; } .navbox-title { border-bottom: 1px solid var(--card-border); font-size: 16px; } .navbox-group { border-top: 1px solid var(--card-border); font-size: 12px; text-transform: uppercase; } .navbox-title a, .navbox-title a:link, .navbox-title a:visited { color: var(--text-primary); text-decoration: none; } .navbox-group a, .navbox-group a:link, .navbox-group a:visited { color: var(--text-secondary); text-decoration: none; } .navbox-inner { padding: 8px; } .navbox-list li a { padding: 4px 6px; border-radius: 4px; transition: background-color 0.15s ease-in-out; } .navbox-list li a:hover { background: var(--item-hover-background); text-decoration: none; } .navbox-image { float: right; margin: 16px; }
</style>";
        }
    }
}
