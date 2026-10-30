using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using HtmlAgilityPack;
using WikiViewer.Core.Models;

namespace WikiViewer.Core.Services
{
    public static class ArticleStyleResourceService
    {
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
            content.ResolvedStyleSheets = new List<string>();
            int size = 0;
            foreach (var uri in GetStyleUrls(content, pageUri, scriptPath))
            {
                try
                {
                    string css = await fetch(uri.AbsoluteUri);
                    if (css == null || css.Length > 1000000 || size + css.Length > 2000000) continue;
                    size += css.Length; content.ResolvedStyleSheets.Add(css);
                }
                catch (Exception) { /* A stylesheet failure must not discard usable article content. */ }
            }
        }
    }
}
