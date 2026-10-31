using System;
using System.Linq;
using WikiViewer.Core.Services;
using WikiViewer.Core.Models;
using Windows.Foundation;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace WikiViewer.Shared.Uwp.Controls
{
    /// <summary>Wrap cells to the card's width; scroll only when the column minimums cannot fit.</summary>
    internal sealed class ArticleTableView : Panel
    {
        private readonly Grid _grid;
        private readonly ScrollViewer _scroll;
        public ArticleTableView(Grid grid)
        {
            _grid = grid;
            _scroll = new ScrollViewer { Content = grid, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollMode = ScrollMode.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
                VerticalScrollMode = ScrollMode.Disabled, ZoomMode = ZoomMode.Disabled };
            Children.Add(_scroll);
        }
        protected override Size MeasureOverride(Size availableSize)
        {
            double width = double.IsInfinity(availableSize.Width) ? 960 : Math.Max(1, availableSize.Width);
            var preferred = Enumerable.Repeat(40d, _grid.ColumnDefinitions.Count).ToArray();
            foreach (FrameworkElement cell in _grid.Children)
            {
                int column = Grid.GetColumn(cell), span = Grid.GetColumnSpan(cell);
                var node = cell.Tag as NativeArticleNode;
                double desired = Math.Max(40, (node != null ? NativeResponsiveLayout.PreferredCellWidth(node, width) : 160) / span);
                for (int i = column; i < Math.Min(preferred.Length, column + span); i++) preferred[i] = Math.Max(preferred[i], desired);
            }
            var columns = NativeResponsiveLayout.TableColumns(width, preferred);
            for (int i = 0; i < columns.Length; i++) _grid.ColumnDefinitions[i].Width = new GridLength(columns[i]);
            _scroll.Measure(new Size(width, availableSize.Height));
            return new Size(width, _scroll.DesiredSize.Height);
        }
        protected override Size ArrangeOverride(Size finalSize)
        {
            _scroll.Arrange(new Rect(0, 0, finalSize.Width, finalSize.Height)); return finalSize;
        }
    }
}
