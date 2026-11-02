using Windows.UI.Xaml.Automation;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;

namespace WikiViewer.Uwp.Controls
{
    internal static class ReaderModeControl
    {
        public static void Update(AppBarToggleButton button)
        {
            bool native = button.IsChecked == true;
            button.Icon = new FontIcon { FontFamily = new FontFamily("Segoe MDL2 Assets"), Glyph = native ? "\uE7C3" : "\uEB41" };
            button.Label = native ? "Page" : "Website";
            AutomationProperties.SetName(button, native ? "Reading mode: Page (native)" : "Reading mode: Website (Web)");
            ToolTipService.SetToolTip(button, native ? "Page mode: native wikitext. Switch to Website mode." : "Website mode: Web view. Switch to Page mode.");
        }
    }
}
