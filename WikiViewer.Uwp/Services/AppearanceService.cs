using System;
using WikiViewer.Core;
using WikiViewer.Core.Services;
using Windows.Foundation.Metadata;
using Windows.System.Profile;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Markup;

namespace WikiViewer.Uwp.Services
{
    public static class AppearanceService
    {
        private static ResourceDictionary _appearance;
        private static ResourceDictionary _controlAppearance;
        public static bool RequiresRestart { get; private set; }
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
            Trace("select theme");
            var desired = AppThemeSupport.Resolve(AppSettings.Appearance, OSBuild);
            // Keep the dictionaries containing AcrylicBrush out of the 15063 XAML loader.
            bool acrylic = ApiInformation.IsTypePresent("Windows.UI.Xaml.Media.AcrylicBrush");
            if (desired == AppAppearance.Windows10Fluent && !acrylic) desired = AppAppearance.Windows10;
            if (_appearance != null)
            {
                // WinUI's template and backdrop resources belong to the running view.
                // Preserve that view and any draft; apply a different style next launch.
                RequiresRestart = desired != Current;
                AppearanceChanged?.Invoke(null, EventArgs.Empty);
                Trace("applied " + Current + (RequiresRestart ? "; pending " + desired : ""));
                return;
            }
            Current = desired;
            UseAcrylicSurfaces = Current == AppAppearance.Windows10Fluent;
            string file = Current == AppAppearance.Windows11SunValley ? "Windows11SunValley.xaml" :
                Current == AppAppearance.Windows10Fluent ? "Windows10Fluent.xaml" : "Windows10.xaml";
            var resources = Application.Current.Resources.MergedDictionaries;
            Trace("control resources " + Current);
            var desiredControls = Current == AppAppearance.Windows11SunValley ?
                Microsoft.UI.Xaml.Controls.ControlsResourcesVersion.Version2 : Microsoft.UI.Xaml.Controls.ControlsResourcesVersion.Version1;
            // WinUI caches templates for realized controls. Change their version only
            // before creating pages; live switches would invalidate existing templates.
            // A single dictionary also avoids conflicting WinUI theme resource caches.
            if (_controlAppearance == null)
            {
                _controlAppearance = CreateControlResources(desiredControls);
                resources.Insert(0, _controlAppearance);
            }
            var source = new Uri("ms-appx:///Themes/" + file);
            bool changed = _appearance?.Source != source;
            if (changed)
            {
                var next = new ResourceDictionary { Source = source };
                Trace("loaded " + file);
                resources.Add(next);
                if (_appearance != null) resources.Remove(_appearance);
                _appearance = next;
                Trace("merged " + file);
            }
            AppearanceChanged?.Invoke(null, EventArgs.Empty);
            Trace("applied " + Current);
        }

        public static void ApplyBackdrop(Page root) =>
            Microsoft.UI.Xaml.Controls.BackdropMaterial.SetApplyToRootOrPageBackground(root, Current == AppAppearance.Windows11SunValley);

        private static ResourceDictionary CreateControlResources(Microsoft.UI.Xaml.Controls.ControlsResourcesVersion version) =>
            (ResourceDictionary)XamlReader.Load("<XamlControlsResources xmlns='using:Microsoft.UI.Xaml.Controls' ControlsResourcesVersion='" + version + "'/>");

        private static void Trace(string stage)
        {
            try { System.IO.File.WriteAllText(System.IO.Path.Combine(Windows.Storage.ApplicationData.Current.LocalFolder.Path, "appearance-diagnostic.log"), DateTime.UtcNow.ToString("O") + " " + stage); }
            catch (Exception) { }
        }
    }
}
