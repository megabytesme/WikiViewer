using System;
using Windows.Foundation;
using Windows.UI.Xaml.Controls;

namespace WikiViewer.Uwp.Controls
{
    /// <summary>Poster and player occupy the same aspect-ratio surface.</summary>
    internal sealed class ArticleVideoViewport : Panel
    {
        public double AspectRatio { get; set; } = 16d / 9;
        protected override Size MeasureOverride(Size availableSize)
        {
            double width = double.IsInfinity(availableSize.Width) ? 480 : Math.Max(1, availableSize.Width);
            var size = new Size(width, Math.Min(480, width / AspectRatio));
            foreach (var child in Children) child.Measure(size);
            return size;
        }
        protected override Size ArrangeOverride(Size finalSize)
        {
            foreach (var child in Children) child.Arrange(new Rect(0, 0, finalSize.Width, finalSize.Height));
            return finalSize;
        }
    }
}
