using System;
using System.Collections.Generic;

namespace WikiViewer.Core.Models
{
    public enum NativeArticleNodeKind
    {
        Container, Text, Paragraph, Heading, Strong, Emphasis, Underline, Strike, Code,
        Preformatted, Link, Image, LineBreak, Rule, List, ListItem, Table, TableRow, TableCell, Quote, Unsupported
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
        public List<NativeArticleNode> Children { get; } = new List<NativeArticleNode>();
    }

    public sealed class NativeArticleDocument
    {
        public List<NativeArticleNode> Nodes { get; } = new List<NativeArticleNode>();
        public HashSet<string> UnsupportedFeatures { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }
}
