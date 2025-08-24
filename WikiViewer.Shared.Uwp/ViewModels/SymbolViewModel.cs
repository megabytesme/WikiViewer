namespace WikiViewer.Shared.Uwp.ViewModels
{
    public class SymbolViewModel
    {
        public string Character { get; set; }
        public string Entity { get; set; }
        public string Tooltip => Entity;
    }
}