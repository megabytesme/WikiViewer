using System;
using WikiViewer.Core;
using WikiViewer.Core.Services;
using Windows.Foundation.Metadata;
using Windows.System.Profile;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace WikiViewer.Uwp.Services
{
    public static class AppearanceService
    {
        private static ResourceDictionary _appearance;
        public static AppAppearance Current { get; private set; }
        public static bool UseWindows10Controls => Current != AppAppearance.Windows11SunValley;
        public static double ControlRadius => Current == AppAppearance.Windows10 ? 0 : Current == AppAppearance.Windows10Fluent ? 2 : 4;
        public static double OverlayRadius => Current == AppAppearance.Windows10 ? 0 : Current == AppAppearance.Windows10Fluent ? 2 : 8;
        public static double CardRadius => Current == AppAppearance.Windows10 ? 0 : Current == AppAppearance.Windows10Fluent ? 2 : 8;
        public static bool UseAcrylicSurfaces { get; private set; }
        public static event EventHandler AppearanceChanged;
        public static int OSBuild
        {
            get { ulong.TryParse(AnalyticsInfo.VersionInfo.DeviceFamilyVersion, out var version); return (int)((version >> 16) & 0xffff); }
        }
        public static bool IsSupported(AppAppearance theme) => AppThemeSupport.IsSupported(theme, OSBuild) &&
            (theme != AppAppearance.Windows10Fluent || ApiInformation.IsTypePresent("Windows.UI.Xaml.Media.AcrylicBrush"));
        public static void Apply()
        {
            Current = AppThemeSupport.Resolve(AppSettings.Appearance, OSBuild);
            // Keep the dictionaries containing AcrylicBrush out of the 15063 XAML loader.
            bool acrylic = ApiInformation.IsTypePresent("Windows.UI.Xaml.Media.AcrylicBrush");
            if (Current == AppAppearance.Windows10Fluent && !acrylic) Current = AppAppearance.Windows10;
            UseAcrylicSurfaces = Current == AppAppearance.Windows10Fluent;
            string file = Current == AppAppearance.Windows11SunValley ? "Windows11SunValley.xaml" :
                Current == AppAppearance.Windows10Fluent ? "Windows10Fluent.xaml" : "Windows10.xaml";
            var resources = Application.Current.Resources.MergedDictionaries;
            foreach (var dictionary in resources)
                if (dictionary is Microsoft.UI.Xaml.Controls.XamlControlsResources controls)
                    controls.ControlsResourcesVersion = Current == AppAppearance.Windows11SunValley ?
                        Microsoft.UI.Xaml.Controls.ControlsResourcesVersion.Version2 : Microsoft.UI.Xaml.Controls.ControlsResourcesVersion.Version1;
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
                Microsoft.UI.Xaml.Controls.BackdropMaterial.SetApplyToRootOrPageBackground(root, Current == AppAppearance.Windows11SunValley);
                // Refresh theme references without replacing pages or losing unsaved edits.
                if (changed)
                {
                    var theme = root.RequestedTheme;
                    root.RequestedTheme = theme == ElementTheme.Light ? ElementTheme.Dark : ElementTheme.Light;
                    root.RequestedTheme = theme;
                }
            }
            AppearanceChanged?.Invoke(null, EventArgs.Empty);
        }
    }
}
