using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using HtmlAgilityPack;
using WikiViewer.Core.Models;

namespace WikiViewer.Core.Services
{
    /// <summary>A deliberately limited stylesheet reader: tag/class/id descendant selectors, no active content.</summary>
    public static class ArticleStyleSheet
    {
        public static IEnumerable<ArticleCssRule> Parse(string css)
        {
            css = Regex.Replace(css ?? "", @"/\*[\s\S]*?\*/", "");
            int index = 0, emitted = 0;
            while (index < css.Length && emitted < 2000)
            {
                int open = css.IndexOf('{', index);
                if (open < 0) yield break;
                string selector = css.Substring(index, open - index).Trim();
                int close = open + 1, depth = 1;
                char quote = '\0';
                for (; close < css.Length; close++)
                {
                    char c = css[close];
                    if ((c == '\'' || c == '"') && (close == 0 || css[close - 1] != '\\')) { if (quote == '\0') quote = c; else if (quote == c) quote = '\0'; }
                    if (quote != '\0') continue;
                    if (c == '{') depth++;
                    if (c == '}' && --depth == 0) break;
                }
                if (close >= css.Length) yield break;
                string declarations = css.Substring(open + 1, close - open - 1);
                index = close + 1;
                // Conditional/nested CSS requires browser layout and must not be applied unconditionally.
                if (selector.StartsWith("@") || declarations.Contains("{")) continue;
                foreach (string part in selector.Split(','))
                {
                    if (emitted >= 2000) yield break;
                    string simple = part.Trim();
                    if (!Regex.IsMatch(simple, @"^[\w.*#\-\s]+$") || simple.Length > 500) continue;
                    emitted++;
                    yield return new ArticleCssRule { Selector = simple, Declarations = declarations,
                        Specificity = simple.Count(c => c == '#') * 100 + simple.Count(c => c == '.') * 10 + Regex.Matches(simple, @"(?:^|\s)[a-zA-Z]").Count };
                }
            }
        }

        internal static bool Matches(ArticleCssRule rule, HtmlNode element)
        {
            var parts = rule.Selector.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0 || !MatchesPart(parts[parts.Length - 1], element)) return false;
            var ancestor = element.ParentNode;
            for (int i = parts.Length - 2; i >= 0; i--)
            {
                while (ancestor != null && !MatchesPart(parts[i], ancestor)) ancestor = ancestor.ParentNode;
                if (ancestor == null) return false;
                ancestor = ancestor.ParentNode;
            }
            return true;
        }

        private static bool MatchesPart(string selector, HtmlNode element)
        {
            var tokens = Regex.Matches(selector, @"([.#]?)([\w*-]+)");
            if (tokens.Count == 0) return false;
            string[] classes = element.GetAttributeValue("class", "").Split(new[] { ' ', '\t', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (Match token in tokens)
            {
                string prefix = token.Groups[1].Value, value = token.Groups[2].Value;
                if (prefix == "." && !classes.Contains(value, StringComparer.Ordinal)) return false;
                if (prefix == "#" && element.GetAttributeValue("id", "") != value) return false;
                if (prefix == "" && value != "*" && !element.Name.Equals(value, StringComparison.OrdinalIgnoreCase)) return false;
            }
            return true;
        }
    }
}
