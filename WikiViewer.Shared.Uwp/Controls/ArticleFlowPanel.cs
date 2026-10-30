using System;
using System.Collections.Generic;
using System.Linq;
using WikiViewer.Core.Models;
using WikiViewer.Core.Services;
using Windows.Foundation;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace WikiViewer.Shared.Uwp.Controls
{
    public sealed class ArticleFlowPanel : Panel
    {
        public int ColumnCount { get; set; } = 1;
        private List<ArticleFlowRect> _rectangles = new List<ArticleFlowRect>();
        public static readonly DependencyProperty LayoutStyleProperty = DependencyProperty.RegisterAttached(
            "LayoutStyle", typeof(NativeArticleStyle), typeof(ArticleFlowPanel), new PropertyMetadata(null));
        public static void SetLayoutStyle(DependencyObject element, NativeArticleStyle style) => element.SetValue(LayoutStyleProperty, style);

        protected override Size MeasureOverride(Size availableSize)
        {
            double width = double.IsInfinity(availableSize.Width) ? 960 : availableSize.Width;
            var items = Children.Select(child =>
            {
                var style = child.GetValue(LayoutStyleProperty) as NativeArticleStyle;
                return new ArticleFlowItem { Float = style?.Float, Clear = style?.Clear,
                    PreferredWidth = style?.Width?.Resolve(width) ?? 300, WrapText = child is ArticleTextFlowPanel };
            }).ToList();
            _rectangles = NativeFlowLayout.Arrange(width, items, (index, constraint) =>
            {
                var element = Children[index] as FrameworkElement;
                var flowText = element as ArticleTextFlowPanel;
                if (flowText != null) flowText.Regions = null;
                if (element != null)
                {
                    var style = element.GetValue(LayoutStyleProperty) as NativeArticleStyle;
                    element.MaxWidth = Math.Min(style?.MaxWidth?.Resolve(width) ?? 960, Math.Max(1, constraint));
                    if (style?.Width != null) element.Width = Math.Min(element.MaxWidth, Math.Max(1, style.Width.Resolve(width)));
                }
                Children[index].Measure(new Size(Math.Max(1, constraint), double.PositiveInfinity));
                return Children[index].DesiredSize.Height;
            }, ColumnCount, (index, regions) =>
            {
                var flow = (ArticleTextFlowPanel)Children[index]; flow.Regions = regions; flow.MaxWidth = width;
                flow.Measure(new Size(width, double.PositiveInfinity)); return flow.DesiredSize.Height;
            });
            return new Size(Math.Max(0, width), _rectangles.Select(r => r.Bottom).DefaultIfEmpty(0).Max());
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            for (int i = 0; i < Children.Count && i < _rectangles.Count; i++)
            {
                var rect = _rectangles[i];
                Children[i].Arrange(new Rect(rect.X, rect.Y, rect.Width, rect.Height));
            }
            return finalSize;
        }
    }
}
