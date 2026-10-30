using System;
using System.Collections.Generic;
using System.Linq;
using WikiViewer.Core.Services;
using Windows.Foundation;
using Windows.UI.Xaml.Controls;

namespace WikiViewer.Shared.Uwp.Controls
{
    /// <summary>Responsive rows for static galleries and equal-track grids/flex groups.</summary>
    internal sealed class ArticleTilePanel : Panel
    {
        public int Columns { get; set; }
        public double Gap { get; set; } = 8;
        private List<ArticleFlowRect> _rectangles = new List<ArticleFlowRect>();
        protected override Size MeasureOverride(Size availableSize)
        {
            double width = double.IsInfinity(availableSize.Width) ? 960 : Math.Max(1, availableSize.Width);
            _rectangles = NativeFlowLayout.Tiles(width, Children.Count, Columns, Gap, (index, cell) =>
            {
                Children[index].Measure(new Size(cell, double.PositiveInfinity)); return Children[index].DesiredSize.Height;
            });
            return new Size(width, _rectangles.Select(r => r.Bottom).DefaultIfEmpty(0).Max());
        }
        protected override Size ArrangeOverride(Size finalSize)
        {
            for (int i = 0; i < Children.Count && i < _rectangles.Count; i++)
            {
                var r = _rectangles[i]; Children[i].Arrange(new Rect(r.X, r.Y, r.Width, r.Height));
            }
            return finalSize;
        }
    }
}
