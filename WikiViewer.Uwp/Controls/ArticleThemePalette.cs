using System;
using System.Collections.Generic;
using WikiViewer.Core.Models;
using WikiViewer.Core.Services;
using Windows.UI;
using Windows.UI.ViewManagement;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Controls;
using WikiViewer.Core;
using WikiViewer.Uwp.Services;

namespace WikiViewer.Uwp.Controls
{
    internal sealed class ArticleThemePalette
    {
        private ArticleThemeColors _colors = new ArticleThemeColors(false);
        private readonly Dictionary<Tuple<uint?, ArticleColorRole, ArticleBackground>, SolidColorBrush> _brushes =
            new Dictionary<Tuple<uint?, ArticleColorRole, ArticleBackground>, SolidColorBrush>();
        private readonly Dictionary<SolidColorBrush, Brush> _surfaces = new Dictionary<SolidColorBrush, Brush>();
        private readonly List<WeakReference<Border>> _borders = new List<WeakReference<Border>>();
        private readonly List<WeakReference<Image>> _images = new List<WeakReference<Image>>();

        public void Update(FrameworkElement owner, bool highContrast, UISettings settings)
        {
            bool dark = owner.ActualTheme == ElementTheme.Dark;
            _colors = new ArticleThemeColors(dark) { HighContrast = highContrast };
            if (highContrast)
            {
                _colors.Canvas = Argb(settings.GetColorValue(UIColorType.Background));
                _colors.Text = Argb(settings.GetColorValue(UIColorType.Foreground));
                _colors.Link = Argb(settings.GetColorValue(UIColorType.Accent));
            }
            foreach (var entry in _brushes) entry.Value.Color = Color(ArticleThemeService.Resolve(entry.Key.Item1, entry.Key.Item2, entry.Key.Item3, _colors));
            foreach (var entry in _surfaces) ArticleSurfaceTheme.Update(entry.Value, entry.Key.Color, highContrast);
            _borders.RemoveAll(reference => { Border border; if (!reference.TryGetTarget(out border)) return true; border.CornerRadius = new CornerRadius(ArticleSurfaceTheme.Radius); return false; });
            _images.RemoveAll(reference => { Image image; if (!reference.TryGetTarget(out image)) return true; ArticleSurfaceTheme.ClipImage(image); return false; });
        }

        public SolidColorBrush Get(string source, ArticleColorRole role, ArticleBackground context = null)
        {
            uint? color = ArticleColor.Parse(source);
            if (!color.HasValue && role == ArticleColorRole.Background) return null;
            var key = Tuple.Create(color, role, context);
            SolidColorBrush brush;
            if (!_brushes.TryGetValue(key, out brush))
            {
                brush = new SolidColorBrush(Color(ArticleThemeService.Resolve(color, role, context, _colors)));
                _brushes.Add(key, brush);
            }
            return brush;
        }
        public Brush Surface(string source)
        {
            var tint = Get(source, ArticleColorRole.Background);
            if (tint == null) return null;
            Brush surface;
            if (!_surfaces.TryGetValue(tint, out surface)) _surfaces[tint] = surface = ArticleSurfaceTheme.Create(tint.Color, _colors.HighContrast);
            return surface;
        }

        public Brush Canvas(string source)
        {
            var tint = Get(source, ArticleColorRole.Background);
            if (tint == null || !AppSettings.ThemeNativeArticles || _colors.HighContrast || AppearanceService.Current == AppAppearance.Windows10) return tint;
            var color = tint.Color;
            // A neutral wiki canvas follows the app's Mica/acrylic. Explicit coloured canvases
            // keep their hue, just as coloured cards do. Images and text are never blurred.
            int spread = Math.Max(color.R, Math.Max(color.G, color.B)) - Math.Min(color.R, Math.Min(color.G, color.B));
            return spread <= 12 ? new SolidColorBrush(Windows.UI.Colors.Transparent) : Surface(source);
        }

        public void Decorate(Border border)
        {
            border.CornerRadius = new CornerRadius(ArticleSurfaceTheme.Radius);
            _borders.Add(new WeakReference<Border>(border));
        }

        public void Decorate(Image image)
        {
            _images.Add(new WeakReference<Image>(image));
            image.SizeChanged += (s, e) => ArticleSurfaceTheme.ClipImage(image);
            ArticleSurfaceTheme.ClipImage(image);
        }

        public void Clear() { _brushes.Clear(); _surfaces.Clear(); _borders.Clear(); _images.Clear(); }
        private static uint Argb(Color color) => ((uint)color.A << 24) | ((uint)color.R << 16) | ((uint)color.G << 8) | color.B;
        private static Color Color(uint color) => Windows.UI.Color.FromArgb((byte)(color >> 24), (byte)(color >> 16), (byte)(color >> 8), (byte)color);
    }
}
