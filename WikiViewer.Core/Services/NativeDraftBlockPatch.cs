using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using WikiViewer.Core.Models;
using WikitextParser;
using WikitextParser.Elements;

namespace WikiViewer.Core.Services
{
    /// <summary>Reparse one edited paragraph or heading, preserving its wiki style and unchanged citations.</summary>
    public sealed class NativeDraftBlockPatch
    {
        public NativeArticleNode Original { get; private set; }
        public List<NativeArticleNode> Parent { get; private set; }
        public List<NativeArticleNode> Replacements { get; private set; }
        public string Source { get; private set; }
        private static readonly Regex References = new Regex(@"<ref\b[^>]*(?:/\s*>|>[\s\S]*?</ref\s*>)", RegexOptions.IgnoreCase);

        public static NativeDraftBlockPatch TryCreate(NativeArticleDocument document, string source, Uri page)
        {
#if WIKITEXT_EXTENDED
            string before = document?.RawWikitext;
            if (before == null || source == null || before == source) return null;
            int start = 0, end = before.Length, nextEnd = source.Length;
            while (start < Math.Min(before.Length, source.Length) && before[start] == source[start]) start++;
            while (end > start && nextEnd > start && before[end - 1] == source[nextEnd - 1]) { end--; nextEnd--; }
            int cursor = 0;
            foreach (var element in Parser.Parse(before))
            {
                if (string.IsNullOrEmpty(element.SourceText)) continue;
                int at = before.IndexOf(element.SourceText, cursor, StringComparison.Ordinal);
                if (at < 0) continue;
                cursor = at + element.SourceText.Length;
                bool heading = element is HeadingElement;
                if (!(element is ParagraphElement) && !heading || start < at || end > cursor) continue;
                int newLength = element.SourceText.Length + source.Length - before.Length;
                if (newLength < 0 || newLength > 32768) return null;
                string changed = source.Substring(at, newLength);
                var oldRefs = References.Matches(element.SourceText).Cast<Match>().Select(m => m.Value).ToList();
                var newRefs = References.Matches(changed).Cast<Match>().Select(m => m.Value).ToList();
                if (!oldRefs.SequenceEqual(newRefs)) return null;
                string expected = Normalize(string.Concat(Parser.Parse(References.Replace(element.SourceText, "")).Select(e => e.ConvertToText())));
                if (expected.Length < 8) return null;
                NativeArticleNode original = null; List<NativeArticleNode> parent = null;
                foreach (var entry in Paragraphs(document.Nodes))
                {
                    if ((entry.Item1.Kind == NativeArticleNodeKind.Heading) != heading) continue;
                    if (Normalize(Text(entry.Item1)) != expected) continue;
                    if (original != null) return null;
                    original = entry.Item1; parent = entry.Item2;
                }
                if (original == null) return null;
                var citations = CitationRoots(original).ToList();
                if (citations.Count != oldRefs.Count) return null;
                int reference = 0;
                string localSource = References.Replace(changed, m => "\uE000REF" + reference++ + "\uE001");
                var parsed = Parser.ParseDocument(localSource);
                if (parsed.RequiresWikiContext) return null;
                string html = string.Concat(parsed.Elements.Select(e => e.ConvertToHtml()));
                var nodes = NativeArticleDocumentService.Parse(html, page).Nodes;
                if (nodes.Count == 0 || nodes.Count > 8 ||
                    (heading ? nodes.Count != 1 || nodes[0].Kind != NativeArticleNodeKind.Heading : nodes.Any(n => n.Kind == NativeArticleNodeKind.Heading))) return null;
                if (nodes.Count > 0) nodes[0].Id = original.Id;
                foreach (var node in nodes)
                {
                    node.Style = original.Style.Copy();
                    RestoreCitations(node.Children, citations);
                    InheritStyle(node, original.Style);
                }
                return new NativeDraftBlockPatch { Original = original, Parent = parent, Replacements = nodes, Source = source };
            }
#endif
            return null;
        }

        private static void InheritStyle(NativeArticleNode node, NativeArticleStyle style)
        {
            node.Style.ColorContext = style.ColorContext;
            if (node.Style.Foreground == null) node.Style.Foreground = style.Foreground;
            if (node.Style.FontFamily == null) node.Style.FontFamily = style.FontFamily;
            foreach (var child in node.Children) InheritStyle(child, style);
        }

        private static void RestoreCitations(List<NativeArticleNode> nodes, List<NativeArticleNode> citations)
        {
            for (int i = nodes.Count - 1; i >= 0; i--)
            {
                var node = nodes[i];
                if (node.Kind != NativeArticleNodeKind.Text) { RestoreCitations(node.Children, citations); continue; }
                var markers = Regex.Matches(node.Text ?? "", "\uE000REF([0-9]+)\uE001");
                if (markers.Count == 0) continue;
                var replacements = new List<NativeArticleNode>(); int offset = 0;
                foreach (Match marker in markers)
                {
                    if (marker.Index > offset) replacements.Add(new NativeArticleNode { Kind = NativeArticleNodeKind.Text, Text = node.Text.Substring(offset, marker.Index - offset), Style = node.Style.Copy() });
                    replacements.Add(citations[int.Parse(marker.Groups[1].Value)].CopyForRendering());
                    offset = marker.Index + marker.Length;
                }
                if (offset < node.Text.Length) replacements.Add(new NativeArticleNode { Kind = NativeArticleNodeKind.Text, Text = node.Text.Substring(offset), Style = node.Style.Copy() });
                nodes.RemoveAt(i); nodes.InsertRange(i, replacements);
            }
        }

        private static bool IsCitation(NativeArticleNode node) => (node.Id ?? "").StartsWith("cite_ref", StringComparison.Ordinal) ||
            (node.TagName == "sup" || node.TagName == "span") && (node.Attributes.TryGetValue("class", out var css) && css.Split(' ').Contains("reference"));

        private static IEnumerable<NativeArticleNode> CitationRoots(NativeArticleNode node)
        {
            foreach (var child in node.Children)
                if (IsCitation(child)) yield return child;
                else foreach (var citation in CitationRoots(child)) yield return citation;
        }

        private static string Text(NativeArticleNode node) => IsCitation(node) ? "" : node.Kind == NativeArticleNodeKind.Text ? node.Text : string.Concat(node.Children.Select(Text));
        private static string Normalize(string text) => Regex.Replace(WebUtility.HtmlDecode(text ?? ""), @"\s+", " ").Trim();
        private static IEnumerable<Tuple<NativeArticleNode, List<NativeArticleNode>>> Paragraphs(List<NativeArticleNode> nodes)
        {
            foreach (var node in nodes)
            {
                if (node.Kind == NativeArticleNodeKind.Paragraph || node.Kind == NativeArticleNodeKind.Heading) yield return Tuple.Create(node, nodes);
                else foreach (var entry in Paragraphs(node.Children)) yield return entry;
            }
        }
    }
}
