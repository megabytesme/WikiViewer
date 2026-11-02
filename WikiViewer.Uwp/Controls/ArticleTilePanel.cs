using System;
using System.Collections.Generic;
using System.Linq;
using WikiViewer.Core.Services;
using WikiViewer.Core.Models;
using Windows.Foundation;
using Windows.UI.Xaml.Controls;

namespace WikiViewer.Uwp.Controls
{
    /// <summary>Responsive rows for static galleries and equal-track grids/flex groups.</summary>
    internal sealed class ArticleTilePanel : Panel
    {
        public int Columns { get; set; }
        public double Gap { get; set; } = 8;
        public NativeArticleStyle FlowStyle { get; set; }
        public List<NativeArticleNode> LayoutNodes { get; } = new List<NativeArticleNode>();
        private List<ArticleFlowRect> _rectangles = new List<ArticleFlowRect>();
        protected override Size MeasureOverride(Size availableSize)
        {
            double width = double.IsInfinity(availableSize.Width) ? 960 : Math.Max(1, availableSize.Width);
            Func<int, double, double> measure = (index, cell) =>
            {
                var element = Children[index] as Windows.UI.Xaml.FrameworkElement;
                if (element != null) element.MaxWidth = Math.Max(1, cell);
                Children[index].Measure(new Size(cell, double.PositiveInfinity)); return Children[index].DesiredSize.Height;
            };
            if (FlowStyle?.Display == "flex")
            {
                var styles = LayoutNodes.Count == Children.Count ? LayoutNodes.Select(node => NativeResponsiveLayout.FlexItemStyle(node, width)).ToList() :
                    Children.Select(c => c.GetValue(ArticleFlowPanel.LayoutStyleProperty) as NativeArticleStyle ?? new NativeArticleStyle()).ToList();
                _rectangles = NativeResponsiveLayout.Flex(width, styles, FlowStyle, measure);
            }
            else
            {
                int columns = FlowStyle?.GridMinimum != null ? Math.Max(1, (int)((width + Gap) / (FlowStyle.GridMinimum.Resolve(width) + Gap))) : Columns;
                _rectangles = NativeFlowLayout.Tiles(width, Children.Count, columns, Gap, measure);
            }
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
