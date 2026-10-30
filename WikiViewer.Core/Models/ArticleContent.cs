using System;
using System.Collections.Generic;

namespace WikiViewer.Core.Models
{
    /// <summary>Source and expanded output returned by the same MediaWiki parse operation.</summary>
    public sealed class ArticleContent
    {
        public const int CurrentNativeFormatVersion = 2;
        public int NativeFormatVersion { get; set; }
        public string Title { get; set; }
        public long RevisionId { get; set; }
        public string HtmlContent { get; set; }
        public string RawWikitext { get; set; }
        public string HeadHtml { get; set; }
        public WikiSyntaxConfiguration Syntax { get; set; }
        public List<string> StyleModules { get; set; } = new List<string>();
        public List<string> ResolvedStyleSheets { get; set; } = new List<string>();
        public bool HasRawSource => RawWikitext != null;
    }

    public class ArticleCachedEventArgs : EventArgs
    {
        public string PageTitle { get; }
        public ArticleCachedEventArgs(string pageTitle) { PageTitle = pageTitle; }
    }
}
