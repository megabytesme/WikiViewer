using System;

namespace WikiViewer.Shared.Uwp.ViewModels
{
    public class SearchSuggestionViewModel
    {
        public string Title { get; set; }
        public string WikiName { get; set; }
        public string IconUrl { get; set; }
        public Guid WikiId { get; set; }
    }
}