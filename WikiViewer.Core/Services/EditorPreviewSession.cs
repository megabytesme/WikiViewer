using System;
using WikiViewer.Core.Models;

namespace WikiViewer.Core.Services
{
    /// <summary>Keeps an unsaved preview paired with its source and rejects late responses.</summary>
    public sealed class EditorPreviewSession
    {
        private long _request;
        private string _source = "";
        public ArticlePreview Current { get; private set; }
        public bool IsStale => Current != null && !string.Equals(Current.Content.RawWikitext, _source, StringComparison.Ordinal);
        public void SourceChanged(string source) { _source = source ?? ""; }
        public EditorPreviewRequest Begin(string source, string title)
        {
            SourceChanged(source);
            return new EditorPreviewRequest(++_request, _source, title);
        }
        public bool IsLatest(EditorPreviewRequest request) => request.Id == _request;
        public bool Accept(EditorPreviewRequest request, ArticlePreview result)
        {
            if (!IsLatest(request) || !string.Equals(request.Source, _source, StringComparison.Ordinal) ||
                !string.Equals(result?.Content?.RawWikitext, request.Source, StringComparison.Ordinal)) return false;
            Current = result;
            return true;
        }
        public void Clear() { _request++; Current = null; _source = ""; }
    }
}
