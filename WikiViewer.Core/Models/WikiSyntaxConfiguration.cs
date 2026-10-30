using System.Collections.Generic;

namespace WikiViewer.Core.Models
{
    public sealed class WikiSyntaxConfiguration
    {
        public string Language { get; set; }
        public string LinkTrailPattern { get; set; } = "[a-z]+";
        public bool UsesFallbackLinkTrail { get; set; }
        public List<string> FileNamespaces { get; set; } = new List<string> { "File", "Image" };
        public List<string> CategoryNamespaces { get; set; } = new List<string> { "Category" };
        public List<string> RedirectAliases { get; set; } = new List<string> { "#REDIRECT" };
        public Dictionary<string, string> ImageOptions { get; set; } = new Dictionary<string, string>();
    }
}
