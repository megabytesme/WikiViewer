using System;

namespace WikiViewer.Core.Models
{
    /// <summary>Source and expanded output returned by the same MediaWiki parse operation.</summary>
    public sealed class ArticleContent
    {
        public string Title { get; set; }
        public long RevisionId { get; set; }
        public string HtmlContent { get; set; }
        public string RawWikitext { get; set; }
        public bool HasRawSource => RawWikitext != null;
    }

    public class ArticleCachedEventArgs : EventArgs
    {
        public string PageTitle { get; }
        public ArticleCachedEventArgs(string pageTitle) { PageTitle = pageTitle; }
    }
}
