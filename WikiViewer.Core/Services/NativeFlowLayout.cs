using System;
using System.Collections.Generic;
using System.Linq;

namespace WikiViewer.Core.Services
{
    public sealed class ArticleFlowItem
    {
        public string Float { get; set; }
        public string Clear { get; set; }
        public double PreferredWidth { get; set; }
    }

    public sealed class ArticleFlowRect
    {
        public double X { get; set; }
        public double Y { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
        public double Bottom => Y + Height;
    }

    /// <summary>Paragraph-level float/column layout, shared by the UWP panel and portable regression tests.</summary>
    public static class NativeFlowLayout
    {
        public static List<ArticleFlowRect> Tiles(double width, int itemCount, int columns, double gap, Func<int, double, double> measureHeight)
        {
            width = Math.Max(1, width); gap = Math.Max(0, Math.Min(100, gap));
            int count = Math.Max(1, Math.Min(columns > 0 ? columns : (int)((width + gap) / (200 + gap)), Math.Max(1, (int)((width + gap) / (120 + gap)))));
            double cell = Math.Max(1, (width - gap * (count - 1)) / count), y = 0;
            var result = new List<ArticleFlowRect>();
            for (int start = 0; start < itemCount; start += count)
            {
                double height = 0;
                for (int i = start; i < Math.Min(itemCount, start + count); i++)
                {
                    double measured = Math.Max(0, measureHeight(i, cell)); height = Math.Max(height, measured);
                    result.Add(new ArticleFlowRect { X = (i - start) * (cell + gap), Y = y, Width = cell, Height = measured });
                }
                y += height + gap;
            }
            return result;
        }
        public static List<ArticleFlowRect> Arrange(double width, IList<ArticleFlowItem> items, Func<int, double, double> measureHeight, int columns = 1)
        {
            width = Math.Max(1, width);
            int count = Math.Max(1, Math.Min(columns, (int)(width / 300)));
            if (count > 1) return Columns(width, items, measureHeight, count);
            var result = new List<ArticleFlowRect>();
            var floats = new List<Tuple<string, ArticleFlowRect>>();
            double y = 0;
            for (int i = 0; i < items.Count; i++)
            {
                var item = items[i];
                if (item.Clear == "both" || item.Clear == "left" || item.Clear == "right")
                    y = Math.Max(y, floats.Where(f => item.Clear == "both" || f.Item1 == item.Clear).Select(f => f.Item2.Bottom).DefaultIfEmpty(y).Max());
                floats.RemoveAll(f => f.Item2.Bottom <= y);
                bool positioned = item.Float == "left" || item.Float == "right";
                double requestedWidth = item.PreferredWidth > 0 ? item.PreferredWidth : 300;
                if (width >= 640 && positioned && requestedWidth <= width - 160)
                {
                    double floatWidth = requestedWidth;
                    double top = Math.Max(y, floats.Where(f => f.Item1 == item.Float).Select(f => f.Item2.Bottom).DefaultIfEmpty(y).Max());
                    double height = Math.Max(0, measureHeight(i, floatWidth));
                    while (true)
                    {
                        var conflicts = floats.Where(f => f.Item1 != item.Float && f.Item2.Y < top + height && f.Item2.Bottom > top && f.Item2.Width + floatWidth > width).ToList();
                        if (conflicts.Count == 0) break;
                        top = conflicts.Max(f => f.Item2.Bottom);
                    }
                    var rect = new ArticleFlowRect { X = item.Float == "right" ? width - floatWidth : 0, Y = top,
                        Width = floatWidth, Height = height };
                    result.Add(rect);
                    floats.Add(Tuple.Create(item.Float, rect));
                    continue;
                }
                if (positioned && requestedWidth > width - 160)
                { y = Math.Max(y, floats.Select(f => f.Item2.Bottom).DefaultIfEmpty(y).Max()); floats.Clear(); }
                double left, right;
                while (true)
                {
                    left = floats.Where(f => f.Item1 == "left" && f.Item2.Bottom > y).Select(f => f.Item2.Width + 12).DefaultIfEmpty(0).Max();
                    right = floats.Where(f => f.Item1 == "right" && f.Item2.Bottom > y).Select(f => f.Item2.X - 12).DefaultIfEmpty(width).Min();
                    if (right - left >= Math.Min(160, width) || floats.Count == 0) break;
                    y = floats.Where(f => f.Item2.Bottom > y).Min(f => f.Item2.Bottom);
                    floats.RemoveAll(f => f.Item2.Bottom <= y);
                }
                double available = Math.Max(1, right - left);
                var block = new ArticleFlowRect { X = left, Y = y, Width = available, Height = Math.Max(0, measureHeight(i, available)) };
                result.Add(block);
                y = block.Bottom;
            }
            return result;
        }

        private static List<ArticleFlowRect> Columns(double width, IList<ArticleFlowItem> items, Func<int, double, double> measureHeight, int count)
        {
            double columnWidth = (width - 16 * (count - 1)) / count;
            var heights = Enumerable.Range(0, items.Count).Select(i => Math.Max(0, measureHeight(i, columnWidth))).ToList();
            double target = heights.Sum() / count;
            var result = new List<ArticleFlowRect>();
            int column = 0;
            double y = 0;
            for (int i = 0; i < items.Count; i++)
            {
                if (column < count - 1 && y > 0 && y + heights[i] > target) { column++; y = 0; }
                result.Add(new ArticleFlowRect { X = column * (columnWidth + 16), Y = y, Width = columnWidth, Height = heights[i] });
                y += heights[i];
            }
            return result;
        }
    }
}
