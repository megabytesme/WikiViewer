using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace WikiViewer.Core.Models
{
    public sealed class ArticleBackground
    {
        public uint Color { get; }
        public ArticleBackground Parent { get; }
        public ArticleBackground(uint color, ArticleBackground parent) { Color = color; Parent = parent; }
    }

    public sealed class ArticleCssRule
    {
        public string Selector { get; set; }
        public string Declarations { get; set; }
        public int Specificity { get; set; }
        internal Func<HtmlAgilityPack.HtmlNode, bool> Match { get; set; }
        internal List<ArticleCssDeclaration> Values { get; set; }
    }

    internal sealed class ArticleCssDeclaration
    {
        public string Name;
        public string Value;
        public bool Important;
    }

    public sealed class SourceImageLayout
    {
        public string FileName { get; set; }
        public string Alignment { get; set; }
        public int Width { get; set; }
        public string SourceText { get; set; }
    }

    public sealed class ArticleLength
    {
        public double Value { get; set; }
        public string Unit { get; set; }
        public double Resolve(double available, double fontSize = 18) => Unit == "%" ? available * Value / 100 :
            Unit == "em" ? fontSize * Value : Unit == "rem" ? 18 * Value : Unit == "pt" ? Value * 4 / 3 : Value;
        public static ArticleLength Parse(string source, bool allowNegative = false)
        {
            var match = Regex.Match((source ?? "").Trim(), @"^(-?\d+(?:\.\d+)?)(px|em|rem|pt|%)?$", RegexOptions.IgnoreCase);
            double number;
            if (!match.Success || !double.TryParse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out number) ||
                (!allowNegative && number < 0) || Math.Abs(number) > 10000) return null;
            return new ArticleLength { Value = number, Unit = match.Groups[2].Success ? match.Groups[2].Value.ToLowerInvariant() : "px" };
        }
    }

    public sealed class ArticleEdges
    {
        public ArticleLength Top { get; set; }
        public ArticleLength Right { get; set; }
        public ArticleLength Bottom { get; set; }
        public ArticleLength Left { get; set; }
    }

    public sealed class NativeArticleStyle
    {
        public NativeArticleStyle Copy() => (NativeArticleStyle)MemberwiseClone();
        public string Alignment { get; set; }
        public string Direction { get; set; }
        public string VerticalAlignment { get; set; }
        public string Float { get; set; }
        public bool PositionExplicit { get; set; }
        public string Clear { get; set; }
        public string Display { get; set; }
        public string Position { get; set; }
        public ArticleLength Left { get; set; }
        public ArticleLength Top { get; set; }
        public ArticleLength Right { get; set; }
        public ArticleLength Bottom { get; set; }
        public ArticleLength TranslateX { get; set; }
        public ArticleLength TranslateY { get; set; }
        public int ZIndex { get; set; }
        public bool Wrap { get; set; }
        public int GridColumns { get; set; }
        public ArticleLength Gap { get; set; }
        public string Foreground { get; set; }
        public string Background { get; set; }
        public ArticleBackground ColorContext { get; set; }
        public string BorderColor { get; set; }
        public string FontFamily { get; set; }
        public string FontWeight { get; set; }
        public string FontStyle { get; set; }
        public string TextDecoration { get; set; }
        public string VerticalText { get; set; }
        public ArticleLength FontSize { get; set; }
        public ArticleLength Width { get; set; }
        public ArticleLength Height { get; set; }
        public ArticleLength MaxWidth { get; set; }
        public ArticleLength BorderWidth { get; set; }
        public ArticleEdges Margin { get; } = new ArticleEdges();
        public ArticleEdges Padding { get; } = new ArticleEdges();
        public int ColumnCount { get; set; } = 1;
        public bool Hidden { get; set; }
        public bool Collapsible { get; set; }
        public bool InitiallyCollapsed { get; set; }
        public bool IsInfobox { get; set; }
        public bool IsThumbnail { get; set; }
        public bool PreserveWhitespace { get; set; }
        public Dictionary<string, string> Declarations { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, string> Variables { get; internal set; } = new Dictionary<string, string>(StringComparer.Ordinal);
    }
}
