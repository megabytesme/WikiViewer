using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using WikiViewer.Core;
using WikiViewer.Uwp.Services;
using Windows.Foundation.Metadata;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Hosting;
using Windows.UI.Xaml.Media;

namespace WikiViewer.Uwp.Controls
{
    internal static class ArticleSurfaceTheme
    {
        internal static double Radius => AppSettings.ThemeNativeArticles ? AppearanceService.CardRadius : 0;
        internal static Brush Create(Color tint, bool highContrast)
        {
            if (!ApiInformation.IsTypePresent("Windows.UI.Xaml.Media.AcrylicBrush")) return new SolidColorBrush(tint);
            return CreateAcrylic(tint, highContrast);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static Brush CreateAcrylic(Color tint, bool highContrast)
        {
            var brush = new AcrylicBrush { BackgroundSource = AcrylicBackgroundSource.Backdrop };
            Update(brush, tint, highContrast); return brush;
        }

        internal static void Update(Brush brush, Color tint, bool highContrast)
        {
            if (brush is SolidColorBrush solid) { solid.Color = tint; return; }
            if (ApiInformation.IsTypePresent("Windows.UI.Xaml.Media.AcrylicBrush")) UpdateAcrylic(brush, tint, highContrast);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void UpdateAcrylic(Brush material, Color tint, bool highContrast)
        {
            var brush = (AcrylicBrush)material;
            bool fluent = AppSettings.ThemeNativeArticles && AppearanceService.UseAcrylicSurfaces && !highContrast;
            brush.TintColor = Color.FromArgb(255, tint.R, tint.G, tint.B);
            brush.FallbackColor = tint;
            // The wiki supplies the hue; its original alpha remains meaningful. A strong tint
            // keeps foreground contrast close to the solid colour used by the theme palette.
            brush.TintOpacity = fluent ? 0.88 * tint.A / 255.0 : 1;
            if (ApiInformation.IsPropertyPresent("Windows.UI.Xaml.Media.AcrylicBrush", "AlwaysUseFallback")) brush.AlwaysUseFallback = !fluent;
        }

        internal static void ClipImage(Image image)
        {
            if (ApiInformation.IsMethodPresent("Windows.UI.Composition.Compositor", "CreateRoundedRectangleGeometry")) ClipModern(image);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void ClipModern(Image image)
        {
            var visual = ElementCompositionPreview.GetElementVisual(image);
            double radius = image.ActualWidth < 48 || image.ActualHeight < 48 ? 0 : Math.Min(Radius, Math.Min(image.ActualWidth, image.ActualHeight) / 2);
            if (radius <= 0) { visual.Clip = null; return; }
            var geometry = visual.Compositor.CreateRoundedRectangleGeometry();
            geometry.Size = new Vector2((float)image.ActualWidth, (float)image.ActualHeight);
            geometry.CornerRadius = new Vector2((float)radius);
            visual.Clip = visual.Compositor.CreateGeometricClip(geometry);
        }
    }
}
