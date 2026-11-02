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
        public static IApiWorkerFactory ApiWorkerFactory { get; set; }
        private static readonly ArticleLoadCoordinator Loads = new ArticleLoadCoordinator();
        private static readonly AsyncRequestCoordinator<ArticleContent> Validations = new AsyncRequestCoordinator<ArticleContent>();
        private static readonly AsyncRequestCoordinator<ArticleContent> Enrichments = new AsyncRequestCoordinator<ArticleContent>();

        public static Task<ArticleContent> LoadAsync(string title, WikiInstance wiki, bool forceRefresh = false)
            => Loads.LoadAsync(CacheKeyService.Article(wiki.Id, title),
                () => AppSettings.IsCachingEnabled ? ArticleCacheManager.GetCachedArticleContentAsync(title, wiki.Id) : Task.FromResult<ArticleContent>(null),
                async () =>
                {
                    using (var worker = await ApiWorkerFactory.CreateApiWorkerAsync(wiki))
                    {
                        await worker.InitializeAsync(wiki.BaseUrl);
                        return await FetchArticleContentAsync(title, Stopwatch.StartNew(), worker, wiki, true);
                    }
                }, forceRefresh);

        public static Task<ArticleContent> RevalidateAsync(string title, WikiInstance wiki, ArticleContent cached)
            => Validations.RunAsync(CacheKeyService.Article(wiki.Id, title), async () =>
            {
                var metadata = await ArticleCacheManager.GetCacheMetadataAsync(title, wiki.Id);
                if (!ArticleLoadCoordinator.NeedsValidation(metadata, DateTime.UtcNow) && cached.HasRawSource && cached.NativeFormatVersion >= ArticleContent.CurrentNativeFormatVersion) return cached;
                if (!NetworkInterface.GetIsNetworkAvailable()) return cached;
                using (var worker = await ApiWorkerFactory.CreateApiWorkerAsync(wiki))
                {
                    await worker.InitializeAsync(wiki.BaseUrl);
                    string url = wiki.ApiEndpoint + "?action=query&prop=revisions&titles=" + Uri.EscapeDataString(title) + "&rvprop=ids%7Ctimestamp&rvlimit=1&format=json";
                    var root = JObject.Parse(await worker.GetJsonFromApiAsync(url));
                    var page = (root["query"]?["pages"] as JObject)?.Properties().FirstOrDefault()?.Value;
                    var revision = page?["revisions"]?.FirstOrDefault();
                    long? id = (long?)revision?["revid"];
                    if (!id.HasValue) throw new InvalidOperationException("The wiki could not confirm the saved page revision.");
                    if (ArticleLoadCoordinator.NeedsExpansion(metadata, cached, id.Value, DateTime.UtcNow)) return await LoadAsync(title, wiki, true);
                    await ArticleCacheManager.MarkCheckedAsync(title, wiki.Id, DateTime.UtcNow);
                    var checkedContent = cached.Copy(); checkedContent.SourceModifiedUtc = (DateTime?)revision?["timestamp"]; return checkedContent;
                }
            });

        public static Task<ArticleContent> EnrichAsync(string title, WikiInstance wiki, ArticleContent source)
            => Enrichments.RunAsync(CacheKeyService.Article(wiki.Id, title) + ":" + source.RevisionId + ":" + CacheKeyService.Hash(source.HtmlContent), async () =>
            {
                if (source.ResourcesResolved || source.ResourcesCheckedUtc.HasValue && DateTime.UtcNow - source.ResourcesCheckedUtc.Value < TimeSpan.FromMinutes(5)) return source;
                var result = source.Copy();
                var styleUrls = ArticleStyleResourceService.GetStyleUrls(result, new Uri(wiki.GetWikiPageUrl(title)), wiki.ScriptPath);
                if (styleUrls.Count == source.ResolvedStyleSheets.Count && WikiResourceCache.Shared != null)
                {
                    var metadata = await ArticleCacheManager.GetCacheMetadataAsync(title, wiki.Id);
                    for (int i = 0; i < styleUrls.Count; i++) WikiResourceCache.Shared.Seed(styleUrls[i].AbsoluteUri, source.ResolvedStyleSheets[i], metadata?.LastUpdated.ToUniversalTime() ?? DateTime.UtcNow.AddDays(-2));
                }
                if (!NetworkInterface.GetIsNetworkAvailable()) return result;
                var worker = new Lazy<Task<IApiWorker>>(async () =>
                {
                    var value = await ApiWorkerFactory.CreateApiWorkerAsync(wiki);
                    try { await value.InitializeAsync(wiki.BaseUrl); return value; }
                    catch { value.Dispose(); throw; }
                }, true);
                try
                {
                    var syntax = WikiSyntaxService.GetAsync(wiki.ApiEndpoint, async url => await (await worker.Value).GetJsonFromApiAsync(url));
                    var styles = ArticleStyleResourceService.ResolveAsync(result, new Uri(wiki.GetWikiPageUrl(title)), wiki.ScriptPath,
                        async url => await (await worker.Value).GetRawHtmlFromUrlAsync(url));
                    await Task.WhenAll(syntax, styles); var resolvedSyntax = await syntax; result.Syntax = resolvedSyntax.Language != null ? resolvedSyntax : source.Syntax ?? resolvedSyntax; result.ResourcesCheckedUtc = DateTime.UtcNow;
                    result.ResourcesResolved = result.Syntax?.Language != null && result.ResolvedStyleSheets.Count == ArticleStyleResourceService.GetStyleUrls(result, new Uri(wiki.GetWikiPageUrl(title)), wiki.ScriptPath).Count;
                    await ArticleCacheManager.UpdateResourcesIfCurrentAsync(title, wiki.Id, source, result);
                    return result;
                }
                finally { if (worker.IsValueCreated && worker.Value.Status == TaskStatus.RanToCompletion) worker.Value.Result.Dispose(); }
            });
        public static async Task<(string HtmlContent, string ResolvedTitle)> FetchAndProcessArticleAsync(
            string pageTitle, Stopwatch stopwatch, IApiWorker worker, WikiInstance wiki,
            bool forceRefresh = false, SemaphoreSlim semaphore = null)
        {
            var content = await FetchArticleContentAsync(pageTitle, stopwatch, worker, wiki, forceRefresh, semaphore);
            return (content.HtmlContent, content.Title);
        }

        public static async Task<ArticleContent> FetchArticleContentAsync(
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
                var cached = await ArticleCacheManager.GetCachedArticleContentAsync(resolvedTitle, wiki.Id);
                if (cached?.HtmlContent != null) return cached;
            }

            if (!isConnected)
            {
                throw new Exception("No network connection and this article has not been cached.");
            }

            try
            {
                string json = await worker.GetJsonFromApiAsync(ArticleContentResponse.BuildUrl(wiki.ApiEndpoint, resolvedTitle));
                var content = ArticleContentResponse.Parse(json, resolvedTitle);
                var prior = await ArticleCacheManager.GetCachedArticleContentAsync(resolvedTitle, wiki.Id);
                content.Syntax = await WikiSyntaxService.GetCachedAsync(wiki.ApiEndpoint) ?? prior?.Syntax;
                var pageUri = new Uri(wiki.GetWikiPageUrl(resolvedTitle));
                if (prior != null && ArticleStyleResourceService.GetStyleUrls(prior, pageUri, wiki.ScriptPath).SequenceEqual(ArticleStyleResourceService.GetStyleUrls(content, pageUri, wiki.ScriptPath)))
                    content.ResolvedStyleSheets = new List<string>(prior.ResolvedStyleSheets);
                await ArticleStyleResourceService.ResolveCachedAsync(content, new Uri(wiki.GetWikiPageUrl(resolvedTitle)), wiki.ScriptPath);
                await ArticleCacheManager.SaveArticleContentAsync(
                    resolvedTitle,
                    wiki.Id,
                    content,
                    DateTime.UtcNow
                );
                return content;
            }
            catch (Exception ex)
            {
                var fallback = await ArticleCacheManager.GetCachedArticleContentAsync(
                    resolvedTitle,
                    wiki.Id
                );
                if (fallback?.HtmlContent != null)
                {
                    return fallback;
                }
                throw;
            }
        }

        public static async Task<string> BuildArticleHtmlAsync(
            string articleContentHtml,
            string pageTitle,
            WikiInstance wiki
        )
        {
            string themeCss = await ArticlePresentation.GetThemeCssAsync();
            string decodedCss = WebUtility.HtmlDecode(themeCss);
            string contentWithAbsoluteUrls = WikitextParsingService.FixRelativeUrls(
                articleContentHtml,
                wiki
            );

            var finalDoc = new HtmlDocument();
            var contentDoc = new HtmlDocument();
            contentDoc.LoadHtml(contentWithAbsoluteUrls);

            var lazyImages = contentDoc.DocumentNode.SelectNodes(
                "//img[contains(@class, 'lazyload') and @data-src]"
            );
            if (lazyImages != null)
            {
                foreach (var imgNode in lazyImages)
                {
                    var realSrc = imgNode.GetAttributeValue("data-src", null);
                    if (!string.IsNullOrEmpty(realSrc))
                    {
                        imgNode.SetAttributeValue("src", realSrc);

                        imgNode.Attributes.Remove("data-src");
                        string currentClass = imgNode.GetAttributeValue("class", "");
                        imgNode.SetAttributeValue(
                            "class",
                            currentClass.Replace("lazyload", "").Trim()
                        );
                    }
                }
            }
            string processedContent = contentDoc.DocumentNode.OuterHtml;

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
            wrapperDiv.InnerHtml = processedContent;
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
