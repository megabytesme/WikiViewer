using System;
using System.Threading.Tasks;

namespace WikiViewer.Core.Services
{
    /// <summary>Presentation preferences supplied by the client, independent of its UI framework.</summary>
    public static class ArticlePresentation
    {
        public static Func<Task<string>> GetThemeCssAsync { get; set; } = () => Task.FromResult("");
        public static Func<bool> IsDarkMode { get; set; } = () => false;
    }
}
