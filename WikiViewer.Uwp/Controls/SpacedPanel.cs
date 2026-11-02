using System;
using Windows.Foundation;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace WikiViewer.Uwp.Controls
{
    /// <summary>Stack layout with spacing on every supported OS, including build 15063.</summary>
    public sealed class SpacedPanel : Panel
    {
        public static readonly DependencyProperty SpacingProperty = DependencyProperty.Register(nameof(Spacing), typeof(double), typeof(SpacedPanel), new PropertyMetadata(0d, LayoutChanged));
        public static readonly DependencyProperty PaddingProperty = DependencyProperty.Register(nameof(Padding), typeof(Thickness), typeof(SpacedPanel), new PropertyMetadata(new Thickness(0), LayoutChanged));
        public static readonly DependencyProperty OrientationProperty = DependencyProperty.Register(nameof(Orientation), typeof(Orientation), typeof(SpacedPanel), new PropertyMetadata(Orientation.Vertical, LayoutChanged));
        public double Spacing { get => (double)GetValue(SpacingProperty); set => SetValue(SpacingProperty, value); }
        public Thickness Padding { get => (Thickness)GetValue(PaddingProperty); set => SetValue(PaddingProperty, value); }
        public Orientation Orientation { get => (Orientation)GetValue(OrientationProperty); set => SetValue(OrientationProperty, value); }
        private static void LayoutChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e) => ((SpacedPanel)sender).InvalidateMeasure();

        protected override Size MeasureOverride(Size availableSize)
        {
            var padding = Padding;
            bool horizontal = Orientation == Orientation.Horizontal;
            var constraint = new Size(horizontal ? double.PositiveInfinity : Math.Max(0, availableSize.Width - padding.Left - padding.Right),
                horizontal ? Math.Max(0, availableSize.Height - padding.Top - padding.Bottom) : double.PositiveInfinity);
            double primary = 0, secondary = 0; int count = 0;
            foreach (var child in Children)
            {
                if (child.Visibility == Visibility.Collapsed) continue;
                child.Measure(constraint);
                primary += horizontal ? child.DesiredSize.Width : child.DesiredSize.Height;
                secondary = Math.Max(secondary, horizontal ? child.DesiredSize.Height : child.DesiredSize.Width);
                count++;
            }
            primary += Math.Max(0, count - 1) * Math.Max(0, Spacing);
            return new Size((horizontal ? primary : secondary) + padding.Left + padding.Right,
                (horizontal ? secondary : primary) + padding.Top + padding.Bottom);
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            var padding = Padding;
            bool horizontal = Orientation == Orientation.Horizontal;
            double position = horizontal ? padding.Left : padding.Top;
            foreach (var child in Children)
            {
                if (child.Visibility == Visibility.Collapsed) continue;
                child.Arrange(horizontal ? new Rect(position, padding.Top, child.DesiredSize.Width, Math.Max(0, finalSize.Height - padding.Top - padding.Bottom)) :
                    new Rect(padding.Left, position, Math.Max(0, finalSize.Width - padding.Left - padding.Right), child.DesiredSize.Height));
                position += (horizontal ? child.DesiredSize.Width : child.DesiredSize.Height) + Math.Max(0, Spacing);
            }
            return finalSize;
        }
    }
}
