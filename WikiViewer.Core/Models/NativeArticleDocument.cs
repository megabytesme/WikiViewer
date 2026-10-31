using System;
using System.Collections.Generic;
using System.Linq;

namespace WikiViewer.Core.Models
{
    public enum NativeArticleNodeKind
    {
        Container, Text, Paragraph, Heading, Strong, Emphasis, Underline, Strike, Code,
        Preformatted, Link, Image, Media, Math, LineBreak, Rule, List, ListItem, Table, TableRow, TableCell, Quote, Unsupported
    }

    public sealed class NativeArticleNode
    {
        public NativeArticleNodeKind Kind { get; set; }
        public string Text { get; set; }
        public string Id { get; set; }
        public Uri Uri { get; set; }
        public int Level { get; set; }
        public int ImageWidth { get; set; }
        public int ColumnSpan { get; set; } = 1;
        public int RowSpan { get; set; } = 1;
        public bool IsHeader { get; set; }
        public bool Ordered { get; set; }
        public int ListStart { get; set; } = 1;
        public int? ListValue { get; set; }
        public bool PreserveWhitespace { get; set; }
        public bool RightToLeft { get; set; }
        public bool IsBlock { get; set; }
        public string TagName { get; set; }
        public string Classes { get; set; }
        public string SourceFileName { get; set; }
        public string OriginalHtml { get; set; }
        public string SvgContent { get; set; }
        public Uri PosterUri { get; set; }
        public List<NativeMediaSource> MediaSources { get; private set; } = new List<NativeMediaSource>();
        public List<Uri> ImageSources { get; private set; } = new List<Uri>();
        public Dictionary<string, string> Attributes { get; private set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public bool HasSourceLayout { get; set; }
        public NativeArticleStyle Style { get; set; } = new NativeArticleStyle();
        public List<NativeArticleNode> Children { get; private set; } = new List<NativeArticleNode>();
        public NativeArticleNode CopyForRendering()
        {
            var copy = (NativeArticleNode)MemberwiseClone(); copy.Style = Style.Copy();
            copy.Children = Children.Select(c => c.CopyForRendering()).ToList();
            copy.Attributes = new Dictionary<string, string>(Attributes, StringComparer.OrdinalIgnoreCase);
            copy.MediaSources = new List<NativeMediaSource>(MediaSources); copy.ImageSources = new List<Uri>(ImageSources);
            return copy;
        }
    }

    public sealed class NativeMediaSource
    {
        public Uri Uri { get; set; }
        public string ContentType { get; set; }
        public string Label { get; set; }
        public int Height { get; set; }
    }

    public sealed class NativeArticleDocument
    {
        public string RawWikitext { get; set; }
        public long RevisionId { get; set; }
        public WikiSyntaxConfiguration Syntax { get; set; }
        public string Background { get; set; }
        public ArticleBackground ColorContext { get; set; }
        public string Foreground { get; set; }
        public List<SourceImageLayout> SourceImages { get; } = new List<SourceImageLayout>();
        public List<string> EmbeddedStyleSheets { get; } = new List<string>();
        public List<ArticleCssRule> StyleRules { get; } = new List<ArticleCssRule>();
        public List<NativeArticleNode> Nodes { get; } = new List<NativeArticleNode>();
        public HashSet<string> UnsupportedFeatures { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }
}
