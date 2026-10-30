using System;
using WikiViewer.Core.Models;

namespace WikiViewer.Core.Services
{
    public enum ArticleColorRole { Background, Text, Link, Border }

    public sealed class ArticleThemeColors
    {
        public bool Dark { get; set; }
        public bool HighContrast { get; set; }
        public uint Canvas { get; set; }
        public uint Text { get; set; }
        public uint Link { get; set; }
        public uint Border { get; set; }
        public ArticleThemeColors(bool dark)
        {
            Dark = dark; Canvas = dark ? 0xff202020 : 0xffffffff;
            Text = dark ? 0xffe6e6e6 : 0xff202122;
            Link = dark ? 0xff8ab4f8 : 0xff0058e0;
            Border = dark ? 0xff676767 : 0xffa2a9b1;
        }
    }

    /// <summary>Adapt UI decoration colours; image/media pixels and cached source remain untouched.</summary>
    public static class ArticleThemeService
    {
        public static uint Resolve(uint? source, ArticleColorRole role, ArticleBackground context, ArticleThemeColors theme)
        {
            if (role == ArticleColorRole.Background) return Background(source ?? 0, theme);
            uint background = EffectiveBackground(context, theme);
            if (role == ArticleColorRole.Border)
                return theme.HighContrast ? theme.Text : source.HasValue && theme.Dark
                    ? Mix(theme.Canvas, source.Value, 0.35) : source ?? theme.Border;
            uint color = theme.HighContrast ? (role == ArticleColorRole.Link ? theme.Link : theme.Text)
                : source ?? (role == ArticleColorRole.Link ? theme.Link : theme.Text);
            if ((color >> 24) == 0) return color;
            if (theme.Dark && role == ArticleColorRole.Text && Luminance(background) < 0.2 && Luminance(color) < 0.25 &&
                Math.Max(Channel(color, 16), Math.Max(Channel(color, 8), Channel(color, 0))) - Math.Min(Channel(color, 16), Math.Min(Channel(color, 8), Channel(color, 0))) < 32)
                color = theme.Text;
            return Readable(color, background);
        }

        public static uint EffectiveBackground(ArticleBackground context, ArticleThemeColors theme)
        {
            if (theme.HighContrast || context == null) return theme.Canvas;
            return Composite(Background(context.Color, theme), EffectiveBackground(context.Parent, theme));
        }

        private static uint Background(uint color, ArticleThemeColors theme)
        {
            if ((color >> 24) == 0) return color;
            if (theme.HighContrast) return theme.Canvas;
            if (!theme.Dark || Luminance(color) < 0.3) return color;
            int red = Channel(color, 16), green = Channel(color, 8), blue = Channel(color, 0);
            int floor = Math.Min(red, Math.Min(green, blue));
            return (color & 0xff000000) | ((uint)(40 + (red - floor) * 0.4) << 16) |
                ((uint)(40 + (green - floor) * 0.4) << 8) | (uint)(40 + (blue - floor) * 0.4);
        }

        private static uint Readable(uint color, uint background)
        {
            if (Contrast(Composite(color, background), background) >= 4.5) return color;
            uint target = Contrast(0xffffffff, background) >= Contrast(0xff000000, background) ? 0xffffffff : 0xff000000;
            // Keep the hue as far as the required text contrast permits.
            for (int step = 1; step <= 20; step++)
            {
                uint candidate = Mix(color, target, step / 20.0);
                if (Contrast(candidate, background) >= 4.5) return candidate;
            }
            return target;
        }

        public static uint Composite(uint color, uint background)
        {
            double alpha = (color >> 24) / 255.0;
            return Mix(background, color, alpha);
        }

        private static uint Mix(uint first, uint second, double amount)
            => 0xff000000 | ((uint)Math.Round(Channel(first, 16) * (1 - amount) + Channel(second, 16) * amount) << 16) |
                ((uint)Math.Round(Channel(first, 8) * (1 - amount) + Channel(second, 8) * amount) << 8) |
                (uint)Math.Round(Channel(first, 0) * (1 - amount) + Channel(second, 0) * amount);
        private static int Channel(uint color, int shift) => (int)((color >> shift) & 255);
        private static double Linear(int channel) { double value = channel / 255.0; return value <= 0.04045 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4); }
        public static double Luminance(uint color) => 0.2126 * Linear(Channel(color, 16)) + 0.7152 * Linear(Channel(color, 8)) + 0.0722 * Linear(Channel(color, 0));
        public static double Contrast(uint first, uint second)
        { double a = Luminance(first), b = Luminance(second); return (Math.Max(a, b) + 0.05) / (Math.Min(a, b) + 0.05); }
    }
}
