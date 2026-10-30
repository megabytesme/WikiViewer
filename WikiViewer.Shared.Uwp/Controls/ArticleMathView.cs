using System;
using System.Linq;
using WikiViewer.Core.Models;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;

namespace WikiViewer.Shared.Uwp.Controls
{
    /// <summary>Static presentation MathML using native controls. Font metrics approximate browser math layout.</summary>
    internal static class ArticleMathView
    {
        public static FrameworkElement Create(NativeArticleNode node, double size = 18)
        {
            var children = node.Children;
            Func<int, double, FrameworkElement> child = (index, font) => index < children.Count ? Create(children[index], font) : Token("", font);
            if (node.TagName == "token" || node.Kind == NativeArticleNodeKind.Unsupported) return Token(node.Text ?? "", size);
            if (node.TagName == "mfrac")
            {
                var grid = new Grid();
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1) });
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                var numerator = child(0, size); numerator.HorizontalAlignment = HorizontalAlignment.Center; numerator.Margin = new Thickness(3, 0, 3, 2);
                var denominator = child(1, size); denominator.HorizontalAlignment = HorizontalAlignment.Center; denominator.Margin = new Thickness(3, 2, 3, 0);
                var line = new Border { Background = ArticleVisualStyle.Brush(node.Style.Foreground) ?? new SolidColorBrush(Windows.UI.Colors.Gray) };
                Grid.SetRow(line, 1); Grid.SetRow(denominator, 2); grid.Children.Add(numerator); grid.Children.Add(line); grid.Children.Add(denominator); return grid;
            }
            if (node.TagName == "msqrt" || node.TagName == "mroot")
            {
                var row = new StackPanel { Orientation = Orientation.Horizontal };
                if (node.TagName == "mroot") row.Children.Add(child(1, size * 0.65));
                row.Children.Add(Token("√", size * 1.4));
                row.Children.Add(new Border { BorderThickness = new Thickness(0, 1, 0, 0), BorderBrush = new SolidColorBrush(Windows.UI.Colors.Gray),
                    Child = node.TagName == "mroot" ? child(0, size) : Row(node, size), Padding = new Thickness(2, 0, 2, 0) }); return row;
            }
            if (new[] { "msub", "msup", "msubsup", "munder", "mover", "munderover" }.Contains(node.TagName))
            {
                bool limits = node.TagName == "munder" || node.TagName == "mover" || node.TagName == "munderover";
                bool hasSub = node.TagName == "msub" || node.TagName == "msubsup" || node.TagName == "munder" || node.TagName == "munderover";
                bool hasSup = node.TagName != "msub" && node.TagName != "munder";
                var grid = new Grid();
                for (int i = 0; i < 3; i++) grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                if (!limits) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var basis = child(0, size); Grid.SetRow(basis, 1); grid.Children.Add(basis);
                if (hasSup) { var sup = child(hasSub ? 2 : 1, size * 0.7); Grid.SetColumn(sup, limits ? 0 : 1); grid.Children.Add(sup); }
                if (hasSub) { var sub = child(1, size * 0.7); Grid.SetColumn(sub, limits ? 0 : 1); Grid.SetRow(sub, 2); grid.Children.Add(sub); }
                return grid;
            }
            if (node.TagName == "mtable")
            {
                var grid = new Grid(); int columnCount = children.Select(n => n.Children.Count).DefaultIfEmpty(0).Max();
                for (int c = 0; c < Math.Min(64, columnCount); c++) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                for (int r = 0; r < Math.Min(256, children.Count); r++)
                {
                    grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                    for (int c = 0; c < Math.Min(64, children[r].Children.Count); c++)
                    { var cell = Create(children[r].Children[c], size); cell.Margin = new Thickness(6, 2, 6, 2); Grid.SetRow(cell, r); Grid.SetColumn(cell, c); grid.Children.Add(cell); }
                }
                return grid;
            }
            if (node.TagName == "mfenced")
            {
                string open, close; node.Attributes.TryGetValue("open", out open); node.Attributes.TryGetValue("close", out close);
                var row = new StackPanel { Orientation = Orientation.Horizontal }; row.Children.Add(Token(open ?? "(", size));
                for (int i = 0; i < children.Count; i++) { if (i > 0) row.Children.Add(Token(",", size)); row.Children.Add(Create(children[i], size)); }
                row.Children.Add(Token(close ?? ")", size)); return row;
            }
            if (node.TagName == "mspace") { string width; node.Attributes.TryGetValue("width", out width); return new Border { Width = Math.Max(0, Math.Min(500, ArticleLength.Parse(width)?.Resolve(500, size) ?? size / 3)) }; }
            var result = Row(node, size);
            result.FlowDirection = FlowDirection.LeftToRight;
            return result;
        }
        private static StackPanel Row(NativeArticleNode node, double size)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            foreach (var child in node.Children) { var content = Create(child, size); content.VerticalAlignment = VerticalAlignment.Center; row.Children.Add(content); }
            return row;
        }
        private static TextBlock Token(string text, double size) => new TextBlock { Text = text, FontFamily = new FontFamily("Cambria Math"), FontSize = Math.Max(8, size), VerticalAlignment = VerticalAlignment.Center };
    }
}
