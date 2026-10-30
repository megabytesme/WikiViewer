using System;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using HtmlAgilityPack;
using WikiViewer.Core.Models;

namespace WikiViewer.Core.Services
{
    /// <summary>Convert server-expanded HTML to a restricted native reading model. Does not execute page code.</summary>
    public static class NativeArticleDocumentService
    {
        public static NativeArticleDocument Parse(string html, Uri pageUri)
        {
            if (pageUri == null || !pageUri.IsAbsoluteUri) throw new ArgumentException("An absolute page URI is required.", nameof(pageUri));
            var htmlDocument = new HtmlDocument();
            htmlDocument.LoadHtml(html ?? "");
            var root = htmlDocument.DocumentNode.SelectSingleNode("//*[contains(concat(' ', normalize-space(@class), ' '), ' mw-parser-output ')]")
                ?? htmlDocument.DocumentNode.SelectSingleNode("//body") ?? htmlDocument.DocumentNode;
            var result = new NativeArticleDocument();
            foreach (var child in root.ChildNodes)
            {
                var node = Convert(child, pageUri, result, false, false, 0);
                if (node != null) result.Nodes.Add(node);
            }
            return result;
        }

        private static NativeArticleNode Convert(HtmlNode source, Uri pageUri, NativeArticleDocument document, bool preserveWhitespace, bool rtl, int depth)
        {
            if (source.NodeType == HtmlNodeType.Comment) return null;
            if (source.NodeType == HtmlNodeType.Text)
                return new NativeArticleNode { Kind = NativeArticleNodeKind.Text, PreserveWhitespace = preserveWhitespace,
                    Text = preserveWhitespace ? WebUtility.HtmlDecode(source.InnerText) : Regex.Replace(WebUtility.HtmlDecode(source.InnerText), @"[ \t\r\n]+", " ") };
            string tag = source.Name.ToLowerInvariant();
            if (tag == "script" || tag == "style" || tag == "noscript" || tag == "head" || tag == "meta" || tag == "link") return null;
            if (depth >= 128)
            {
                document.UnsupportedFeatures.Add("deeply nested content");
                return new NativeArticleNode { Kind = NativeArticleNodeKind.Unsupported, Text = "This section is available in Web view." };
            }
            var node = new NativeArticleNode { Kind = NativeArticleNodeKind.Container, Id = source.GetAttributeValue("id", null),
                IsBlock = tag == "div" || tag == "section" || tag == "article" || tag == "figure" || tag == "figcaption" || tag == "caption",
                PreserveWhitespace = preserveWhitespace || tag == "pre", RightToLeft = source.GetAttributeValue("dir", rtl ? "rtl" : "ltr") == "rtl" };
            switch (tag)
            {
                case "p": node.Kind = NativeArticleNodeKind.Paragraph; break;
                case "h1": case "h2": case "h3": case "h4": case "h5": case "h6":
                    node.Kind = NativeArticleNodeKind.Heading; node.Level = tag[1] - '0'; break;
                case "b": case "strong": node.Kind = NativeArticleNodeKind.Strong; break;
                case "i": case "em": case "cite": node.Kind = NativeArticleNodeKind.Emphasis; break;
                case "u": case "ins": node.Kind = NativeArticleNodeKind.Underline; break;
                case "s": case "strike": case "del": node.Kind = NativeArticleNodeKind.Strike; break;
                case "code": case "tt": case "kbd": case "samp": node.Kind = NativeArticleNodeKind.Code; break;
                case "pre": node.Kind = NativeArticleNodeKind.Preformatted; break;
                case "blockquote": node.Kind = NativeArticleNodeKind.Quote; break;
                case "br": node.Kind = NativeArticleNodeKind.LineBreak; break;
                case "hr": node.Kind = NativeArticleNodeKind.Rule; break;
                case "a": node.Kind = NativeArticleNodeKind.Link; node.Uri = Resolve(source.GetAttributeValue("href", ""), pageUri); break;
                case "img":
                    node.Kind = NativeArticleNodeKind.Image;
                    node.Text = WebUtility.HtmlDecode(source.GetAttributeValue("alt", ""));
                    node.Uri = Resolve(source.GetAttributeValue("data-src", null) ?? source.GetAttributeValue("src", ""), pageUri);
                    break;
                case "ul": case "ol": case "dl":
                    node.Kind = NativeArticleNodeKind.List; node.Ordered = tag == "ol";
                    int start;
                    if (int.TryParse(source.GetAttributeValue("start", ""), out start)) node.ListStart = Math.Max(-1000000, Math.Min(1000000, start));
                    if (tag == "dl") node.Text = "definition";
                    break;
                case "li": case "dt": case "dd":
                    node.Kind = NativeArticleNodeKind.ListItem; node.IsHeader = tag == "dt";
                    if (tag == "dd") node.Text = "definition";
                    int value;
                    if (int.TryParse(source.GetAttributeValue("value", ""), out value)) node.ListValue = Math.Max(-1000000, Math.Min(1000000, value));
                    break;
                case "table": node.Kind = NativeArticleNodeKind.Table; break;
                case "tr": node.Kind = NativeArticleNodeKind.TableRow; break;
                case "td": case "th":
                    node.Kind = NativeArticleNodeKind.TableCell; node.IsHeader = tag == "th";
                    node.ColumnSpan = Span(source, "colspan"); node.RowSpan = Span(source, "rowspan"); break;
                case "div": case "span": case "section": case "article": case "figure": case "figcaption":
                case "tbody": case "thead": case "tfoot": case "caption": case "small": case "big":
                case "sup": case "sub": case "abbr": case "q": case "time": case "ruby": case "rb": case "rt": case "rp":
                    break;
                default:
                    document.UnsupportedFeatures.Add(tag);
                    node.Kind = NativeArticleNodeKind.Unsupported;
                    node.Text = WebUtility.HtmlDecode(source.InnerText);
                    return node;
            }
            foreach (var child in source.ChildNodes)
            {
                var converted = Convert(child, pageUri, document, node.PreserveWhitespace, node.RightToLeft, depth + 1);
                if (converted != null) node.Children.Add(converted);
            }
            return node;
        }

        private static int Span(HtmlNode source, string attribute)
        { int span; return int.TryParse(source.GetAttributeValue(attribute, "1"), out span) ? Math.Max(1, Math.Min(64, span)) : 1; }

        private static Uri Resolve(string value, Uri pageUri)
        {
            Uri uri;
            return !string.IsNullOrWhiteSpace(value) && Uri.TryCreate(pageUri, WebUtility.HtmlDecode(value), out uri) &&
                (uri.Scheme == "http" || uri.Scheme == "https") ? uri : null;
        }
    }
}
