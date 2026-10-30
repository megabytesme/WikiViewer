using System;
using System.Collections.Generic;
using System.Linq;
using WikiViewer.Core.Models;
using WikiViewer.Core.Services;
using Windows.Foundation;
using Windows.UI.Xaml.Controls;

namespace WikiViewer.Shared.Uwp.Controls
{
    /// <summary>Static relative containers with absolute annotations, such as map markers and image labels.</summary>
    internal sealed class ArticlePositionPanel : Panel
    {
        public NativeArticleStyle PositionStyle { get; set; }
        private readonly List<ArticleFlowRect> _positions = new List<ArticleFlowRect>();
        protected override Size MeasureOverride(Size availableSize)
        {
            double width = double.IsInfinity(availableSize.Width) ? 960 : Math.Max(1, availableSize.Width), y = 0;
            _positions.Clear();
            foreach (var child in Children)
            {
                var style = child.GetValue(ArticleFlowPanel.LayoutStyleProperty) as NativeArticleStyle;
                if (style?.Position == "absolute") { _positions.Add(null); continue; }
                child.Measure(new Size(width, double.PositiveInfinity));
                _positions.Add(new ArticleFlowRect { X = 0, Y = y, Width = width, Height = child.DesiredSize.Height });
                y += child.DesiredSize.Height;
            }
            double height = Math.Max(y, PositionStyle?.Height?.Resolve(width) ?? 0);
            for (int i = 0; i < Children.Count; i++)
            {
                if (_positions[i] != null) continue;
                var child = Children[i]; var style = (NativeArticleStyle)child.GetValue(ArticleFlowPanel.LayoutStyleProperty);
                double constraint = Math.Max(1, Math.Min(width, style.Width?.Resolve(width) ?? width));
                child.Measure(new Size(constraint, double.PositiveInfinity));
                _positions[i] = NativeFlowLayout.Position(width, height, Math.Min(constraint, child.DesiredSize.Width), child.DesiredSize.Height, style);
                Canvas.SetZIndex(child, style.ZIndex);
            }
            return new Size(width, height > 0 ? height : _positions.Select(r => r.Bottom).DefaultIfEmpty(0).Max());
        }
        protected override Size ArrangeOverride(Size finalSize)
        {
            for (int i = 0; i < Children.Count && i < _positions.Count; i++)
            { var r = _positions[i]; Children[i].Arrange(new Rect(r.X, r.Y, r.Width, r.Height)); }
            return finalSize;
        }
    }
}
