using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using WikiViewer.Core.Models;

namespace WikiViewer.Core.Services
{
    /// <summary>Retain a few parsed models, never controls; each reader gets its own mutable layout nodes.</summary>
    public static class NativeDocumentCache
    {
        private sealed class Entry { public string Key; public NativeArticleDocument Document; public int Nodes; }
        private static readonly object Gate = new object();
        private static readonly LinkedList<Entry> Entries = new LinkedList<Entry>();
        private static readonly AsyncRequestCoordinator<NativeArticleDocument> Requests = new AsyncRequestCoordinator<NativeArticleDocument>();
        private static int _nodes;
        public static void Clear() { lock (Gate) { Entries.Clear(); _nodes = 0; } }

        public static async Task<NativeArticleDocument> GetAsync(ArticleContent content, Uri page, double? width)
        {
            var elapsed = Stopwatch.StartNew();
            string key = await Task.Run(() => page.AbsoluteUri + ":" + width?.ToString("R", CultureInfo.InvariantCulture) + ":" +
                CacheKeyService.Hash(string.Join("\n", new[] { content.HtmlContent, content.RawWikitext, content.HeadHtml, JsonConvert.SerializeObject(content.Syntax) }
                    .Concat(content.ResolvedStyleSheets ?? new List<string>()))));
            bool cached = false;
            var template = await Requests.RunAsync(key, async () =>
            {
                lock (Gate)
                {
                    var hit = Entries.First;
                    while (hit != null)
                    {
                        if (hit.Value.Key == key) { cached = true; Entries.Remove(hit); Entries.AddFirst(hit); return hit.Value.Document; }
                        hit = hit.Next;
                    }
                }
                var document = await Task.Run(() => NativeArticleDocumentService.Parse(content, page, width));
                int nodes = Count(document.Nodes);
                if (nodes <= 24000)
                {
                    lock (Gate)
                    {
                        Entries.AddFirst(new Entry { Key = key, Document = document, Nodes = nodes }); _nodes += nodes;
                        while (Entries.Count > 3 || _nodes > 24000) { _nodes -= Entries.Last.Value.Nodes; Entries.RemoveLast(); }
                    }
                }
                return document;
            });
            var copy = await Task.Run(() => Clone(template));
            ArticleLoadMetrics.Record(cached ? "model-cache" : "model-parse", elapsed.ElapsedMilliseconds);
            return copy;
        }
        private static int Count(IEnumerable<NativeArticleNode> nodes) => nodes.Sum(n => 1 + Count(n.Children));
        private static NativeArticleDocument Clone(NativeArticleDocument source)
        {
            var copy = new NativeArticleDocument { RawWikitext = source.RawWikitext, RevisionId = source.RevisionId, Syntax = source.Syntax,
                Background = source.Background, Foreground = source.Foreground, ColorContext = source.ColorContext };
            copy.Nodes.AddRange(source.Nodes.Select(n => n.CopyForRendering())); copy.SourceImages.AddRange(source.SourceImages);
            copy.EmbeddedStyleSheets.AddRange(source.EmbeddedStyleSheets); copy.StyleRules.AddRange(source.StyleRules);
            foreach (string feature in source.UnsupportedFeatures) copy.UnsupportedFeatures.Add(feature);
            return copy;
        }
    }
}
