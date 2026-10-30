using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using HtmlAgilityPack;
using WikiViewer.Core.Models;

namespace WikiViewer.Core.Services
{
    /// <summary>Bounded static CSS. Selectors and declarations compile once per document.</summary>
    public static class ArticleStyleSheet
    {
        private static readonly Regex Identifier = new Regex(@"\G[\w-]+", RegexOptions.CultureInvariant);
        private static readonly Regex Attribute = new Regex(@"^\s*([\w-]+)\s*(?:(~=|\|=|\^=|\$=|\*=|=)\s*(?:'([^']*)'|""([^""]*)""|([^\s]+)))?\s*$", RegexOptions.CultureInvariant);

        public static IEnumerable<ArticleCssRule> Parse(string css, double? viewportWidth = null)
        {
            var rules = new List<ArticleCssRule>();
            ParseRules(Regex.Replace(css ?? "", @"/\*[\s\S]*?\*/", ""), viewportWidth, rules, 0);
            return rules;
        }
        private static void ParseRules(string css, double? width, List<ArticleCssRule> rules, int level)
        {
            if (level > 12) return;
            int index = 0;
            while (index < css.Length && rules.Count < 2000)
            {
                int open = css.IndexOf('{', index);
                if (open < 0) return;
                string selector = css.Substring(index, open - index).Trim();
                int close = Closing(css, open, '{', '}');
                if (close < 0) return;
                string declarations = css.Substring(open + 1, close - open - 1);
                index = close + 1;
                if (selector.StartsWith("@media", StringComparison.OrdinalIgnoreCase))
                {
                    if (MediaMatches(selector.Substring(6).Trim(), width)) ParseRules(declarations, width, rules, level + 1);
                    continue;
                }
                if (selector.StartsWith("@") || declarations.Contains("{")) continue;
                var values = ReadDeclarations(declarations);
                foreach (string part in Split(selector, ','))
                {
                    if (rules.Count >= 2000) return;
                    int specificity;
                    var match = Compile(part.Trim(), out specificity);
                    if (match != null) rules.Add(new ArticleCssRule { Selector = part.Trim(), Declarations = declarations,
                        Specificity = specificity, Match = match, Values = values });
                }
            }
        }
        internal static bool Matches(ArticleCssRule rule, HtmlNode element)
        {
            if (rule.Match == null) { int ignored; rule.Match = Compile(rule.Selector, out ignored); }
            return rule.Match != null && rule.Match(element);
        }
        private static bool MediaMatches(string query, double? width)
        {
            if (!width.HasValue) return false;
            foreach (var alternative in Split(query, ','))
            {
                var rest = alternative.Trim().ToLowerInvariant();
                if (rest.StartsWith("screen")) rest = rest.Substring(6).Trim();
                else if (rest.StartsWith("all")) rest = rest.Substring(3).Trim();
                bool matches = true;
                foreach (var condition in rest.Split(new[] { "and" }, StringSplitOptions.RemoveEmptyEntries))
                {
                    var m = Regex.Match(condition.Trim(), @"^\(\s*(min|max)-width\s*:\s*(\d+(?:\.\d+)?)(px|em|rem)\s*\)$");
                    if (!m.Success) { matches = false; break; }
                    double limit = double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture) * (m.Groups[3].Value == "px" ? 1 : 18);
                    if (m.Groups[1].Value == "min" ? width.Value < limit : width.Value > limit) matches = false;
                }
                if (matches && (rest.Length == 0 || rest.Contains("("))) return true;
            }
            return false;
        }
        private static Func<HtmlNode, bool> Compile(string selector, out int specificity)
        {
            specificity = 0;
            if (string.IsNullOrWhiteSpace(selector) || selector.Length > 500) return null;
            int index = 0;
            var compounds = new List<Func<HtmlNode, bool>>();
            var relations = new List<char>();
            while (index < selector.Length)
            {
                int start = index, brackets = 0, parentheses = 0; char quote = '\0';
                while (index < selector.Length)
                {
                    char c = selector[index];
                    if (quote != '\0') { if (c == quote) quote = '\0'; index++; continue; }
                    if (c == '\'' || c == '"') quote = c;
                    if (c == '[') brackets++; if (c == ']') brackets--;
                    if (c == '(') parentheses++; if (c == ')') parentheses--;
                    if (brackets == 0 && parentheses == 0 && (char.IsWhiteSpace(c) || c == '>' || c == '+' || c == '~')) break;
                    index++;
                }
                int score; var compound = Compound(selector.Substring(start, index - start), out score);
                if (compound == null) return null;
                compounds.Add(compound); specificity += score;
                while (index < selector.Length && char.IsWhiteSpace(selector[index])) index++;
                if (index == selector.Length) break;
                char relation = ' ';
                if (selector[index] == '>' || selector[index] == '+' || selector[index] == '~') relation = selector[index++];
                while (index < selector.Length && char.IsWhiteSpace(selector[index])) index++;
                if (index == selector.Length) return null;
                relations.Add(relation);
            }
            return node => MatchChain(node, compounds.Count - 1, compounds, relations);
        }
        private static bool MatchChain(HtmlNode node, int index, List<Func<HtmlNode, bool>> compounds, List<char> relations)
        {
            if (node == null || node.NodeType != HtmlNodeType.Element || !compounds[index](node)) return false;
            if (index == 0) return true;
            char relation = relations[index - 1];
            if (relation == '>') return MatchChain(node.ParentNode, index - 1, compounds, relations);
            if (relation == '+') return MatchChain(Previous(node), index - 1, compounds, relations);
            for (var candidate = relation == '~' ? Previous(node) : node.ParentNode; candidate != null;
                candidate = relation == '~' ? Previous(candidate) : candidate.ParentNode)
                if (MatchChain(candidate, index - 1, compounds, relations)) return true;
            return false;
        }
        private static Func<HtmlNode, bool> Compound(string source, out int specificity)
        {
            specificity = 0;
            var tests = new List<Func<HtmlNode, bool>>(); int index = 0;
            while (index < source.Length)
            {
                char prefix = source[index];
                if (prefix == '*') { index++; tests.Add(n => true); continue; }
                if (prefix == '[')
                {
                    int end = Closing(source, index, '[', ']'); if (end < 0) return null;
                    var attr = Attribute.Match(source.Substring(index + 1, end - index - 1)); if (!attr.Success) return null;
                    string name = attr.Groups[1].Value, op = attr.Groups[2].Value;
                    string value = attr.Groups[3].Success ? attr.Groups[3].Value : attr.Groups[4].Success ? attr.Groups[4].Value : attr.Groups[5].Value;
                    tests.Add(n => n.Attributes.Contains(name) && AttributeMatches(n.GetAttributeValue(name, ""), op, value));
                    specificity += 10; index = end + 1; continue;
                }
                if (prefix == '.' || prefix == '#' || prefix == ':') index++;
                var identifier = Identifier.Match(source, index); if (!identifier.Success) return null;
                string token = identifier.Value; index += token.Length;
                if (prefix == '.') { specificity += 10; tests.Add(n => HasWord(n.GetAttributeValue("class", ""), token)); }
                else if (prefix == '#') { specificity += 100; tests.Add(n => n.GetAttributeValue("id", "") == token); }
                else if (prefix == ':')
                {
                    specificity += 10;
                    if (token == "not")
                    {
                        if (index >= source.Length || source[index] != '(') return null;
                        int end = Closing(source, index, '(', ')'); if (end < 0) return null;
                        int score; var negated = Compound(source.Substring(index + 1, end - index - 1), out score); if (negated == null) return null;
                        specificity += score - 10; tests.Add(n => !negated(n)); index = end + 1;
                    }
                    else if (token == "first-child") tests.Add(n => Previous(n) == null);
                    else if (token == "last-child") tests.Add(n => Next(n) == null);
                    else if (token == "only-child") tests.Add(n => Previous(n) == null && Next(n) == null);
                    else if (token == "empty") tests.Add(n => !n.ChildNodes.Any(c => c.NodeType == HtmlNodeType.Element || c.NodeType == HtmlNodeType.Text && c.InnerText.Length > 0));
                    else if (token == "root") tests.Add(n => n.ParentNode == null || n.ParentNode.NodeType == HtmlNodeType.Document);
                    else if (token == "nth-child")
                    {
                        if (index >= source.Length || source[index] != '(') return null;
                        int end = source.IndexOf(')', index + 1); if (end < 0) return null;
                        var nth = Nth(source.Substring(index + 1, end - index - 1)); if (nth == null) return null;
                        tests.Add(n => { int i = 1; for (var p = Previous(n); p != null; p = Previous(p)) i++; return nth(i); }); index = end + 1;
                    }
                    else return null;
                }
                else { specificity++; tests.Add(n => n.Name.Equals(token, StringComparison.OrdinalIgnoreCase)); }
            }
            return tests.Count == 0 ? (Func<HtmlNode, bool>)null : n => tests.All(test => test(n));
        }
        private static Func<int, bool> Nth(string expression)
        {
            expression = Regex.Replace(expression, @"\s+", "").ToLowerInvariant();
            if (expression == "odd") expression = "2n+1"; if (expression == "even") expression = "2n";
            int exact; if (int.TryParse(expression, out exact)) return i => i == exact;
            var m = Regex.Match(expression, @"^([+-]?\d*)n([+-]\d+)?$"); if (!m.Success) return null;
            string a = m.Groups[1].Value; int step, offset = 0;
            if (a == "" || a == "+") step = 1; else if (a == "-") step = -1; else if (!int.TryParse(a, out step)) return null;
            if (m.Groups[2].Success && !int.TryParse(m.Groups[2].Value, out offset)) return null;
            return i => step == 0 ? i == offset : ((long)i - offset) % step == 0 && ((long)i - offset) / step >= 0;
        }
        private static HtmlNode Previous(HtmlNode n) { do { n = n.PreviousSibling; } while (n != null && n.NodeType != HtmlNodeType.Element); return n; }
        private static HtmlNode Next(HtmlNode n) { do { n = n.NextSibling; } while (n != null && n.NodeType != HtmlNodeType.Element); return n; }
        private static bool AttributeMatches(string actual, string op, string expected)
        {
            switch (op)
            {
                case "": return true; case "=": return actual == expected; case "~=": return HasWord(actual, expected);
                case "|=": return actual == expected || actual.StartsWith(expected + "-", StringComparison.Ordinal);
                case "^=": return expected.Length > 0 && actual.StartsWith(expected, StringComparison.Ordinal);
                case "$=": return expected.Length > 0 && actual.EndsWith(expected, StringComparison.Ordinal);
                case "*=": return expected.Length > 0 && actual.Contains(expected); default: return false;
            }
        }
        private static bool HasWord(string text, string word)
        {
            for (int start = 0; start < text.Length;)
            {
                while (start < text.Length && char.IsWhiteSpace(text[start])) start++;
                int end = start; while (end < text.Length && !char.IsWhiteSpace(text[end])) end++;
                if (end - start == word.Length && string.CompareOrdinal(text, start, word, 0, word.Length) == 0) return true;
                start = end;
            }
            return false;
        }
        private static int Closing(string source, int start, char open, char close)
        {
            int depth = 0; char quote = '\0';
            for (int i = start; i < source.Length; i++)
            {
                char c = source[i]; if (c == '\\') { i++; continue; }
                if (quote != '\0') { if (c == quote) quote = '\0'; continue; }
                if (c == '\'' || c == '"') { quote = c; continue; }
                if (c == open) depth++; if (c == close && --depth == 0) return i;
            }
            return -1;
        }
        internal static IEnumerable<string> Split(string source, char separator)
        {
            int start = 0, depth = 0; char quote = '\0';
            for (int i = 0; i < source.Length; i++)
            {
                char c = source[i]; if (c == '\\') { i++; continue; }
                if (quote != '\0') { if (c == quote) quote = '\0'; continue; }
                if (c == '\'' || c == '"') quote = c;
                if (c == '(' || c == '[') depth++; if (c == ')' || c == ']') depth--;
                if (c == separator && depth == 0 && quote == '\0') { yield return source.Substring(start, i - start); start = i + 1; }
            }
            yield return source.Substring(start);
        }
        internal static List<ArticleCssDeclaration> ReadDeclarations(string source)
        {
            var result = new List<ArticleCssDeclaration>();
            foreach (string declaration in Split(source ?? "", ';'))
            {
                int colon = declaration.IndexOf(':'); if (colon <= 0) continue;
                string value = declaration.Substring(colon + 1).Trim();
                bool important = Regex.IsMatch(value, @"\s*!important\s*$", RegexOptions.IgnoreCase);
                string name = declaration.Substring(0, colon).Trim();
                result.Add(new ArticleCssDeclaration { Name = name.StartsWith("--") ? name : name.ToLowerInvariant(),
                    Value = important ? Regex.Replace(value, @"\s*!important\s*$", "", RegexOptions.IgnoreCase) : value, Important = important });
            }
            return result;
        }
    }
}
