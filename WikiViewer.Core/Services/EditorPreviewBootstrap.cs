using System;
using WikiViewer.Core.Models;

namespace WikiViewer.Core.Services
{
    /// <summary>Hands the displayed source/expansion pair to the next editor without serializing it into navigation state.</summary>
    public static class EditorPreviewBootstrap
    {
        private static readonly object Gate = new object();
        private static Guid _wiki;
        private static string _title;
        private static ArticleContent _content;

        public static void Store(Guid wiki, string title, ArticleContent content)
        {
            lock (Gate) { _wiki = wiki; _title = title; _content = content?.Copy(); }
        }

        public static ArticleContent Take(Guid wiki, string title)
        {
            lock (Gate)
            {
                var result = wiki == _wiki && string.Equals(title, _title, StringComparison.Ordinal) ? _content : null;
                _content = null; _title = null;
                return result;
            }
        }

        public static ArticlePreview Create(ArticleContent published, string fetchedSource, string editorSource)
        {
            // Only reuse an expansion paired with the freshly retrieved source. The editor
            // may normalize line endings or add its terminal paragraph marker on SetText.
            if (published?.RawWikitext == null || published.HtmlContent == null ||
                !string.Equals(published.RawWikitext, fetchedSource, StringComparison.Ordinal)) return null;
            var content = published.Copy();
            content.RawWikitext = editorSource; content.RevisionId = 0;
            var result = new ArticlePreview { Content = content, HeadHtml = published.HeadHtml, NeedsServerExpansion = true };
            result.StyleModules.AddRange(content.StyleModules);
            return result;
        }
    }
}
