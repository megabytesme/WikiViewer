using System;
using System.Collections.Generic;
using WikiViewer.Core.Models;
using WikiViewer.Core.Services;
using Windows.UI;
using Windows.UI.ViewManagement;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Media;

namespace WikiViewer.Uwp.Controls
{
    internal sealed class ArticleThemePalette
    {
        private ArticleThemeColors _colors = new ArticleThemeColors(false);
        private readonly Dictionary<Tuple<uint?, ArticleColorRole, ArticleBackground>, SolidColorBrush> _brushes =
            new Dictionary<Tuple<uint?, ArticleColorRole, ArticleBackground>, SolidColorBrush>();

        public void Update(FrameworkElement owner, bool highContrast, UISettings settings)
        {
#if UWP_1809
            bool dark = owner.ActualTheme == ElementTheme.Dark;
#else
            bool dark = Application.Current.RequestedTheme == ApplicationTheme.Dark;
#endif
            _colors = new ArticleThemeColors(dark) { HighContrast = highContrast };
            if (highContrast)
            {
                _colors.Canvas = Argb(settings.GetColorValue(UIColorType.Background));
                _colors.Text = Argb(settings.GetColorValue(UIColorType.Foreground));
                _colors.Link = Argb(settings.GetColorValue(UIColorType.Accent));
            }
            foreach (var entry in _brushes) entry.Value.Color = Color(ArticleThemeService.Resolve(entry.Key.Item1, entry.Key.Item2, entry.Key.Item3, _colors));
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
        public void Clear() => _brushes.Clear();
        private static uint Argb(Color color) => ((uint)color.A << 24) | ((uint)color.R << 16) | ((uint)color.G << 8) | color.B;
        private static Color Color(uint color) => Windows.UI.Color.FromArgb((byte)(color >> 24), (byte)(color >> 16), (byte)(color >> 8), (byte)color);
    }
}
