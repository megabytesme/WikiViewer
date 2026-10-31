using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using System.Threading;
using HtmlAgilityPack;
using WikiViewer.Core.Models;

namespace WikiViewer.Core.Services
{
    public static class ArticleStyleResourceService
    {
        // WebView returns a document wrapper when navigating to text/css resources.
        public static string NormalizeResponse(string response)
        {
            if (string.IsNullOrWhiteSpace(response)) return null;
            if (!response.TrimStart().StartsWith("<", StringComparison.Ordinal)) return response;
            var document = new HtmlDocument(); document.LoadHtml(response);
            var pre = document.DocumentNode.SelectNodes("//pre");
            return pre?.Count == 1 ? WebUtility.HtmlDecode(pre[0].InnerText) : null;
        }

        public static List<Uri> GetStyleUrls(ArticleContent content, Uri pageUri, string scriptPath)
        {
            var document = new HtmlDocument(); document.LoadHtml(content.HeadHtml ?? "");
            var result = new List<Uri>();
            foreach (var link in document.DocumentNode.SelectNodes("//link[@href]") ?? Enumerable.Empty<HtmlNode>())
            {
                if (!link.GetAttributeValue("rel", "").Split(' ').Contains("stylesheet")) continue;
                Uri uri;
                if (!Uri.TryCreate(pageUri, WebUtility.HtmlDecode(link.GetAttributeValue("href", "")), out uri) || uri.Scheme != pageUri.Scheme ||
                    uri.Host != pageUri.Host || uri.Port != pageUri.Port || !(uri.AbsolutePath.EndsWith("/load.php", StringComparison.OrdinalIgnoreCase) || uri.AbsolutePath.EndsWith(".css", StringComparison.OrdinalIgnoreCase))) continue;
                if (!result.Contains(uri)) result.Add(uri);
            }
            if (content.StyleModules != null && content.StyleModules.Count > 0)
            {
                string path = (scriptPath ?? "").Trim('/');
                var uri = new Uri(pageUri, "/" + (path.Length > 0 ? path + "/" : "") +
                    "load.php?modules=" + Uri.EscapeDataString(string.Join("|", content.StyleModules.Take(100))) + "&only=styles");
                if (!result.Contains(uri)) result.Add(uri);
            }
            return result.Take(6).ToList();
        }

        public static async Task ResolveAsync(ArticleContent content, Uri pageUri, string scriptPath, Func<string, Task<string>> fetch)
        {
            using (var concurrency = new SemaphoreSlim(3))
            {
                var tasks = GetStyleUrls(content, pageUri, scriptPath).Select(async uri =>
                {
                    await concurrency.WaitAsync();
                    try { return WikiResourceCache.Shared != null ? await WikiResourceCache.Shared.GetAsync(uri.AbsoluteUri, async () => NormalizeResponse(await fetch(uri.AbsoluteUri))) : NormalizeResponse(await fetch(uri.AbsoluteUri)); }
                    catch (Exception) { return null; }
                    finally { concurrency.Release(); }
                });
                // Task.WhenAll retains source order, which matters for the CSS cascade.
                var sheets = Bound(await Task.WhenAll(tasks));
                if (sheets.Count >= (content.ResolvedStyleSheets?.Count ?? 0)) content.ResolvedStyleSheets = sheets;
            }
        }

        public static async Task ResolveCachedAsync(ArticleContent content, Uri pageUri, string scriptPath)
        {
            if (WikiResourceCache.Shared == null) return;
            var sheets = await Task.WhenAll(GetStyleUrls(content, pageUri, scriptPath).Select(uri => WikiResourceCache.Shared.TryReadAsync(uri.AbsoluteUri)));
            var cached = Bound(sheets);
            if (cached.Count >= (content.ResolvedStyleSheets?.Count ?? 0)) content.ResolvedStyleSheets = cached;
        }
        private static List<string> Bound(IEnumerable<string> sheets)
        {
            var result = new List<string>(); int size = 0;
            foreach (string css in sheets)
            { if (css != null && css.Length <= 1000000 && size + css.Length <= 2000000) { result.Add(css); size += css.Length; } }
            return result;
        }
    }
}
