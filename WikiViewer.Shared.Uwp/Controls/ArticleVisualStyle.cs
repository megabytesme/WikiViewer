using System;
using WikiViewer.Core.Models;
using WikiViewer.Core.Services;
using Windows.UI;
using Windows.UI.Text;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Documents;
using Windows.UI.Xaml.Media;

namespace WikiViewer.Shared.Uwp.Controls
{
    internal static class ArticleVisualStyle
    {
        internal static SolidColorBrush Brush(string value)
        {
            uint? parsed = ArticleColor.Parse(value);
            if (!parsed.HasValue) return null;
            uint color = parsed.Value;
            return new SolidColorBrush(Color.FromArgb((byte)(color >> 24), (byte)(color >> 16), (byte)(color >> 8), (byte)color));
        }

        internal static Thickness Edges(ArticleEdges edges, Thickness defaults)
        {
            return new Thickness(edges.Left?.Resolve(800) ?? defaults.Left, edges.Top?.Resolve(800) ?? defaults.Top,
                edges.Right?.Resolve(800) ?? defaults.Right, edges.Bottom?.Resolve(800) ?? defaults.Bottom);
        }

        internal static FrameworkElement Block(FrameworkElement content, NativeArticleNode node)
        {
            var style = node.Style;
            var padding = Edges(style.Padding, new Thickness(0));
            double border = style.BorderWidth?.Resolve(800) ?? 0;
            bool decorated = border > 0 || Brush(style.Background) != null || padding.Left + padding.Right + padding.Top + padding.Bottom > 0;
            FrameworkElement result = content;
            if (decorated)
            {
                result = new Border { Child = content, Padding = padding, Background = Brush(style.Background),
                    BorderThickness = new Thickness(Math.Min(12, border)), BorderBrush = Brush(style.BorderColor) ?? new SolidColorBrush(Colors.Gray) };
                if (content is RichTextBlock) { result.Margin = content.Margin; content.Margin = new Thickness(0); }
            }
            result.Margin = Edges(style.Margin, result.Margin);
            result.FlowDirection = node.RightToLeft ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
            if (style.Width != null && style.Width.Unit != "%") result.Width = Math.Max(1, style.Width.Resolve(800));
            if (style.Height != null && node.Kind != NativeArticleNodeKind.Image) result.MinHeight = Math.Max(0, Math.Min(1200, style.Height.Resolve(800)));
            if (style.Width != null && style.Alignment == "center") result.HorizontalAlignment = HorizontalAlignment.Center;
            else if (style.Width != null && style.Alignment == "right" && style.Float == null) result.HorizontalAlignment = HorizontalAlignment.Right;
            ArticleFlowPanel.SetLayoutStyle(result, style);
            return result;
        }

        internal static TextAlignment Alignment(string value, bool rtl)
        {
            switch (value)
            {
                case "center": return TextAlignment.Center;
                case "justify": return TextAlignment.Justify;
                case "right": return TextAlignment.Right;
                case "end": return rtl ? TextAlignment.Left : TextAlignment.Right;
                case "start": return rtl ? TextAlignment.Right : TextAlignment.Left;
                default: return rtl ? TextAlignment.Right : TextAlignment.Left;
            }
        }

        internal static void Text(RichTextBlock text, NativeArticleNode node)
        {
            var style = node.Style;
            text.TextAlignment = Alignment(style.Alignment, node.RightToLeft);
            var foreground = Brush(style.Foreground);
            if (foreground != null) text.Foreground = foreground;
            if (style.FontSize != null) text.FontSize = Math.Max(6, Math.Min(120, style.FontSize.Resolve(18)));
            if (style.FontFamily != null) text.FontFamily = new FontFamily(style.FontFamily);
            if (style.FontWeight == "bold" || style.FontWeight == "600" || style.FontWeight == "700") text.FontWeight = FontWeights.Bold;
            if (style.FontWeight == "normal" || style.FontWeight == "400") text.FontWeight = FontWeights.Normal;
            if (style.FontStyle == "italic") text.FontStyle = FontStyle.Italic;
        }

        internal static void Inline(Span span, NativeArticleNode node)
        {
            var style = node.Style;
            var foreground = Brush(style.Foreground);
            if (foreground != null) span.Foreground = foreground;
            if (style.FontSize != null) span.FontSize = Math.Max(6, Math.Min(120, style.FontSize.Resolve(18)));
            if (style.FontWeight == "bold" || style.FontWeight == "600" || style.FontWeight == "700") span.FontWeight = FontWeights.Bold;
            if (style.FontWeight == "normal" || style.FontWeight == "400") span.FontWeight = FontWeights.Normal;
            if (style.FontStyle == "italic") span.FontStyle = FontStyle.Italic;
            if (style.FontFamily != null) span.FontFamily = new FontFamily(style.FontFamily);
            if (style.VerticalText == "sup") Typography.SetVariants(span, FontVariants.Superscript);
            if (style.VerticalText == "sub") Typography.SetVariants(span, FontVariants.Subscript);
#if UWP_1809
            if (style.TextDecoration != null)
            {
                TextDecorations decorations = TextDecorations.None;
                if (style.TextDecoration.Contains("line-through")) decorations |= TextDecorations.Strikethrough;
                if (style.TextDecoration.Contains("underline")) decorations |= TextDecorations.Underline;
                span.TextDecorations = decorations;
            }
#endif
        }
    }
}
