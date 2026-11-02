using System;
using WikiViewer.Core;
using Windows.Foundation.Metadata;
using Windows.System.Profile;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace WikiViewer.Uwp.Services
{
    public static class AppearanceService
    {
        private static ResourceDictionary _appearance;
        public static bool UseFluent { get; private set; }
        public static void Apply()
        {
            ulong.TryParse(AnalyticsInfo.VersionInfo.DeviceFamilyVersion, out var version);
            bool windows11 = ((version >> 16) & 0xffff) >= 22000;
            UseFluent = AppSettings.Appearance == AppAppearance.Windows10Fluent ||
                AppSettings.Appearance != AppAppearance.Windows11 && !windows11;
            string file = UseFluent ? (ApiInformation.IsTypePresent("Windows.UI.Xaml.Media.AcrylicBrush") ? "Windows10Acrylic.xaml" : "Windows10.xaml") : "Windows11.xaml";
            var resources = Application.Current.Resources.MergedDictionaries;
            var source = new Uri("ms-appx:///Themes/" + file);
            bool changed = _appearance?.Source != source;
            if (changed)
            {
                var next = new ResourceDictionary { Source = source };
                if (_appearance != null) resources.Remove(_appearance);
                resources.Add(next); _appearance = next;
            }
            if (Window.Current.Content is Frame frame && frame.Content is Page root)
            {
                Microsoft.UI.Xaml.Controls.BackdropMaterial.SetApplyToRootOrPageBackground(root, !UseFluent && windows11);
                // Refresh theme references without replacing pages or losing unsaved edits.
                if (changed)
                {
                    var theme = root.RequestedTheme;
                    root.RequestedTheme = theme == ElementTheme.Light ? ElementTheme.Dark : ElementTheme.Light;
                    root.RequestedTheme = theme;
                }
            }
        }
    }
}
