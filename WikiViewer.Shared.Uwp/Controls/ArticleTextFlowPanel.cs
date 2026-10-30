using System;
using System.Collections.Generic;
using System.Linq;
using WikiViewer.Core.Services;
using Windows.Foundation;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace WikiViewer.Shared.Uwp.Controls
{
    /// <summary>Link a paragraph through width bands beside floats and back to full width below them.</summary>
    internal sealed class ArticleTextFlowPanel : Panel
    {
        private readonly RichTextBlock _text;
        private readonly List<ArticleFlowRect> _positions = new List<ArticleFlowRect>();
        private List<ArticleFlowRect> _regions;
        public List<ArticleFlowRect> Regions
        {
            get => _regions;
            set
            {
                if (_regions == null && value == null) return;
                if (_regions != null && value != null && _regions.Count == value.Count && _regions.Zip(value, (a, b) => a.X == b.X && a.Y == b.Y && a.Width == b.Width && a.Height == b.Height).All(equal => equal)) return;
                _regions = value; InvalidateMeasure();
            }
        }
        public ArticleTextFlowPanel(RichTextBlock text)
        {
            _text = text; Margin = text.Margin; text.Margin = new Thickness(0); Children.Add(text);
        }
        protected override Size MeasureOverride(Size availableSize)
        {
            double width = double.IsInfinity(availableSize.Width) ? 960 : Math.Max(1, availableSize.Width);
            var regions = Regions ?? new List<ArticleFlowRect> { new ArticleFlowRect { Width = width, Height = double.PositiveInfinity } };
            while (Children.Count < regions.Count) Children.Add(new RichTextBlockOverflow());
            var first = regions.Count > 1 ? (RichTextBlockOverflow)Children[1] : null;
            // Reassigning the same overflow target still invalidates native text layout.
            // Doing so during Measure keeps the entire page in a layout loop.
            if (_text.OverflowContentTarget != first) _text.OverflowContentTarget = first;
            for (int i = 1; i < Children.Count; i++)
            {
                var overflow = (RichTextBlockOverflow)Children[i];
                var next = i + 1 < regions.Count ? (RichTextBlockOverflow)Children[i + 1] : null;
                if (overflow.OverflowContentTarget != next) overflow.OverflowContentTarget = next;
            }
            _positions.Clear(); bool more = true;
            for (int i = 0; i < Children.Count; i++)
            {
                var element = (FrameworkElement)Children[i];
                if (!more || i >= regions.Count) { if (element.Visibility != Visibility.Collapsed) element.Visibility = Visibility.Collapsed; continue; }
                var region = regions[i]; if (element.Visibility != Visibility.Visible) element.Visibility = Visibility.Visible;
                double constraint = Math.Max(1, Math.Min(width - region.X, region.Width));
                if (element.MaxWidth != constraint) element.MaxWidth = constraint;
                if (element.MaxHeight != region.Height) element.MaxHeight = region.Height;
                element.Measure(new Size(element.MaxWidth, region.Height));
                more = i == 0 ? _text.HasOverflowContent : ((RichTextBlockOverflow)element).HasOverflowContent;
                double height = more && !double.IsInfinity(region.Height) ? region.Height : element.DesiredSize.Height;
                _positions.Add(new ArticleFlowRect { X = region.X, Y = region.Y, Width = element.MaxWidth, Height = height });
            }
            return new Size(width, _positions.Select(r => r.Bottom).DefaultIfEmpty(0).Max());
        }
        protected override Size ArrangeOverride(Size finalSize)
        {
            for (int i = 0; i < _positions.Count; i++) { var r = _positions[i]; Children[i].Arrange(new Rect(r.X, r.Y, r.Width, r.Height)); }
            return finalSize;
        }
    }
}
