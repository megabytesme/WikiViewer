using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using WikitextParser;
using WikitextParser.Elements;
using WikiViewer.Core.Models;

namespace WikiViewer.Core.Services
{
    public static class WikitextLayoutService
    {
        public static List<SourceImageLayout> ReadImages(string source, WikiSyntaxConfiguration syntax = null)
        {
            if (string.IsNullOrEmpty(source)) return new List<SourceImageLayout>();
#if WIKITEXT_CONFIGURABLE
            var nodes = ElementTraversal.DescendantsAndSelf(Parser.Parse(source, new ParserOptions {
                FileNamespaces = syntax?.FileNamespaces, CategoryNamespaces = syntax?.CategoryNamespaces, RedirectAliases = syntax?.RedirectAliases,
                ImageOptionAliases = syntax?.ImageOptions, LinkTrailPattern = syntax?.LinkTrailPattern ?? "[a-z]+"
            }));
#elif WIKITEXT_EXTENDED
            var nodes = ElementTraversal.DescendantsAndSelf(Parser.Parse(source));
#else
            var nodes = WalkLegacy(Parser.Parse(source));
#endif
            return nodes.OfType<ImageElement>().Select(image => new SourceImageLayout
            {
                FileName = NormalizeFileName(image.FileName), SourceText = image.SourceText,
                Alignment = image.Options.LastOrDefault(o => o == "left" || o == "right" || o == "center" || o == "none") ??
                    (image.Options.Any(o => o == "thumb" || o == "thumbnail" || o == "frame") ? "right" : null),
                Width = image.Options.Select(o => Regex.Match(o, @"^(\d+)(?:x\d+)?px$", RegexOptions.IgnoreCase))
                    .Where(m => m.Success).Select(m => { int width; return int.TryParse(m.Groups[1].Value, out width) ? Math.Min(2000, width) : 0; }).LastOrDefault()
            }).ToList();
        }

        public static string NormalizeFileName(string name, WikiSyntaxConfiguration syntax = null)
        {
            string result = Uri.UnescapeDataString((name ?? "").Replace('_', ' ')).Trim();
            if (result.StartsWith("./", StringComparison.Ordinal)) result = result.Substring(2);
            foreach (string alias in (syntax?.FileNamespaces ?? new List<string>()).Concat(new[] { "File", "Image" }))
            {
                int marker = result.IndexOf("/" + alias + ":", StringComparison.OrdinalIgnoreCase);
                if (marker >= 0) { result = result.Substring(marker + alias.Length + 2); break; }
            }
            if (result.StartsWith("File:", StringComparison.OrdinalIgnoreCase)) result = result.Substring(5);
            if (result.StartsWith("Image:", StringComparison.OrdinalIgnoreCase)) result = result.Substring(6);
            int colon = result.IndexOf(':');
            if (colon > 0 && syntax?.FileNamespaces?.Any(alias => alias.Equals(result.Substring(0, colon), StringComparison.OrdinalIgnoreCase)) == true) result = result.Substring(colon + 1);
            return result;
        }

#if !WIKITEXT_EXTENDED
        private static IEnumerable<WikitextElement> WalkLegacy(IEnumerable<WikitextElement> nodes)
        {
            foreach (var node in nodes)
            {
                yield return node;
                IEnumerable<WikitextElement> children = new WikitextElement[0];
                if (node is ParagraphElement p) children = p.ChildElements;
                else if (node is TemplateElement t) children = t.Parameters.Select(v => v.Value);
                else if (node is BoldElement b) children = new[] { b.InnerElement };
                else if (node is ItalicElement i) children = new[] { i.InnerElement };
                foreach (var child in WalkLegacy(children)) yield return child;
            }
        }
#endif
    }
}
