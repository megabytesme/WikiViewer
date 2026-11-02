using System;
using System.Collections.Generic;
using WikiViewer.Core.Models;

namespace WikiViewer.Core.Services
{
    /// <summary>A conservative local edit: update one unambiguous text run without re-expanding a page.</summary>
    public sealed class NativeDraftTextPatch
    {
        public NativeArticleNode Node { get; private set; }
        public string Text { get; private set; }
        public string Source { get; private set; }
        private const string Syntax = "[]{}<>|'=\r\n#*;~&";

        public static NativeDraftTextPatch TryCreate(NativeArticleDocument document, string source)
        {
            string before = document?.RawWikitext;
            if (before == null || source == null || before == source) return null;
            int start = 0, limit = Math.Min(before.Length, source.Length);
            while (start < limit && before[start] == source[start]) start++;
            int oldEnd = before.Length, newEnd = source.Length;
            while (oldEnd > start && newEnd > start && before[oldEnd - 1] == source[newEnd - 1]) { oldEnd--; newEnd--; }
            string removed = before.Substring(start, oldEnd - start), inserted = source.Substring(start, newEnd - start);
            if (removed.Length > 4096 || inserted.Length > 4096 || ContainsSyntax(removed) || ContainsSyntax(inserted)) return null;
            int templateDepth = 0, linkDepth = 0;
            bool linkLabel = false;
            for (int i = 0; i < start; i++)
            {
                if (i + 1 < start && before[i] == '{' && before[i + 1] == '{') { templateDepth++; i++; }
                else if (i + 1 < start && before[i] == '}' && before[i + 1] == '}') { templateDepth = Math.Max(0, templateDepth - 1); i++; }
                else if (i + 1 < start && before[i] == '[' && before[i + 1] == '[') { linkDepth++; linkLabel = false; i++; }
                else if (i + 1 < start && before[i] == ']' && before[i + 1] == ']') { linkDepth = Math.Max(0, linkDepth - 1); i++; }
                else if (linkDepth > 0 && before[i] == '|') linkLabel = true;
            }
            if (templateDepth > 0 || linkDepth > 0 && !linkLabel ||
                start > 0 && before.LastIndexOf('<', start - 1) > before.LastIndexOf('>', start - 1)) return null;
            // Source punctuation delimits context, so links, templates and encoded entities cannot
            // accidentally become literal text. Structural edits use the grouped local/server path.
            int left = start, right = oldEnd;
            while (left > 0 && start - left < 48 && Syntax.IndexOf(before[left - 1]) < 0) left--;
            while (right < before.Length && right - oldEnd < 48 && Syntax.IndexOf(before[right]) < 0) right++;
            string prefix = before.Substring(left, start - left), suffix = before.Substring(oldEnd, right - oldEnd);
            if (prefix.Length + suffix.Length < 8) return null;
            string needle = prefix + removed + suffix;
            NativeArticleNode match = null; int matchAt = 0;
            foreach (var node in TextNodes(document.Nodes))
            {
                int at = (node.Text ?? "").IndexOf(needle, StringComparison.Ordinal);
                if (at < 0) continue;
                if (match != null || node.Text.IndexOf(needle, at + 1, StringComparison.Ordinal) >= 0) return null;
                match = node; matchAt = at + prefix.Length;
            }
            if (match == null) return null;
            return new NativeDraftTextPatch { Node = match, Text = match.Text.Remove(matchAt, removed.Length).Insert(matchAt, inserted), Source = source };
        }

        private static bool ContainsSyntax(string text)
        { foreach (char c in text) if (Syntax.IndexOf(c) >= 0) return true; return false; }

        private static IEnumerable<NativeArticleNode> TextNodes(IEnumerable<NativeArticleNode> nodes)
        {
            foreach (var node in nodes)
            {
                if (node.Kind == NativeArticleNodeKind.Text) yield return node;
                // A local text change must not rewrite CSS, mathematical source or a template's
                // independently expanded output merely because its text happens to look similar.
                if (node.Kind == NativeArticleNodeKind.Math || node.Kind == NativeArticleNodeKind.Media || node.Kind == NativeArticleNodeKind.Image ||
                    node.Kind == NativeArticleNodeKind.Heading || node.Kind == NativeArticleNodeKind.Link) continue;
                foreach (var child in TextNodes(node.Children)) yield return child;
            }
        }
    }
}
