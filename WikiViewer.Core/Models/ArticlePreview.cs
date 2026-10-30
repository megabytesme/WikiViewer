using System.Collections.Generic;

namespace WikiViewer.Core.Models
{
    public sealed class ArticlePreview
    {
        public ArticleContent Content { get; set; }
        public string HeadHtml { get; set; }
        public string WebHtml { get; set; }
        public List<string> StyleModules { get; } = new List<string>();
    }

    public sealed class EditorPreviewRequest
    {
        internal EditorPreviewRequest(long id, string source, string title) { Id = id; Source = source; Title = title; }
        public long Id { get; }
        public string Source { get; }
        public string Title { get; }
    }
}
