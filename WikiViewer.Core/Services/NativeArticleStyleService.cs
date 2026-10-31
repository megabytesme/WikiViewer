using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using HtmlAgilityPack;
using WikiViewer.Core.Models;

namespace WikiViewer.Core.Services
{
    /// <summary>Retains inline declarations and maps a bounded CSS/MediaWiki class subset to native properties.</summary>
    public static class NativeArticleStyleService
    {
        internal static NativeArticleStyle Read(HtmlNode element, IEnumerable<ArticleCssRule> rules = null, NativeArticleStyle parent = null)
        {
            var style = new NativeArticleStyle();
            // Most nodes add no custom properties. Share their already-computed inherited map.
            if (parent != null) style.Variables = parent.Variables;
            var classes = (element.GetAttributeValue("class", "") ?? "").Split(new[] { ' ', '\t', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            Func<string, bool> has = name => classes.Contains(name, StringComparer.OrdinalIgnoreCase);
            style.IsInfobox = has("infobox");
            style.IsThumbnail = has("thumb") || has("thumbinner") || element.Name == "figure";
            bool thumbnailFrame = has("thumb") || element.GetAttributeValue("typeof", "").Contains("mw:File/Thumb");
            if (thumbnailFrame)
            {
                style.Background = "#f8f9fa"; style.Foreground = "#202122"; style.BorderColor = "#a2a9b1"; style.BorderWidth = ArticleLength.Parse("1px");
                SetEdges(style.Padding, "4px"); SetEdges(style.Margin, "0 0 1em 1em");
                style.Float = "right";
            }
            if (style.IsInfobox) { style.Float = "right"; style.Width = ArticleLength.Parse("22em"); style.Background = "#f8f9fa"; style.Foreground = "#202122"; style.BorderColor = "#a2a9b1"; style.BorderWidth = ArticleLength.Parse("1px"); SetEdges(style.Margin, "0 0 1em 1em"); }
            if (has("wikitable")) { style.Background = "#f8f9fa"; style.Foreground = "#202122"; style.BorderColor = "#a2a9b1"; style.BorderWidth = ArticleLength.Parse("1px"); }
            if (has("tright") || has("floatright") || has("mw-halign-right")) { style.Float = "right"; style.PositionExplicit = true; }
            if (has("tleft") || has("floatleft") || has("mw-halign-left")) { style.Float = "left"; style.PositionExplicit = true; }
            if (has("center") || has("tcenter") || has("mw-halign-center")) { style.Alignment = "center"; style.Float = null; style.PositionExplicit = true; }
            if (has("mw-halign-none")) { style.Float = null; style.PositionExplicit = true; }
            if (has("mw-collapsible") || has("collapsible")) style.Collapsible = true;
            style.InitiallyCollapsed = has("mw-collapsed") || has("collapsed");
            style.Hidden = has("noprint") && has("mw-editsection") || has("mw-editsection") || has("shortdescription");
            if (has("references") || has("reflist")) style.FontSize = ArticleLength.Parse("90%");
            if (element.Name == "small") style.FontSize = ArticleLength.Parse("85%");
            if (element.Name == "big") style.FontSize = ArticleLength.Parse("120%");
            if (element.Name == "sup" || element.Name == "sub") style.VerticalText = element.Name;
            if (element.Name == "dt" || element.Name == "th") style.FontWeight = "bold";
            if (element.Name == "b" || element.Name == "strong") style.FontWeight = "bold";
            if (element.Name == "i" || element.Name == "em" || element.Name == "cite") style.FontStyle = "italic";
            if (element.Name == "th") { style.Background = "#eaecf0"; style.Foreground = "#202122"; }
            if (element.Name == "s" || element.Name == "strike" || element.Name == "del") style.TextDecoration = "line-through";
            style.Alignment = element.GetAttributeValue("align", style.Alignment);
            string direction = element.GetAttributeValue("dir", "").ToLowerInvariant();
            if (direction == "rtl" || direction == "ltr") style.Direction = direction;
            style.VerticalAlignment = element.GetAttributeValue("valign", style.VerticalAlignment);
            style.Width = ArticleLength.Parse(element.GetAttributeValue("width", "")) ?? style.Width;
            style.Height = ArticleLength.Parse(element.GetAttributeValue("height", ""));
            style.Background = element.GetAttributeValue("bgcolor", style.Background);
            style.Foreground = element.GetAttributeValue("color", style.Foreground);
            var declarations = (rules ?? new ArticleCssRule[0]).Where(rule => ArticleStyleSheet.Matches(rule, element))
                .OrderBy(rule => rule.Specificity).SelectMany(rule => rule.Values ?? ArticleStyleSheet.ReadDeclarations(rule.Declarations))
                .Concat(ArticleStyleSheet.ReadDeclarations(element.GetAttributeValue("style", ""))).OrderBy(d => d.Important).ToList();
            var custom = declarations.Where(d => d.Name.StartsWith("--")).ToList();
            if (custom.Count > 0)
            {
                var variables = new Dictionary<string, string>(style.Variables, StringComparer.Ordinal);
                foreach (var declaration in custom) variables[declaration.Name] = declaration.Value;
                style.Variables = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var variable in variables)
                {
                    var value = ResolveVariables(variable.Value, variables);
                    if (value != null) style.Variables[variable.Key] = value;
                }
            }
            foreach (var declaration in declarations.Where(d => !d.Name.StartsWith("--")))
            {
                string key = declaration.Name;
                string value = ResolveVariables(declaration.Value, style.Variables);
                if (value == null) continue;
                style.Declarations[key] = value;
                switch (key)
                {
                    case "width": style.Width = ArticleLength.Parse(value); break;
                    case "height": style.Height = ArticleLength.Parse(value); break;
                    case "max-width": style.MaxWidth = ArticleLength.Parse(value); break;
                    case "min-width": style.MinWidth = ArticleLength.Parse(value); break;
                    case "text-align": style.Alignment = value.ToLowerInvariant(); break;
                    case "direction": if (value == "rtl" || value == "ltr") style.Direction = value; break;
                    case "vertical-align": style.VerticalAlignment = value.ToLowerInvariant(); if (value == "super" || value == "sub") style.VerticalText = value == "super" ? "sup" : "sub"; break;
                    case "float": style.Float = value == "left" || value == "right" ? value : null; style.PositionExplicit = true; break;
                    case "clear": style.Clear = value; break;
                    case "color": style.Foreground = value; break;
                    case "background-color": style.Background = value; break;
                    case "background": if (ArticleColor.Parse(value).HasValue) style.Background = value; break;
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
                    case "display": style.Display = value; style.Hidden = value == "none"; break;
                    case "position": style.Position = value; break;
                    case "left": style.Left = ArticleLength.Parse(value, true); break;
                    case "top": style.Top = ArticleLength.Parse(value, true); break;
                    case "right": style.Right = ArticleLength.Parse(value, true); break;
                    case "bottom": style.Bottom = ArticleLength.Parse(value, true); break;
                    case "z-index": int z; if (int.TryParse(value, out z)) style.ZIndex = Math.Max(-10000, Math.Min(10000, z)); break;
                    case "transform":
                        var translate = Regex.Match(value, @"^translate\(\s*([^,]+),\s*([^\)]+)\)$");
                        if (translate.Success) { style.TranslateX = ArticleLength.Parse(translate.Groups[1].Value, true); style.TranslateY = ArticleLength.Parse(translate.Groups[2].Value, true); }
                        break;
                    case "flex-wrap": style.Wrap = value == "wrap" || value == "wrap-reverse"; break;
                    case "flex-direction": style.FlexDirection = value; break;
                    case "flex-flow":
                        foreach (var token in value.Split(' '))
                        {
                            if (token == "row" || token == "column") style.FlexDirection = token;
                            if (token == "wrap" || token == "wrap-reverse") style.Wrap = true;
                            if (token == "nowrap") style.Wrap = false;
                        }
                        break;
                    case "flex-grow": style.FlexGrow = FlexNumber(value, 0); break;
                    case "flex-shrink": style.FlexShrink = FlexNumber(value, 1); break;
                    case "flex-basis": style.FlexBasis = ArticleLength.Parse(value); break;
                    case "flex":
                        var flex = value.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                        style.FlexGrow = value == "auto" ? 1 : FlexNumber(flex.FirstOrDefault(), 0);
                        style.FlexShrink = value == "none" ? 0 : flex.Length > 1 ? FlexNumber(flex[1], 1) : 1;
                        style.FlexBasis = ArticleLength.Parse(flex.LastOrDefault());
                        if (flex.Length == 1 && value != "none" && value != "auto") style.FlexBasis = ArticleLength.Parse("0px");
                        break;
                    case "justify-content": style.JustifyContent = value; break;
                    case "gap": case "grid-gap": style.Gap = ArticleLength.Parse(value); break;
                    case "grid-template-columns":
                        var auto = Regex.Match(value, @"^repeat\(\s*auto-(?:fill|fit)\s*,\s*minmax\(\s*([^,]+),\s*1fr\s*\)\s*\)$");
                        if (auto.Success) style.GridMinimum = ArticleLength.Parse(auto.Groups[1].Value);
                        var repeat = Regex.Match(value, @"^repeat\(\s*(\d+)\s*,");
                        int count;
                        if (repeat.Success && int.TryParse(repeat.Groups[1].Value, out count)) style.GridColumns = Math.Max(1, Math.Min(12, count));
                        else if (Regex.IsMatch(value, @"^(?:[\d.]+(?:fr|px|%|em)|auto)(?:\s+(?:[\d.]+(?:fr|px|%|em)|auto))*$"))
                            style.GridColumns = Math.Min(12, value.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Length);
                        break;
                    case "white-space": style.PreserveWhitespace = value == "pre" || value == "pre-wrap"; break;
                    default:
                        if (key.StartsWith("padding-")) SetEdge(style.Padding, key.Substring(8), value);
                        if (key.StartsWith("margin-")) SetEdge(style.Margin, key.Substring(7), value);
                        break;
                }
            }
            if (style.Background != null && style.Foreground == null && parent?.Foreground == null)
            {
                uint? color = ArticleColor.Parse(style.Background);
                if (color.HasValue && (color.Value >> 24) > 127)
                {
                    uint c = color.Value;
                    double light = ((c >> 16) & 255) * 0.299 + ((c >> 8) & 255) * 0.587 + (c & 255) * 0.114;
                    style.Foreground = light > 140 ? "#202122" : "#ffffff";
                }
            }
            return style;
        }

        private static double FlexNumber(string value, double fallback)
        {
            double number;
            return double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out number) &&
                number >= 0 && number <= 1000 ? number : fallback;
        }

        private static string ResolveVariables(string value, Dictionary<string, string> variables)
        {
            // Bound substitution, including cycles and nested fallback values.
            for (int step = 0; step < 24 && value.Contains("var("); step++)
            {
                int start = value.LastIndexOf("var(", StringComparison.Ordinal), end = value.IndexOf(')', start);
                if (end < 0) return null;
                string expression = value.Substring(start + 4, end - start - 4);
                int comma = expression.IndexOf(',');
                string name = (comma < 0 ? expression : expression.Substring(0, comma)).Trim(), replacement;
                if (!variables.TryGetValue(name, out replacement)) replacement = comma < 0 ? null : expression.Substring(comma + 1).Trim();
                if (replacement == null) return null;
                value = value.Substring(0, start) + replacement + value.Substring(end + 1);
                if (value.Length > 16000) return null;
            }
            return value.Contains("var(") ? null : value;
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
