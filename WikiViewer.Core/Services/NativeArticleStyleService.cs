using System;
using System.Linq;
using System.Text.RegularExpressions;
using HtmlAgilityPack;
using WikiViewer.Core.Models;

namespace WikiViewer.Core.Services
{
    /// <summary>Retains inline declarations and maps a bounded CSS/MediaWiki class subset to native properties.</summary>
    public static class NativeArticleStyleService
    {
        internal static NativeArticleStyle Read(HtmlNode element)
        {
            var style = new NativeArticleStyle();
            var classes = (element.GetAttributeValue("class", "") ?? "").Split(new[] { ' ', '\t', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            Func<string, bool> has = name => classes.Contains(name, StringComparer.OrdinalIgnoreCase);
            style.IsInfobox = has("infobox");
            style.IsThumbnail = has("thumb") || has("thumbinner") || element.Name == "figure";
            if (style.IsInfobox) { style.Float = "right"; style.Width = ArticleLength.Parse("22em"); style.Background = "#f8f9fa"; style.Foreground = "#202122"; style.BorderColor = "#a2a9b1"; style.BorderWidth = ArticleLength.Parse("1px"); SetEdges(style.Margin, "0 0 1em 1em"); }
            if (has("wikitable")) { style.Background = "#f8f9fa"; style.Foreground = "#202122"; style.BorderColor = "#a2a9b1"; style.BorderWidth = ArticleLength.Parse("1px"); }
            if (has("tright") || has("floatright") || has("mw-halign-right")) style.Float = "right";
            if (has("tleft") || has("floatleft") || has("mw-halign-left")) style.Float = "left";
            if (has("center") || has("tcenter") || has("mw-halign-center")) style.Alignment = "center";
            if (has("mw-halign-none")) style.Float = null;
            if (has("mw-collapsible") || has("collapsible")) style.Collapsible = true;
            style.InitiallyCollapsed = has("mw-collapsed") || has("collapsed");
            style.Hidden = has("noprint") && has("mw-editsection") || has("mw-editsection") || has("shortdescription");
            if (has("references") || has("reflist")) style.FontSize = ArticleLength.Parse("90%");
            if (element.Name == "small") style.FontSize = ArticleLength.Parse("85%");
            if (element.Name == "big") style.FontSize = ArticleLength.Parse("120%");
            if (element.Name == "sup" || element.Name == "sub") style.VerticalText = element.Name;
            if (element.Name == "dt" || element.Name == "th") style.FontWeight = "bold";
            if (element.Name == "th") { style.Background = "#eaecf0"; style.Foreground = "#202122"; }
            if (element.Name == "s" || element.Name == "strike" || element.Name == "del") style.TextDecoration = "line-through";
            style.Alignment = element.GetAttributeValue("align", style.Alignment);
            style.VerticalAlignment = element.GetAttributeValue("valign", style.VerticalAlignment);
            style.Width = ArticleLength.Parse(element.GetAttributeValue("width", "")) ?? style.Width;
            style.Height = ArticleLength.Parse(element.GetAttributeValue("height", ""));
            style.Background = element.GetAttributeValue("bgcolor", style.Background);
            style.Foreground = element.GetAttributeValue("color", style.Foreground);
            foreach (string declaration in (element.GetAttributeValue("style", "") ?? "").Split(';'))
            {
                int colon = declaration.IndexOf(':');
                if (colon < 0) continue;
                string key = declaration.Substring(0, colon).Trim().ToLowerInvariant();
                string value = Regex.Replace(declaration.Substring(colon + 1).Trim(), @"\s*!important\s*$", "", RegexOptions.IgnoreCase);
                style.Declarations[key] = value;
                switch (key)
                {
                    case "width": style.Width = ArticleLength.Parse(value); break;
                    case "height": style.Height = ArticleLength.Parse(value); break;
                    case "max-width": style.MaxWidth = ArticleLength.Parse(value); break;
                    case "text-align": style.Alignment = value.ToLowerInvariant(); break;
                    case "vertical-align": style.VerticalAlignment = value.ToLowerInvariant(); break;
                    case "float": style.Float = value == "left" || value == "right" ? value : null; break;
                    case "clear": style.Clear = value; break;
                    case "color": style.Foreground = value; break;
                    case "background-color": style.Background = value; break;
                    case "background": if (!value.Contains("(") && !value.Contains(" ")) style.Background = value; break;
                    case "font-size": style.FontSize = ArticleLength.Parse(value); break;
                    case "font-weight": style.FontWeight = value; break;
                    case "font-style": style.FontStyle = value; break;
                    case "font-family": if (value.IndexOf("mono", StringComparison.OrdinalIgnoreCase) >= 0) style.FontFamily = "Consolas"; break;
                    case "text-decoration": style.TextDecoration = value; break;
                    case "border":
                        var parts = value.Split(' ');
                        style.BorderWidth = parts.Select(v => ArticleLength.Parse(v)).FirstOrDefault(v => v != null);
                        style.BorderColor = parts.LastOrDefault(v => v.StartsWith("#")) ?? "#a2a9b1";
                        if (value == "none" || value == "0") style.BorderWidth = ArticleLength.Parse("0");
                        break;
                    case "border-width": style.BorderWidth = ArticleLength.Parse(value); break;
                    case "border-color": style.BorderColor = value; break;
                    case "padding": SetEdges(style.Padding, value); break;
                    case "margin": SetEdges(style.Margin, value); if (value.Contains("auto")) style.Alignment = "center"; break;
                    case "column-count": case "-moz-column-count": case "-webkit-column-count":
                        int columns; if (int.TryParse(value, out columns)) style.ColumnCount = Math.Max(1, Math.Min(4, columns)); break;
                    case "display": if (value == "none") style.Hidden = true; break;
                    case "white-space": style.PreserveWhitespace = value == "pre" || value == "pre-wrap"; break;
                    default:
                        if (key.StartsWith("padding-")) SetEdge(style.Padding, key.Substring(8), value);
                        if (key.StartsWith("margin-")) SetEdge(style.Margin, key.Substring(7), value);
                        break;
                }
            }
            return style;
        }

        private static void SetEdge(ArticleEdges edges, string edge, string value)
        {
            var length = ArticleLength.Parse(value);
            switch (edge) { case "top": edges.Top = length; break; case "right": edges.Right = length; break; case "bottom": edges.Bottom = length; break; case "left": edges.Left = length; break; }
        }

        private static void SetEdges(ArticleEdges edges, string value)
        {
            var values = value.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (values.Length == 0 || values.Length > 4) return;
            edges.Top = ArticleLength.Parse(values[0]);
            edges.Right = ArticleLength.Parse(values.Length > 1 ? values[1] : values[0]);
            edges.Bottom = ArticleLength.Parse(values.Length > 2 ? values[2] : values[0]);
            edges.Left = ArticleLength.Parse(values.Length > 3 ? values[3] : values.Length > 1 ? values[1] : values[0]);
        }
    }
}
