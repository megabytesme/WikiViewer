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
            => Parse(new ArticleContent { HtmlContent = html }, pageUri);

        public static NativeArticleDocument Parse(ArticleContent content, Uri pageUri)
        {
            if (pageUri == null || !pageUri.IsAbsoluteUri) throw new ArgumentException("An absolute page URI is required.", nameof(pageUri));
            var htmlDocument = new HtmlDocument();
            htmlDocument.LoadHtml(content.HtmlContent ?? "");
            var root = htmlDocument.DocumentNode.SelectSingleNode("//*[contains(concat(' ', normalize-space(@class), ' '), ' mw-parser-output ')]")
                ?? htmlDocument.DocumentNode.SelectSingleNode("//body") ?? htmlDocument.DocumentNode;
            var result = new NativeArticleDocument { RawWikitext = content.RawWikitext, RevisionId = content.RevisionId };
            try { result.SourceImages.AddRange(WikitextLayoutService.ReadImages(content.RawWikitext)); }
            catch (Exception) { result.UnsupportedFeatures.Add("raw source layout"); }
            foreach (var child in root.ChildNodes)
            {
                var node = Convert(child, pageUri, result, false, false, false, null, 0);
                if (node != null) result.Nodes.Add(node);
            }
            return result;
        }

        private static NativeArticleNode Convert(HtmlNode source, Uri pageUri, NativeArticleDocument document, bool preserveWhitespace, bool rtl, bool insideLink, NativeArticleStyle parentStyle, int depth)
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
            var style = NativeArticleStyleService.Read(source);
            Inherit(style, parentStyle);
            var node = new NativeArticleNode { Kind = NativeArticleNodeKind.Container, Id = source.GetAttributeValue("id", null),
                TagName = tag, Classes = source.GetAttributeValue("class", ""), Style = style,
                IsBlock = tag == "div" || tag == "section" || tag == "article" || tag == "figure" || tag == "figcaption" || tag == "caption",
                PreserveWhitespace = preserveWhitespace || tag == "pre" || style.PreserveWhitespace, RightToLeft = string.Equals(source.GetAttributeValue("dir", rtl ? "rtl" : "ltr"), "rtl", StringComparison.OrdinalIgnoreCase) };
            foreach (var attribute in source.Attributes) node.Attributes[attribute.Name] = attribute.Value;
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
                    int imageWidth;
                    if (int.TryParse(source.GetAttributeValue("width", ""), out imageWidth)) node.ImageWidth = Math.Max(0, Math.Min(2000, imageWidth));
                    ApplySourceImage(source, node, document);
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
                case "details": node.IsBlock = true; style.Collapsible = true; style.InitiallyCollapsed = !source.Attributes.Contains("open"); break;
                case "summary": node.Kind = NativeArticleNodeKind.Paragraph; style.FontWeight = "bold"; break;
                case "video": case "audio": case "iframe": case "object": case "embed": case "svg": case "math":
                    node.OriginalHtml = source.OuterHtml;
                    document.UnsupportedFeatures.Add(tag);
                    node.Kind = NativeArticleNodeKind.Unsupported;
                    node.Text = WebUtility.HtmlDecode(source.InnerText);
                    node.Uri = Resolve(source.GetAttributeValue("src", ""), pageUri);
                    break;
                default:
                    document.UnsupportedFeatures.Add(tag);
                    node.IsBlock = true;
                    break;
            }
            // UWP Hyperlink labels permit Run and Span, but not nested Hyperlink,
            // LineBreak or embedded block controls. Preserve their readable content.
            if (insideLink)
            {
                if (node.Kind == NativeArticleNodeKind.LineBreak) { node.Kind = NativeArticleNodeKind.Text; node.Text = " "; }
                if (node.Kind == NativeArticleNodeKind.Link || node.IsBlock || node.Kind == NativeArticleNodeKind.Paragraph || node.Kind == NativeArticleNodeKind.Heading ||
                    node.Kind == NativeArticleNodeKind.Quote || node.Kind == NativeArticleNodeKind.Preformatted || node.Kind == NativeArticleNodeKind.List || node.Kind == NativeArticleNodeKind.Table)
                { node.Kind = NativeArticleNodeKind.Container; node.IsBlock = false; }
            }
            foreach (var child in source.ChildNodes)
            {
                var converted = Convert(child, pageUri, document, node.PreserveWhitespace, node.RightToLeft, insideLink || node.Kind == NativeArticleNodeKind.Link, style, depth + 1);
                if (converted != null) node.Children.Add(converted);
            }
            return node;
        }

        private static void Inherit(NativeArticleStyle style, NativeArticleStyle parent)
        {
            double font = parent?.FontSize?.Resolve(18) ?? 18;
            if (style.FontSize != null) style.FontSize = new ArticleLength { Value = style.FontSize.Resolve(font, font), Unit = "px" };
            else style.FontSize = parent?.FontSize;
            style.Foreground = style.Foreground ?? parent?.Foreground;
            style.Alignment = style.Alignment ?? parent?.Alignment;
            style.FontFamily = style.FontFamily ?? parent?.FontFamily;
            style.FontWeight = style.FontWeight ?? parent?.FontWeight;
            style.FontStyle = style.FontStyle ?? parent?.FontStyle;
        }

        private static void ApplySourceImage(HtmlNode source, NativeArticleNode node, NativeArticleDocument document)
        {
            string file = source.GetAttributeValue("resource", "");
            for (var parent = source.ParentNode; string.IsNullOrEmpty(file) && parent != null; parent = parent.ParentNode)
            {
                string href = parent.GetAttributeValue("href", "");
                int fileIndex = href.IndexOf("File:", StringComparison.OrdinalIgnoreCase);
                if (fileIndex >= 0) file = href.Substring(fileIndex);
            }
            if (string.IsNullOrEmpty(file))
            {
                string src = source.GetAttributeValue("src", "");
                var thumb = Regex.Match(src, @"/thumb/[^/]+/[^/]+/([^/]+)/");
                file = thumb.Success ? thumb.Groups[1].Value : src.Substring(src.LastIndexOf('/') + 1).Split('?')[0];
            }
            int start = file.IndexOf("File:", StringComparison.OrdinalIgnoreCase);
            if (start >= 0) file = file.Substring(start);
            node.SourceFileName = WikitextLayoutService.NormalizeFileName(file);
            var matches = document.SourceImages.Where(h => string.Equals(h.FileName, node.SourceFileName, StringComparison.Ordinal)).ToList();
            // Never guess which occurrence is intended when a file has conflicting source options.
            if (matches.Count == 0 || matches.Any(h => h.Alignment != matches[0].Alignment || h.Width != matches[0].Width)) return;
            var hint = matches[0];
            node.HasSourceLayout = true;
            if (node.ImageWidth == 0 && hint.Width > 0) node.ImageWidth = hint.Width;
            if (node.Style.Width == null && hint.Width > 0) node.Style.Width = ArticleLength.Parse(hint.Width.ToString(System.Globalization.CultureInfo.InvariantCulture));
            if (node.Style.Float == null && node.Style.Alignment == null)
            {
                if (hint.Alignment == "left" || hint.Alignment == "right") node.Style.Float = hint.Alignment;
                else if (hint.Alignment == "center") node.Style.Alignment = "center";
            }
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
