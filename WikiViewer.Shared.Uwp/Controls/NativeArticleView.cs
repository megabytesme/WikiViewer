using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using WikiViewer.Core.Models;
using WikiViewer.Core.Services;
using Windows.Foundation;
using Windows.Storage;
using Windows.UI;
using Windows.UI.Text;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Documents;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Imaging;

namespace WikiViewer.Shared.Uwp.Controls
{
    /// <summary>An opt-in reading view for server-expanded content. Page scripts and CSS are not executed.</summary>
    public sealed class NativeArticleView : UserControl
    {
        private readonly StackPanel _root = new StackPanel { MaxWidth = 960, Margin = new Thickness(20, 56, 20, 80), HorizontalAlignment = HorizontalAlignment.Stretch };
        private readonly Dictionary<string, FrameworkElement> _anchors = new Dictionary<string, FrameworkElement>(StringComparer.Ordinal);
        private readonly List<Image> _images = new List<Image>();
        private Func<Uri, Task> _navigate;
        private Func<string, Task<string>> _resolveImage;
        private int _version;
        private Uri _pageUri;
        private readonly ScrollViewer _scroll = new ScrollViewer { HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };

        public NativeArticleView()
        {
            _scroll.Content = _root;
            Content = _scroll;
            _root.SizeChanged += (s, e) => { foreach (var image in _images) image.MaxWidth = Math.Max(80, e.NewSize.Width - 40); };
        }

        public void ShowArticle(ArticleContent content, Uri pageUri, Func<Uri, Task> navigate, Func<string, Task<string>> resolveImage)
        {
            try
            {
            ClearArticle();
            _navigate = navigate;
            _resolveImage = resolveImage;
            _pageUri = pageUri;
            var document = NativeArticleDocumentService.Parse(content.HtmlContent, pageUri);
            if (document.UnsupportedFeatures.Count > 0)
                _root.Children.Add(new TextBlock { Text = "Some content is available in Web view. Use the reader button to switch views.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12), Opacity = 0.75 });
            AppendBlocks(_root, document.Nodes);
            _root.UpdateLayout();
            _scroll.ChangeView(null, 0, null, true);
            }
            catch (Exception ex) { _ = LogFailureAsync(ex); throw; }
        }

        private static async Task LogFailureAsync(Exception exception)
        {
            try
            {
                var file = await ApplicationData.Current.LocalFolder.CreateFileAsync("native-reader-error.log", CreationCollisionOption.ReplaceExisting);
                await FileIO.WriteTextAsync(file, exception.ToString());
            }
            catch (Exception) { }
        }

        public void ClearArticle()
        {
            _version++;
            _root.Children.Clear();
            _anchors.Clear();
            _images.Clear();
        }

        private static bool IsBlock(NativeArticleNode node) => node.IsBlock ||
            node.Kind == NativeArticleNodeKind.Paragraph || node.Kind == NativeArticleNodeKind.Heading || node.Kind == NativeArticleNodeKind.List ||
            node.Kind == NativeArticleNodeKind.Table || node.Kind == NativeArticleNodeKind.Quote || node.Kind == NativeArticleNodeKind.Preformatted || node.Kind == NativeArticleNodeKind.Rule;

        private void AppendBlocks(Panel panel, IEnumerable<NativeArticleNode> nodes)
        {
            var inline = new List<NativeArticleNode>();
            foreach (var node in nodes)
            {
                if (!IsBlock(node)) { inline.Add(node); continue; }
                FlushInline(panel, inline);
                var block = RenderBlock(node);
                Register(node.Id, block);
                panel.Children.Add(block);
            }
            FlushInline(panel, inline);
        }

        private void FlushInline(Panel panel, List<NativeArticleNode> nodes)
        {
            if (nodes.Count == 0) return;
            if (nodes.Any(n => n.Kind != NativeArticleNodeKind.Text || !string.IsNullOrWhiteSpace(n.Text))) panel.Children.Add(Text(nodes));
            nodes.Clear();
        }

        private FrameworkElement RenderBlock(NativeArticleNode node)
        {
            if (node.Kind == NativeArticleNodeKind.Rule) return new Border { Height = 1, Background = new SolidColorBrush(Colors.Gray), Margin = new Thickness(0, 12, 0, 12) };
            if (node.Kind == NativeArticleNodeKind.Table) return Table(node);
            if (node.Kind == NativeArticleNodeKind.List)
            {
                var list = new StackPanel { Margin = new Thickness(8, 4, 0, 8) };
                int number = node.ListStart;
                foreach (var item in node.Children.Where(c => c.Kind == NativeArticleNodeKind.ListItem))
                {
                    if (item.ListValue.HasValue) number = item.ListValue.Value;
                    var row = new Grid();
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    row.Children.Add(new TextBlock { Text = node.Text == "definition" ? "" : node.Ordered ? number++.ToString() + "." : "•", MinWidth = 28, FontSize = 18, Margin = new Thickness(0, 8, 4, 0) });
                    var content = new StackPanel { Margin = new Thickness(item.Text == "definition" ? 16 : 0, 0, 0, 0) };
                    Grid.SetColumn(content, 1);
                    AppendBlocks(content, item.Children);
                    row.Children.Add(content);
                    Register(item.Id, row);
                    list.Children.Add(row);
                }
                return list;
            }
            if (node.Kind == NativeArticleNodeKind.Container || node.Kind == NativeArticleNodeKind.Quote)
            {
                var panel = new StackPanel { Margin = new Thickness(node.Kind == NativeArticleNodeKind.Quote ? 20 : 0, 0, 0, 0),
                    FlowDirection = node.RightToLeft ? FlowDirection.RightToLeft : FlowDirection.LeftToRight };
                AppendBlocks(panel, node.Children);
                return panel;
            }
            var text = Text(node.Children);
            text.FlowDirection = node.RightToLeft ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
            if (node.Kind == NativeArticleNodeKind.Heading)
            { text.FontSize = Math.Max(18, 34 - node.Level * 3); text.FontWeight = FontWeights.SemiBold; text.Margin = new Thickness(0, 20, 0, 8); }
            if (node.Kind == NativeArticleNodeKind.Preformatted)
            { text.FontFamily = new FontFamily("Consolas"); text.TextWrapping = TextWrapping.NoWrap; return new ScrollViewer { Content = text, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled }; }
            return text;
        }

        private RichTextBlock Text(IEnumerable<NativeArticleNode> nodes)
        {
            var text = new RichTextBlock { IsTextSelectionEnabled = true, FontSize = 18, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 8) };
            var paragraph = new Paragraph();
            foreach (var node in nodes) AppendInline(paragraph.Inlines, node, text);
            text.Blocks.Add(paragraph);
            return text;
        }

        private void AppendInline(InlineCollection inlines, NativeArticleNode node, FrameworkElement anchor)
        {
            Register(node.Id, anchor);
            if (node.Kind == NativeArticleNodeKind.Text) { inlines.Add(new Run { Text = node.Text ?? "" }); return; }
            if (node.Kind == NativeArticleNodeKind.LineBreak) { inlines.Add(new LineBreak()); return; }
            if (node.Kind == NativeArticleNodeKind.Image) { inlines.Add(new InlineUIContainer { Child = Image(node) }); return; }
            if (node.Kind == NativeArticleNodeKind.Unsupported) { inlines.Add(new Run { Text = string.IsNullOrWhiteSpace(node.Text) ? "[Content available in Web view]" : node.Text }); return; }
            if (IsBlock(node)) { inlines.Add(new InlineUIContainer { Child = RenderBlock(node) }); return; }
            Span span;
            if (node.Kind == NativeArticleNodeKind.Link && node.Uri != null)
            {
                var image = Descendants(node).FirstOrDefault(n => n.Kind == NativeArticleNodeKind.Image);
                if (image != null)
                {
                    var button = new Button { Content = Image(image), Padding = new Thickness(0), BorderThickness = new Thickness(0), Background = new SolidColorBrush(Colors.Transparent) };
                    button.Click += async (s, e) => await Navigate(node.Uri);
                    inlines.Add(new InlineUIContainer { Child = button }); return;
                }
                var link = new Hyperlink();
                link.Click += async (s, e) => await Navigate(node.Uri);
                span = link;
            }
            else if (node.Kind == NativeArticleNodeKind.Strong) span = new Bold();
            else if (node.Kind == NativeArticleNodeKind.Emphasis) span = new Italic();
            else if (node.Kind == NativeArticleNodeKind.Underline) span = new Underline();
            else span = new Span();
            if (node.Kind == NativeArticleNodeKind.Code) span.FontFamily = new FontFamily("Consolas");
            foreach (var child in node.Children) AppendInline(span.Inlines, child, anchor);
            inlines.Add(span);
        }

        private FrameworkElement Image(NativeArticleNode node)
        {
            if (node.Uri == null) return new TextBlock { Text = node.Text ?? "[Image]", TextWrapping = TextWrapping.Wrap };
            var image = new Image { Width = node.ImageWidth > 0 ? node.ImageWidth : double.NaN, MaxWidth = Math.Max(80, _root.ActualWidth > 0 ? _root.ActualWidth - 40 : 320), MaxHeight = 600, Stretch = Stretch.Uniform, Margin = new Thickness(0, 8, 0, 8) };
            Windows.UI.Xaml.Automation.AutomationProperties.SetName(image, node.Text ?? "");
            _images.Add(image);
            int version = _version;
            image.Loaded += async (s, e) =>
            {
                if (image.Source != null) return;
                try
                {
                    string resolved = _resolveImage == null ? null : await _resolveImage(node.Uri.AbsoluteUri);
                    if (version != _version) return;
                    Uri uri;
                    image.Source = new BitmapImage(Uri.TryCreate(resolved, UriKind.Absolute, out uri) ? uri : node.Uri);
                }
                catch (Exception ex) { System.Diagnostics.Debug.WriteLine("Native reader image: " + ex.Message); }
            };
            return image;
        }

        private FrameworkElement Table(NativeArticleNode node)
        {
            var grid = new Grid { Margin = new Thickness(0, 8, 0, 8) };
            var occupied = new HashSet<string>();
            var rows = Descendants(node).Where(n => n.Kind == NativeArticleNodeKind.TableRow).Take(1024).ToList();
            int maxColumn = 0;
            for (int row = 0; row < rows.Count; row++)
            {
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                int column = 0;
                foreach (var cell in rows[row].Children.Where(n => n.Kind == NativeArticleNodeKind.TableCell))
                {
                    while (column < 64 && Enumerable.Range(column, Math.Min(cell.ColumnSpan, 64 - column)).Any(c => occupied.Contains(row + ":" + c))) column++;
                    if (column >= 64) break;
                    int columns = Math.Min(cell.ColumnSpan, 64 - column), rowSpan = Math.Min(cell.RowSpan, rows.Count - row);
                    for (int r = row; r < row + rowSpan; r++) for (int c = column; c < column + columns; c++) occupied.Add(r + ":" + c);
                    var panel = new StackPanel { MinWidth = 80, MaxWidth = 360 };
                    AppendBlocks(panel, cell.Children);
                    var border = new Border { Child = panel, BorderThickness = new Thickness(0.5), BorderBrush = new SolidColorBrush(Colors.Gray), Padding = new Thickness(8) };
                    Grid.SetRow(border, row); Grid.SetColumn(border, column); Grid.SetColumnSpan(border, columns); Grid.SetRowSpan(border, rowSpan);
                    Register(cell.Id, border);
                    grid.Children.Add(border);
                    column += columns;
                    maxColumn = Math.Max(maxColumn, column);
                }
            }
            for (int column = 0; column < maxColumn; column++) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var wrapper = new StackPanel();
            foreach (var caption in node.Children.Where(c => c.IsBlock)) wrapper.Children.Add(RenderBlock(caption));
            wrapper.Children.Add(new ScrollViewer { Content = grid, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled });
            return wrapper;
        }

        private static IEnumerable<NativeArticleNode> Descendants(NativeArticleNode node)
        {
            foreach (var child in node.Children)
            { yield return child; if (child.Kind != NativeArticleNodeKind.Table) foreach (var nested in Descendants(child)) yield return nested; }
        }

        private void Register(string id, FrameworkElement element)
        { if (!string.IsNullOrEmpty(id) && !_anchors.ContainsKey(id)) _anchors.Add(id, element); }

        private async Task Navigate(Uri uri)
        {
            try
            {
                FrameworkElement anchor;
                if (_pageUri != null && uri.Scheme == _pageUri.Scheme && uri.Host == _pageUri.Host && uri.Port == _pageUri.Port &&
                    uri.AbsolutePath == _pageUri.AbsolutePath && uri.Query == _pageUri.Query &&
                    !string.IsNullOrEmpty(uri.Fragment) && _anchors.TryGetValue(Uri.UnescapeDataString(uri.Fragment.TrimStart('#')), out anchor))
                { _scroll.ChangeView(null, anchor.TransformToVisual(_root).TransformPoint(new Point()).Y, null); return; }
                if (_navigate != null) await _navigate(uri);
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine("Native reader navigation: " + ex.Message); }
        }
    }
}
