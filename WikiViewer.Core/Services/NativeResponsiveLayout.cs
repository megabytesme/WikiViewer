using System;
using System.Collections.Generic;
using System.Linq;
using WikiViewer.Core.Models;

namespace WikiViewer.Core.Services
{
    /// <summary>Finite-width sizing shared by native tables, flex rows and compatibility tests.</summary>
    public static class NativeResponsiveLayout
    {
        public static NativeArticleStyle FlexItemStyle(NativeArticleNode node, double available)
        {
            var style = node.Style.Copy();
            // An auto basis follows the content. A fixed 200px basis lets a small
            // icon consume most of an audio card while its player is squeezed.
            if (style.FlexBasis == null && style.Width == null)
                style.FlexBasis = new ArticleLength { Value = PreferredCellWidth(node, available), Unit = "px" };
            return style;
        }

        public static double PreferredCellWidth(NativeArticleNode cell, double available)
        {
            if (cell.Style.Width != null) return Math.Max(40, Math.Min(2000, cell.Style.Width.Resolve(available)));
            Func<NativeArticleNode, string> text = null;
            text = node => node.Kind == NativeArticleNodeKind.Text ? node.Text ?? "" : string.Concat(node.Children.Select(text));
            // Estimate an intrinsic width without measuring nested flow panels against infinity.
            double font = cell.Style.FontSize?.Resolve(18) ?? 18;
            double preferred = Math.Min(360, Math.Max(40, text(cell).Trim().Length * font * 0.5 + 20));
            Func<NativeArticleNode, double> image = null;
            image = node => Math.Max(node.ImageWidth, node.Children.Select(image).DefaultIfEmpty(0).Max());
            return Math.Max(preferred, Math.Min(360, image(cell) + 16));
        }
        public static double[] TableColumns(double available, double[] preferred, double minimum = 40)
        {
            var widths = preferred.Select(w => Math.Max(minimum, Math.Min(2000, w))).ToArray();
            double total = widths.Sum(), floor = widths.Length * minimum;
            double target = Math.Max(floor, Math.Min(total, Math.Max(1, available)));
            if (total > target && total > floor)
                for (int i = 0; i < widths.Length; i++) widths[i] = minimum + (widths[i] - minimum) * (target - floor) / (total - floor);
            return widths;
        }

        public static List<ArticleFlowRect> Flex(double available, IList<NativeArticleStyle> items, NativeArticleStyle container,
            Func<int, double, double> measureHeight)
        {
            double width = Math.Max(1, available), gap = Math.Max(0, Math.Min(100, container.Gap?.Resolve(width) ?? 0));
            var result = new List<ArticleFlowRect>();
            double y = 0;
            for (int start = 0; start < items.Count;)
            {
                int end = start; double occupied = 0;
                var sizes = new List<double>(); var minimums = new List<double>();
                while (end < items.Count)
                {
                    var item = items[end];
                    double min = Math.Min(width, item.MinWidth?.Resolve(width) ?? 0);
                    double basis = Math.Max(min, (item.FlexBasis ?? item.Width)?.Resolve(width) ?? 200);
                    basis = Math.Min(width, Math.Min(basis, Math.Max(min, item.MaxWidth?.Resolve(width) ?? width)));
                    if (container.Wrap && end > start && occupied + gap + basis > width + 0.01) break;
                    occupied += (end > start ? gap : 0) + basis; sizes.Add(basis); minimums.Add(min); end++;
                }
                double free = width - occupied;
                if (free > 0)
                {
                    // Redistribute remaining space when an item reaches max-width.
                    for (int pass = 0; pass < sizes.Count && free > 0.01; pass++)
                    {
                        double grow = Enumerable.Range(start, end - start).Where(i => sizes[i - start] < (items[i].MaxWidth?.Resolve(width) ?? width) - 0.01).Sum(i => items[i].FlexGrow);
                        if (grow <= 0) break;
                        double distributed = 0;
                        for (int i = start; i < end; i++)
                        {
                            double add = Math.Max(0, Math.Min(free * items[i].FlexGrow / grow, (items[i].MaxWidth?.Resolve(width) ?? width) - sizes[i - start]));
                            sizes[i - start] += add; distributed += add;
                        }
                        free -= distributed;
                    }
                }
                else if (free < 0)
                {
                    for (int pass = 0; pass < sizes.Count && free < -0.01; pass++)
                    {
                        double shrink = Enumerable.Range(start, end - start).Where(i => sizes[i - start] > minimums[i - start] + 0.01).Sum(i => items[i].FlexShrink * sizes[i - start]);
                        if (shrink <= 0) break;
                        double removed = 0;
                        for (int i = start; i < end; i++)
                        {
                            double take = Math.Min(-free * items[i].FlexShrink * sizes[i - start] / shrink, sizes[i - start] - minimums[i - start]);
                            sizes[i - start] -= take; removed += take;
                        }
                        free += removed;
                    }
                }
                double remaining = Math.Max(0, width - sizes.Sum() - gap * (sizes.Count - 1));
                double x = container.JustifyContent == "center" ? remaining / 2 : container.JustifyContent == "flex-end" ? remaining : 0;
                double spacing = gap + (container.JustifyContent == "space-between" && sizes.Count > 1 ? remaining / (sizes.Count - 1) : 0), height = 0;
                for (int i = start; i < end; i++)
                {
                    double measured = Math.Max(0, measureHeight(i, Math.Max(1, sizes[i - start])));
                    result.Add(new ArticleFlowRect { X = x, Y = y, Width = Math.Max(1, sizes[i - start]), Height = measured });
                    x += sizes[i - start] + spacing; height = Math.Max(height, measured);
                }
                y += height + gap; start = end;
            }
            return result;
        }
    }
}
