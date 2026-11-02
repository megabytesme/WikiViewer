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
            RequiresRestart = ((Microsoft.UI.Xaml.Controls.XamlControlsResources)_controlAppearance).ControlsResourcesVersion != desiredControls;
            var source = new Uri("ms-appx:///Themes/" + file);
            bool changed = _appearance?.Source != source;
            if (changed)
            {
                var next = new ResourceDictionary { Source = source };
                Trace("loaded " + file);
                // Keep the initial app dictionary alive for realized WinUI templates.
                // App-owned surfaces resolve their current brushes separately.
                if (_appearance == null) resources.Add(next);
                _appearance = next;
                Trace("merged " + file);
            }
            if (Window.Current.Content is Frame frame && frame.Content is Page root) ApplyBackdrop(root);
            AppearanceSurface.Refresh();
            AppearanceChanged?.Invoke(null, EventArgs.Empty);
            WriteMaterialDiagnostics();
            Trace("applied " + Current);
        }

        public static void ApplyBackdrop(Page root)
        {
            bool mica = Current == AppAppearance.Windows11SunValley;
            Microsoft.UI.Xaml.Controls.BackdropMaterial.SetApplyToRootOrPageBackground(root, mica);
            // Mica writes a local Background value; disabling it does not restore the
            // former ThemeResource expression. Restore the selected material explicitly.
            if (!mica) root.Background = GetBrush("AppBackgroundBrush");
            if (root.FindName("NavView") is FrameworkElement navigation)
                UpdateNavigationSurfaces(navigation);
            WriteMaterialDiagnostics();
        }

        private static void WriteMaterialDiagnostics()
        {
            try
            {
                var root = (Window.Current.Content as Frame)?.Content as Page;
                var details = new { Utc = DateTime.UtcNow, Theme = Current.ToString(), Mica = root != null && Microsoft.UI.Xaml.Controls.BackdropMaterial.GetApplyToRootOrPageBackground(root),
                    Background = root?.Background?.GetType().Name, RequiresRestart, Surfaces = AppearanceSurface.Describe() };
                System.IO.File.WriteAllText(System.IO.Path.Combine(Windows.Storage.ApplicationData.Current.LocalFolder.Path, "appearance-materials.json"), Newtonsoft.Json.JsonConvert.SerializeObject(details));
            }
            catch (Exception) { }
        }

        private static void UpdateNavigationSurfaces(DependencyObject target)
        {
            if (target is FrameworkElement element)
            {
                if (element is SplitView split && element.Name == "RootSplitView")
                {
                    split.PaneBackground = GetBrush("AppBackgroundBrush");
                    if (split.Content is FrameworkElement content && content.Name == "ContentGrid")
                        AppearanceSurface.SetResourceKey(content, "AppContentBackgroundBrush");
                    return;
                }
                // The content presenter hosts the article; only inspect navigation chrome.
                if (element is ContentPresenter && element.Name == "ContentPresenter") return;
            }
            for (int i = 0; i < Windows.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(target); i++)
                UpdateNavigationSurfaces(Windows.UI.Xaml.Media.VisualTreeHelper.GetChild(target, i));
        }

        internal static Windows.UI.Xaml.Media.Brush GetBrush(string key)
        {
            bool dark = Application.Current.RequestedTheme == ApplicationTheme.Dark;
            if (Window.Current.Content is Frame frame && frame.Content is Page root && ApiInformation.IsPropertyPresent("Windows.UI.Xaml.FrameworkElement", "ActualTheme"))
                dark = IsDark(root);
            string theme = new Windows.UI.ViewManagement.AccessibilitySettings().HighContrast ? "HighContrast" :
                dark ? "Dark" : "Light";
            return FindBrush(_appearance, key, theme) ?? Application.Current.Resources[key] as Windows.UI.Xaml.Media.Brush;
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static bool IsDark(FrameworkElement owner) => owner.ActualTheme == ElementTheme.Dark;

        private static Windows.UI.Xaml.Media.Brush FindBrush(ResourceDictionary dictionary, string key, string theme)
        {
            if (dictionary == null) return null;
            if (dictionary.ThemeDictionaries.TryGetValue(theme, out var themed) && themed is ResourceDictionary colors && colors.ContainsKey(key))
                return colors[key] as Windows.UI.Xaml.Media.Brush;
            if (dictionary.ContainsKey(key)) return dictionary[key] as Windows.UI.Xaml.Media.Brush;
            for (int i = dictionary.MergedDictionaries.Count - 1; i >= 0; i--)
            {
                var brush = FindBrush(dictionary.MergedDictionaries[i], key, theme);
                if (brush != null) return brush;
            }
            return null;
        }

        private static ResourceDictionary CreateControlResources(Microsoft.UI.Xaml.Controls.ControlsResourcesVersion version) =>
            (ResourceDictionary)XamlReader.Load("<XamlControlsResources xmlns='using:Microsoft.UI.Xaml.Controls' ControlsResourcesVersion='" + version + "'/>");

        private static void Trace(string stage)
        {
            try { System.IO.File.WriteAllText(System.IO.Path.Combine(Windows.Storage.ApplicationData.Current.LocalFolder.Path, "appearance-diagnostic.log"), DateTime.UtcNow.ToString("O") + " " + stage); }
            catch (Exception) { }
        }
    }
}
