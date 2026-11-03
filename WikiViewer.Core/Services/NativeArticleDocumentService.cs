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
        public static bool IsSamePage(Uri page, Uri target)
        {
            if (page == null || target == null || !page.IsAbsoluteUri || !target.IsAbsoluteUri) return false;
            // MediaWiki treats spaces and underscores in article titles as equivalent.
            Func<Uri, string> path = uri => Regex.Replace(Uri.UnescapeDataString(uri.AbsolutePath).Replace('_', ' '), " +", " ");
            return page.Scheme == target.Scheme && page.Host == target.Host && page.Port == target.Port && page.Query == target.Query && path(page) == path(target);
        }
        public static NativeArticleDocument Parse(string html, Uri pageUri)
            => Parse(new ArticleContent { HtmlContent = html }, pageUri);

        public static NativeArticleDocument Parse(ArticleContent content, Uri pageUri, double? viewportWidth = null)
        {
            if (pageUri == null || !pageUri.IsAbsoluteUri) throw new ArgumentException("An absolute page URI is required.", nameof(pageUri));
            var htmlDocument = new HtmlDocument();
            htmlDocument.LoadHtml(string.IsNullOrEmpty(content.HeadHtml) ? content.HtmlContent ?? "" : content.HeadHtml + (content.HtmlContent ?? "") + "</body></html>");
            var root = htmlDocument.DocumentNode.SelectSingleNode("//*[contains(concat(' ', normalize-space(@class), ' '), ' mw-parser-output ')]")
                ?? htmlDocument.DocumentNode.SelectSingleNode("//body") ?? htmlDocument.DocumentNode;
            var result = new NativeArticleDocument { RawWikitext = content.RawWikitext, RevisionId = content.RevisionId, Syntax = content.Syntax };
            foreach (var sheet in content.ResolvedStyleSheets ?? Enumerable.Empty<string>())
            {
                var css = ArticleStyleResourceService.NormalizeResponse(sheet);
                if (css != null) result.StyleRules.AddRange(ArticleStyleSheet.Parse(css, viewportWidth));
            }
            foreach (var sheet in htmlDocument.DocumentNode.SelectNodes("//style") ?? Enumerable.Empty<HtmlNode>())
            {
                result.EmbeddedStyleSheets.Add(sheet.InnerText);
                result.StyleRules.AddRange(ArticleStyleSheet.Parse(sheet.InnerText, viewportWidth));
            }
            try { result.SourceImages.AddRange(WikitextLayoutService.ReadImages(content.RawWikitext, content.Syntax)); }
            catch (Exception) { result.UnsupportedFeatures.Add("raw source layout"); }
            NativeArticleStyle rootStyle = null;
            foreach (var ancestor in root.AncestorsAndSelf().Reverse().Where(a => a.NodeType == HtmlNodeType.Element))
            {
                var inherited = rootStyle;
                rootStyle = NativeArticleStyleService.Read(ancestor, result.StyleRules, inherited);
                Inherit(rootStyle, inherited);
                result.Background = rootStyle.Background ?? result.Background;
            }
            result.Foreground = rootStyle?.Foreground;
            result.ColorContext = rootStyle?.ColorContext;
            foreach (var child in root.ChildNodes)
            {
                var node = Convert(child, pageUri, result, false, false, false, rootStyle, 0);
                if (node != null) NormalizeFlow(node);
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
            var style = NativeArticleStyleService.Read(source, document.StyleRules, parentStyle);
            Inherit(style, parentStyle);
            var node = new NativeArticleNode { Kind = NativeArticleNodeKind.Container, Id = WebUtility.HtmlDecode(source.GetAttributeValue("id", null) ?? (tag == "a" ? source.GetAttributeValue("name", null) : null)),
                TagName = tag, Classes = source.GetAttributeValue("class", ""), Style = style,
                IsBlock = style.Position == "absolute" || style.Display == "block" || tag == "div" || tag == "section" || tag == "article" || tag == "figure" || tag == "figcaption" || tag == "caption",
                PreserveWhitespace = preserveWhitespace || tag == "pre" || style.PreserveWhitespace, RightToLeft = style.Direction != null ? style.Direction == "rtl" : rtl };
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
                case "center": node.IsBlock = true; style.Alignment = "center"; break;
                case "br": node.Kind = NativeArticleNodeKind.LineBreak; break;
                case "hr": node.Kind = NativeArticleNodeKind.Rule; break;
                case "a": node.Kind = NativeArticleNodeKind.Link; node.Uri = Resolve(source.GetAttributeValue("href", ""), pageUri); break;
                case "img":
                    node.Kind = NativeArticleNodeKind.Image;
                    node.Text = WebUtility.HtmlDecode(source.GetAttributeValue("alt", ""));
                    node.ImageSources.AddRange(NativeMediaService.ImageSources(source, pageUri));
                    node.Uri = node.ImageSources.FirstOrDefault();
                    int imageWidth;
                    if (int.TryParse(source.GetAttributeValue("width", ""), out imageWidth)) node.ImageWidth = Math.Max(0, Math.Min(2000, imageWidth));
                    ApplySourceImage(source, node, document);
                    break;
                case "ul": case "ol": case "dl":
                    node.Kind = NativeArticleNodeKind.List; node.Ordered = tag == "ol";
                    int start;
                    if (int.TryParse(source.GetAttributeValue("start", ""), out start)) node.ListStart = Math.Max(-1000000, Math.Min(1000000, start));
                    if (tag == "dl") node.Text = "definition";
                    if (node.Classes.Split(' ').Contains("gallery")) { node.Kind = NativeArticleNodeKind.Container; node.IsBlock = true; style.Display = "gallery"; style.Wrap = true; }
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
                case "sup": case "sub": case "abbr": case "q": case "time": case "ruby": case "rb": case "rt": case "rp": case "picture":
                    break;
                case "details": node.IsBlock = true; style.Collapsible = true; style.InitiallyCollapsed = !source.Attributes.Contains("open"); break;
                case "summary": node.Kind = NativeArticleNodeKind.Paragraph; style.FontWeight = "bold"; break;
                case "svg":
                    node.OriginalHtml = source.OuterHtml;
                    node.SvgContent = NativeSvgService.Prepare(source.OuterHtml);
                    node.Text = WebUtility.HtmlDecode(source.SelectSingleNode(".//title")?.InnerText ?? source.GetAttributeValue("aria-label", "Vector image"));
                    node.Kind = node.SvgContent != null ? NativeArticleNodeKind.Image : NativeArticleNodeKind.Unsupported;
                    if (node.SvgContent == null) document.UnsupportedFeatures.Add("svg");
                    return node;
                case "video": case "audio":
                    node.Kind = NativeArticleNodeKind.Media; node.IsBlock = true;
                    node.OriginalHtml = source.OuterHtml;
                    node.PosterUri = Resolve(source.GetAttributeValue("poster", ""), pageUri);
                    var candidates = new[] { source }.Concat(source.ChildNodes.Where(c => c.Name.Equals("source", StringComparison.OrdinalIgnoreCase)));
                    foreach (var candidate in candidates)
                    {
                        var mediaSource = NativeMediaService.ReadSource(candidate, pageUri);
                        if (mediaSource != null && !node.MediaSources.Any(s => s.Uri == mediaSource.Uri)) node.MediaSources.Add(mediaSource);
                    }
                    node.Uri = node.MediaSources.FirstOrDefault()?.Uri;
                    if (node.MediaSources.Count == 0)
                    {
                        document.UnsupportedFeatures.Add(tag); node.Kind = NativeArticleNodeKind.Unsupported;
                        node.Text = WebUtility.HtmlDecode(source.InnerText);
                    }
                    break;
                case "source": case "track": return null;
                case "math":
                    var math = ReadMath(source, document, 0); math.Style = style; math.Id = node.Id;
                    math.IsBlock = source.GetAttributeValue("display", "") == "block"; return math;
                case "iframe": case "object": case "embed":
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
            if (node.Classes.Split(' ').Contains("gallerybox")) { node.Kind = NativeArticleNodeKind.Container; node.IsBlock = true; node.Style.Float = null; }
            // UWP Hyperlink labels permit Run and Span, but not nested Hyperlink,
            // LineBreak or embedded block controls. Preserve their readable content.
            if (insideLink)
            {
                if (node.Kind != NativeArticleNodeKind.Image) style.Float = null;
                style.Collapsible = false;
                if (node.Kind == NativeArticleNodeKind.LineBreak) { node.Kind = NativeArticleNodeKind.Text; node.Text = " "; }
                if (node.Kind != NativeArticleNodeKind.Image && node.Kind != NativeArticleNodeKind.Media &&
                    (node.Kind == NativeArticleNodeKind.Link || node.IsBlock || node.Kind == NativeArticleNodeKind.Paragraph || node.Kind == NativeArticleNodeKind.Heading ||
                    node.Kind == NativeArticleNodeKind.Quote || node.Kind == NativeArticleNodeKind.Preformatted || node.Kind == NativeArticleNodeKind.List || node.Kind == NativeArticleNodeKind.Table))
                { node.Kind = NativeArticleNodeKind.Container; node.IsBlock = false; }
            }
            foreach (var child in source.ChildNodes)
            {
                var converted = Convert(child, pageUri, document, node.PreserveWhitespace, node.RightToLeft, insideLink || node.Kind == NativeArticleNodeKind.Link, style, depth + 1);
                if (converted != null) node.Children.Add(converted);
            }
            // MediaWiki wraps players in inline file spans. Keep those wrappers
            // in block layout so the player receives the card's finite width.
            if (tag == "span" && ContainsMedia(node))
                node.IsBlock = true;
            if (node.Kind == NativeArticleNodeKind.List && (style.Display == "grid" || style.Display == "flex"))
            {
                node.Kind = NativeArticleNodeKind.Container; node.IsBlock = true;
                foreach (var item in node.Children.Where(c => c.Kind == NativeArticleNodeKind.ListItem))
                { item.Kind = NativeArticleNodeKind.Container; item.IsBlock = true; }
            }
            else if (node.Kind == NativeArticleNodeKind.List && source.AncestorsAndSelf().Any(a => a.GetAttributeValue("class", "").Split(' ').Contains("hlist")))
            {
                // MediaWiki horizontal lists are readable, wrapping text with separators.
                node.Kind = NativeArticleNodeKind.Container; node.IsBlock = false;
                var items = node.Children.Where(c => c.Kind == NativeArticleNodeKind.ListItem).ToList();
                node.Children.Clear();
                bool nested = source.Ancestors().Any(a => a.Name == "ul" || a.Name == "ol");
                if (nested) node.Children.Add(new NativeArticleNode { Kind = NativeArticleNodeKind.Text, Text = " (" });
                foreach (var item in items)
                {
                    if (node.Children.Count > (nested ? 1 : 0)) node.Children.Add(new NativeArticleNode { Kind = NativeArticleNodeKind.Text, Text = " · " });
                    item.Kind = NativeArticleNodeKind.Container; item.IsBlock = false; node.Children.Add(item);
                }
                if (nested) node.Children.Add(new NativeArticleNode { Kind = NativeArticleNodeKind.Text, Text = ")" });
            }
            if (node.Classes.Split(' ').Contains("navbox-inner") && style.Display == "grid") NormalizeNavigationBox(node);
            return node;
        }

        private static void NormalizeNavigationBox(NativeArticleNode node)
        {
            // These grids encode group/list relationships, including nested subgroups.
            // Keep that relationship rather than scattering each child into equal tiles.
            var children = node.Children.Where(c => c.Kind != NativeArticleNodeKind.Text || !string.IsNullOrWhiteSpace(c.Text)).ToList();
            node.Kind = NativeArticleNodeKind.Table; node.IsBlock = true; node.Style.Display = "block"; node.Children.Clear();
            NativeArticleNode group = null;
            Action<NativeArticleNode, NativeArticleNode> row = (label, body) =>
            {
                var line = new NativeArticleNode { Kind = NativeArticleNodeKind.TableRow };
                foreach (var part in new[] { label, body }.Where(p => p != null))
                {
                    var cell = new NativeArticleNode { Kind = NativeArticleNodeKind.TableCell, ColumnSpan = label == null || body == null ? 2 : 1 };
                    cell.Style.ColorContext = part.Style.ColorContext;
                    if (part == label && body != null) cell.Style.Width = ArticleLength.Parse("24%");
                    cell.Children.Add(part); line.Children.Add(cell);
                }
                node.Children.Add(line);
            };
            foreach (var child in children)
            {
                var classes = (child.Classes ?? "").Split(' ');
                if (classes.Contains("navbox-group"))
                {
                    if (group != null) row(null, group);
                    group = child;
                }
                else if (classes.Contains("navbox-list")) { row(group, child); group = null; }
                else { if (group != null) { row(null, group); group = null; } row(null, child); }
            }
            if (group != null) row(null, group);
        }

        private static NativeArticleNode ReadMath(HtmlNode source, NativeArticleDocument document, int depth)
        {
            string tag = source.Name.ToLowerInvariant();
            var node = new NativeArticleNode { Kind = NativeArticleNodeKind.Math, TagName = tag, OriginalHtml = depth == 0 ? source.OuterHtml : null };
            foreach (var attribute in source.Attributes) node.Attributes[attribute.Name] = attribute.Value;
            string[] supported = { "math", "semantics", "mrow", "mstyle", "mi", "mn", "mo", "mtext", "ms", "mspace", "mfrac", "msqrt", "mroot", "msub", "msup", "msubsup", "munder", "mover", "munderover", "mfenced", "mtable", "mtr", "mtd" };
            if (depth > 64 || !supported.Contains(tag))
            {
                document.UnsupportedFeatures.Add("math layout"); node.Kind = NativeArticleNodeKind.Unsupported;
                node.Text = WebUtility.HtmlDecode(source.InnerText); return node;
            }
            foreach (var child in source.ChildNodes)
            {
                if (child.NodeType == HtmlNodeType.Comment || child.Name == "annotation" || child.Name == "annotation-xml") continue;
                if (child.NodeType == HtmlNodeType.Text)
                { if (!string.IsNullOrWhiteSpace(child.InnerText)) node.Children.Add(new NativeArticleNode { Kind = NativeArticleNodeKind.Math, TagName = "token", Text = WebUtility.HtmlDecode(child.InnerText) }); }
                else if (child.NodeType == HtmlNodeType.Element) node.Children.Add(ReadMath(child, document, depth + 1));
            }
            return node;
        }

        private static bool ContainsMedia(NativeArticleNode node) => !node.Style.Hidden &&
            (node.Kind == NativeArticleNodeKind.Media || node.Children.Any(ContainsMedia));

        private static void Inherit(NativeArticleStyle style, NativeArticleStyle parent)
        {
            var background = ArticleColor.Parse(style.Background);
            style.ColorContext = background.HasValue && (background.Value >> 24) != 0
                ? new ArticleBackground(background.Value, parent?.ColorContext) : parent?.ColorContext;
            double font = parent?.FontSize?.Resolve(18) ?? 18;
            if (style.FontSize != null) style.FontSize = new ArticleLength { Value = style.FontSize.Resolve(font, font), Unit = "px" };
            else style.FontSize = parent?.FontSize;
            style.Foreground = style.Foreground ?? parent?.Foreground;
            style.Alignment = style.Alignment ?? parent?.Alignment;
            style.Direction = style.Direction ?? parent?.Direction;
            style.FontFamily = style.FontFamily ?? parent?.FontFamily;
            style.FontWeight = style.FontWeight ?? parent?.FontWeight;
            style.FontStyle = style.FontStyle ?? parent?.FontStyle;
        }

        private static void NormalizeFlow(NativeArticleNode node)
        {
            foreach (var child in node.Children) NormalizeFlow(child);
            if (node.Kind == NativeArticleNodeKind.Link && node.Uri != null && (node.IsBlock || node.Style.Float != null))
            {
                // Keep block placement outside the inline hyperlink, whose input handler would otherwise be lost.
                var link = new NativeArticleNode { Kind = NativeArticleNodeKind.Link, Uri = node.Uri, TagName = "a", Classes = node.Classes,
                    RightToLeft = node.RightToLeft, Style = node.Style.Copy() };
                link.Style.Float = null;
                foreach (var attribute in node.Attributes) link.Attributes[attribute.Key] = attribute.Value;
                link.Children.AddRange(node.Children);
                node.Kind = NativeArticleNodeKind.Container; node.IsBlock = true; node.Uri = null;
                node.Children.Clear(); node.Children.Add(link);
            }
            if (node.Kind != NativeArticleNodeKind.Paragraph || node.Style.Float != null) return;
            bool floating = node.Children.Any(child => child.Style.Float != null ||
                (child.Kind == NativeArticleNodeKind.Image || child.Kind == NativeArticleNodeKind.Link) && Images(child).Any(image => image.HasSourceLayout && image.Style.Float != null));
            if (!floating) return;
            var children = node.Children.ToList();
            node.Children.Clear();
            node.Kind = NativeArticleNodeKind.Container;
            node.IsBlock = true;
            NativeArticleNode paragraph = null;
            foreach (var child in children)
            {
                var sourceImage = Images(child).FirstOrDefault(image => image.HasSourceLayout && image.Style.Float != null);
                string side = child.Style.Float ?? sourceImage?.Style.Float;
                if (side == null)
                {
                    if (paragraph == null)
                    {
                        paragraph = new NativeArticleNode { Kind = NativeArticleNodeKind.Paragraph, RightToLeft = node.RightToLeft,
                            Style = new NativeArticleStyle { Foreground = node.Style.Foreground, ColorContext = node.Style.ColorContext, FontFamily = node.Style.FontFamily, FontSize = node.Style.FontSize,
                                FontWeight = node.Style.FontWeight, FontStyle = node.Style.FontStyle, Alignment = node.Style.Alignment } };
                        node.Children.Add(paragraph);
                    }
                    paragraph.Children.Add(child);
                    continue;
                }
                paragraph = null;
                var style = child.Style.Copy();
                style.Float = side;
                style.Width = style.Width ?? sourceImage?.Style.Width;
                var wrapper = new NativeArticleNode { Kind = NativeArticleNodeKind.Container, IsBlock = true, Style = style };
                child.Style = child.Style.Copy(); child.Style.Float = null;
                if (sourceImage != null) sourceImage.Style.Float = null;
                wrapper.Children.Add(child);
                node.Children.Add(wrapper);
            }
        }

        private static System.Collections.Generic.IEnumerable<NativeArticleNode> Images(NativeArticleNode node)
        {
            if (node.Kind == NativeArticleNodeKind.Image) yield return node;
            foreach (var child in node.Children) foreach (var image in Images(child)) yield return image;
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
            node.SourceFileName = WikitextLayoutService.NormalizeFileName(file, document.Syntax);
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
