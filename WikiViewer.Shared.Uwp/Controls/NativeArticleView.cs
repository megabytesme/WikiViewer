using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using WikiViewer.Core.Models;
using WikiViewer.Core.Services;
using Windows.Foundation;
using Windows.Storage;
using Windows.Storage.Streams;
using Windows.UI;
using Windows.UI.Text;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Documents;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Imaging;

namespace WikiViewer.Shared.Uwp.Controls
{
    /// <summary>An opt-in reading view for server-expanded content. Page scripts and CSS are not executed.</summary>
    public sealed class NativeArticleView : UserControl
    {
        private readonly ArticleFlowPanel _root = new ArticleFlowPanel { MaxWidth = 1100, Margin = new Thickness(20, 56, 20, 80), HorizontalAlignment = HorizontalAlignment.Stretch };
        private readonly Dictionary<string, FrameworkElement> _anchors = new Dictionary<string, FrameworkElement>(StringComparer.Ordinal);
        private readonly List<Image> _images = new List<Image>();
        private readonly List<MediaElement> _media = new List<MediaElement>();
        private Func<Uri, Task> _navigate;
        private Func<string, Task<string>> _resolveImage;
        private int _version;
        private Uri _pageUri;
        private readonly ScrollViewer _scroll = new ScrollViewer { HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        public Thickness ContentMargin { get => _root.Margin; set => _root.Margin = value; }
        public string UnsupportedContentMessage { get; set; } = "Some content is available in Web view. Use the reader button to switch views.";

        public NativeArticleView()
        {
            _scroll.Content = _root;
            Content = _scroll;
            Unloaded += (s, e) => ClearArticle();
            _root.SizeChanged += (s, e) => { foreach (var image in _images.ToArray()) LimitImageWidth(image); };
        }

        public void ShowArticle(ArticleContent content, Uri pageUri, Func<Uri, Task> navigate, Func<string, Task<string>> resolveImage)
        {
            try
            {
            ClearArticle();
            var document = NativeArticleDocumentService.Parse(content, pageUri);
            RenderDocument(document, pageUri, navigate, resolveImage);
            }
            catch (Exception ex) { _ = LogFailureAsync(ex); throw; }
        }

        public async Task<bool> ShowArticleAsync(ArticleContent content, Uri pageUri, Func<Uri, Task> navigate, Func<string, Task<string>> resolveImage)
        {
            ClearArticle();
            int version = _version;
            try
            {
                double? width = ActualWidth > 0 ? (double?)ActualWidth : null;
                var document = await Task.Run(() => NativeArticleDocumentService.Parse(content, pageUri, width));
                if (version != _version) return false;
                RenderDocument(document, pageUri, navigate, resolveImage);
                return true;
            }
            catch (Exception ex)
            {
                if (version != _version) return false;
                _ = LogFailureAsync(ex); throw;
            }
        }

        private void RenderDocument(NativeArticleDocument document, Uri pageUri, Func<Uri, Task> navigate, Func<string, Task<string>> resolveImage)
        {
            _navigate = navigate;
            _resolveImage = resolveImage;
            _pageUri = pageUri;
            if (document.UnsupportedFeatures.Count > 0)
                _root.Children.Add(new TextBlock { Text = UnsupportedContentMessage, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12), Opacity = 0.75 });
            AppendBlocks(_root, document.Nodes);
            _root.UpdateLayout();
            _scroll.ChangeView(null, 0, null, true);
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
            foreach (var media in _media) { media.Stop(); media.Source = null; }
            _media.Clear();
            _root.Children.Clear();
            _anchors.Clear();
            _images.Clear();
        }

        private static bool IsBlock(NativeArticleNode node) => node.IsBlock || node.Style.Float != null ||
            node.Kind == NativeArticleNodeKind.Paragraph || node.Kind == NativeArticleNodeKind.Heading || node.Kind == NativeArticleNodeKind.List ||
            node.Kind == NativeArticleNodeKind.Table || node.Kind == NativeArticleNodeKind.Quote || node.Kind == NativeArticleNodeKind.Preformatted || node.Kind == NativeArticleNodeKind.Rule;

        private void AppendBlocks(Panel panel, IEnumerable<NativeArticleNode> nodes, NativeArticleNode owner = null)
        {
            var inline = new List<NativeArticleNode>();
            foreach (var node in nodes)
            {
                if (node.Style.Hidden) continue;
                PromoteImageLayout(node);
                if (!IsBlock(node)) { inline.Add(node); continue; }
                FlushInline(panel, inline, owner);
                var block = RenderBlock(node);
                Register(node.Id, block);
                panel.Children.Add(block);
            }
            FlushInline(panel, inline, owner);
        }

        private void FlushInline(Panel panel, List<NativeArticleNode> nodes, NativeArticleNode owner)
        {
            if (nodes.Count == 0) return;
            if (nodes.Any(n => n.Kind != NativeArticleNodeKind.Text || !string.IsNullOrWhiteSpace(n.Text))) panel.Children.Add(Text(nodes, owner));
            nodes.Clear();
        }

        private FrameworkElement RenderBlock(NativeArticleNode node)
        {
            var content = node.Style.Collapsible ? Collapsible(node) : RenderBlockCore(node);
            return ArticleVisualStyle.Block(content, node);
        }

        private FrameworkElement Collapsible(NativeArticleNode node)
        {
            var panel = new StackPanel();
            var body = new ContentControl { HorizontalContentAlignment = HorizontalAlignment.Stretch };
            string title = string.Concat(Descendants(node).Where(n => n.TagName == "summary" || n.TagName == "caption" || n.Kind == NativeArticleNodeKind.Heading)
                .Take(1).Select(n => PlainText(n)));
            if (string.IsNullOrWhiteSpace(title)) title = node.Kind == NativeArticleNodeKind.Table ? "Table" : "Section";
            var toggle = new ToggleButton { Content = title, IsChecked = !node.Style.InitiallyCollapsed, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left };
            Action update = () =>
            {
                bool open = toggle.IsChecked == true;
                if (open && body.Content == null) body.Content = RenderBlockCore(node);
                body.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
            };
            toggle.Click += (s, e) =>
            {
                try { update(); }
                catch (Exception ex)
                {
                    _ = LogFailureAsync(ex);
                    body.Content = new TextBlock { Text = "This section requires Web view. Use the reader button to switch views.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 8) };
                    body.Visibility = Visibility.Visible;
                }
            };
            panel.Children.Add(toggle); panel.Children.Add(body); update();
            return panel;
        }

        private FrameworkElement RenderBlockCore(NativeArticleNode node)
        {
            if (node.Kind == NativeArticleNodeKind.Image) return Image(node);
            if (node.Kind == NativeArticleNodeKind.Media) return Media(node);
            if (node.Kind == NativeArticleNodeKind.Rule) return new Border { Height = 1, Background = new SolidColorBrush(Colors.Gray), Margin = new Thickness(0, 12, 0, 12) };
            if (node.Kind == NativeArticleNodeKind.Table) return Table(node);
            if (node.Kind == NativeArticleNodeKind.List)
            {
                var list = new ArticleFlowPanel { ColumnCount = node.Style.ColumnCount, Margin = new Thickness(8, 4, 0, 8) };
                int number = node.ListStart;
                foreach (var item in node.Children.Where(c => c.Kind == NativeArticleNodeKind.ListItem))
                {
                    if (item.ListValue.HasValue) number = item.ListValue.Value;
                    var row = new Grid();
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    row.Children.Add(new TextBlock { Text = node.Text == "definition" ? "" : node.Ordered ? number++.ToString() + "." : "•", MinWidth = 28, FontSize = 18, Margin = new Thickness(0, 8, 4, 0) });
                    var content = new ArticleFlowPanel { Margin = new Thickness(item.Text == "definition" ? 16 : 0, 0, 0, 0) };
                    Grid.SetColumn(content, 1);
                    AppendBlocks(content, item.Children, item);
                    row.Children.Add(content);
                    Register(item.Id, row);
                    list.Children.Add(row);
                }
                return list;
            }
            if (node.Kind == NativeArticleNodeKind.Container || node.Kind == NativeArticleNodeKind.Quote)
            {
                if (node.Style.Display == "grid" || node.Style.Display == "gallery" || node.Style.Display == "flex" && node.Style.Declarations.ContainsKey("flex-direction") == false)
                {
                    var tiles = new ArticleTilePanel { Columns = node.Style.GridColumns > 0 ? node.Style.GridColumns : node.Style.Display == "flex" && !node.Style.Wrap ? node.Children.Count(IsBlock) : 0,
                        Gap = node.Style.Gap?.Resolve(800) ?? 8 };
                    AppendBlocks(tiles, node.Children, node);
                    return tiles;
                }
                var panel = new ArticleFlowPanel { ColumnCount = node.Style.ColumnCount, Margin = new Thickness(node.Kind == NativeArticleNodeKind.Quote ? 20 : 0, 0, 0, 0),
                    FlowDirection = node.RightToLeft ? FlowDirection.RightToLeft : FlowDirection.LeftToRight };
                if (node.Style.ColumnCount > 1 && node.Children.Count(n => n.Kind == NativeArticleNodeKind.List) == 1)
                {
                    node.Children.First(n => n.Kind == NativeArticleNodeKind.List).Style.ColumnCount = node.Style.ColumnCount;
                    panel.ColumnCount = 1;
                }
                AppendBlocks(panel, node.Children.Where(n => node.TagName != "details" || n.TagName != "summary"), node);
                return panel;
            }
            var text = Text(node.Children, node);
            text.FlowDirection = node.RightToLeft ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
            if (node.Kind == NativeArticleNodeKind.Heading)
            { text.FontSize = Math.Max(18, 34 - node.Level * 3); text.FontWeight = FontWeights.SemiBold; text.Margin = new Thickness(0, 20, 0, 8); }
            ArticleVisualStyle.Text(text, node);
            if (node.Kind == NativeArticleNodeKind.Preformatted)
            { text.FontFamily = new FontFamily("Consolas"); text.TextWrapping = TextWrapping.NoWrap; return new ScrollViewer { Content = text, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled }; }
            return text;
        }

        private RichTextBlock Text(IEnumerable<NativeArticleNode> nodes, NativeArticleNode owner = null)
        {
            var text = new RichTextBlock { IsTextSelectionEnabled = true, FontSize = 18, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 8) };
            var paragraph = new Paragraph();
            foreach (var node in nodes) AppendInline(paragraph.Inlines, node, text);
            text.Blocks.Add(paragraph);
            if (owner != null) ArticleVisualStyle.Text(text, owner);
            return text;
        }

        private void AppendInline(InlineCollection inlines, NativeArticleNode node, FrameworkElement anchor)
        {
            if (node.Style.Hidden) return;
            Register(node.Id, anchor);
            if (node.Kind == NativeArticleNodeKind.Text) { inlines.Add(new Run { Text = node.Text ?? "" }); return; }
            if (node.Kind == NativeArticleNodeKind.LineBreak) { inlines.Add(new LineBreak()); return; }
            if (node.Kind == NativeArticleNodeKind.Image) { inlines.Add(new InlineUIContainer { Child = Image(node) }); return; }
            if (node.Kind == NativeArticleNodeKind.Media) { inlines.Add(new InlineUIContainer { Child = Media(node) }); return; }
            if (node.Kind == NativeArticleNodeKind.Unsupported)
            {
                inlines.Add(new Run { Text = string.IsNullOrWhiteSpace(node.Text) ? "[Content available in Web view]" : node.Text });
                foreach (var imageNode in Descendants(node).Where(n => n.Kind == NativeArticleNodeKind.Image)) inlines.Add(new InlineUIContainer { Child = Image(imageNode) });
                return;
            }
            if (IsBlock(node)) { inlines.Add(new InlineUIContainer { Child = RenderBlock(node) }); return; }
            Span span;
            if (node.Kind == NativeArticleNodeKind.Link && node.Uri != null)
            {
                var image = Descendants(node).FirstOrDefault(n => n.Kind == NativeArticleNodeKind.Image);
                if (image != null)
                {
                    var label = new ArticleFlowPanel();
                    // Preserve every image and text node in the link, including gallery/image labels.
                    AppendBlocks(label, node.Children, node);
                    var button = new Button { Content = label, Padding = new Thickness(0), BorderThickness = new Thickness(0), Background = new SolidColorBrush(Colors.Transparent) };
                    button.Click += async (s, e) => await Navigate(node.Uri);
                    inlines.Add(new InlineUIContainer { Child = button }); return;
                }
                var link = new Hyperlink();
                link.Click += async (s, e) => await Navigate(node.Uri);
                span = link;
            }
            else if (node.Kind == NativeArticleNodeKind.Strong) span = new Bold();
            else if (node.Kind == NativeArticleNodeKind.Emphasis) span = new Italic();
            else if (node.Kind == NativeArticleNodeKind.Underline || node.Style.TextDecoration != null && node.Style.TextDecoration.Contains("underline")) span = new Underline();
            else span = new Span();
            if (node.Kind == NativeArticleNodeKind.Code) span.FontFamily = new FontFamily("Consolas");
            ArticleVisualStyle.Inline(span, node);
            foreach (var child in node.Children) AppendInline(span.Inlines, child, anchor);
            inlines.Add(span);
        }

        private FrameworkElement Image(NativeArticleNode node)
        {
            if (node.Uri == null && node.SvgContent == null) return new TextBlock { Text = node.Text ?? "[Image]", TextWrapping = TextWrapping.Wrap };
#if !UWP_1809
            if (node.SvgContent != null || IsSvg(node.Uri))
                return new TextBlock { Text = (node.Text ?? "Vector image") + " (available in Web view)", TextWrapping = TextWrapping.Wrap };
#endif
            var image = new Image { Width = node.ImageWidth > 0 ? node.ImageWidth : double.NaN, MaxWidth = Math.Max(80, _root.ActualWidth > 0 ? _root.ActualWidth - 40 : 320), MaxHeight = 600, Stretch = Stretch.Uniform, Margin = new Thickness(0, 8, 0, 8) };
            var host = new Grid(); host.Children.Add(image);
            var fallback = new TextBlock { Text = (node.Text ?? "Image") + " (available in Web view)", TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed };
            host.Children.Add(fallback);
            Action failed = () => { image.Visibility = Visibility.Collapsed; fallback.Visibility = Visibility.Visible; };
            image.ImageFailed += (s, e) => failed();
            Windows.UI.Xaml.Automation.AutomationProperties.SetName(image, node.Text ?? "");
            _images.Add(image);
            int version = _version;
            var resolveImage = _resolveImage;
            image.Loaded += async (s, e) =>
            {
                LimitImageWidth(image);
                if (image.Source != null || version != _version) return;
                try
                {
#if UWP_1809
                    if (node.SvgContent != null)
                    {
                        var svg = new SvgImageSource(); image.Source = svg;
                        using (var stream = new InMemoryRandomAccessStream())
                        {
                            using (var writer = new DataWriter(stream.GetOutputStreamAt(0))) { writer.WriteBytes(System.Text.Encoding.UTF8.GetBytes(node.SvgContent)); await writer.StoreAsync(); }
                            stream.Seek(0);
                            if (await svg.SetSourceAsync(stream) != SvgImageSourceLoadStatus.Success && version == _version) failed();
                        }
                        return;
                    }
#endif
                    string resolved = resolveImage == null ? null : await resolveImage(node.Uri.AbsoluteUri);
                    if (version != _version) return;
                    Uri uri;
                    uri = Uri.TryCreate(resolved, UriKind.Absolute, out uri) ? uri : node.Uri;
#if UWP_1809
                    if (IsSvg(node.Uri))
                    {
                        var svg = new SvgImageSource(uri); svg.OpenFailed += (sender, args) => { if (version == _version) failed(); }; image.Source = svg;
                    }
                    else
#endif
                    image.Source = new BitmapImage(uri);
                }
                catch (Exception ex) { if (version == _version) failed(); System.Diagnostics.Debug.WriteLine("Native reader image: " + ex.Message); }
            };
            return host;
        }

        private FrameworkElement Media(NativeArticleNode node)
        {
            var panel = new StackPanel { Margin = new Thickness(0, 8, 0, 8) };
            if (node.PosterUri != null) panel.Children.Add(Image(new NativeArticleNode { Kind = NativeArticleNodeKind.Image, Uri = node.PosterUri, Text = "Video poster", ImageWidth = 480 }));
            var sources = node.MediaSources.OrderBy(s => s.ContentType.Contains("mp4") || s.ContentType.Contains("mpeg") ? 0 : 1).ToList();
            if (sources.Count > 0)
            {
                var media = new MediaElement { AutoPlay = false, AreTransportControlsEnabled = true, MaxHeight = node.TagName == "audio" ? 80 : 480, MinHeight = 64, Stretch = Stretch.Uniform };
                var choices = new ComboBox { ItemsSource = sources, DisplayMemberPath = "Label", SelectedIndex = 0, HorizontalAlignment = HorizontalAlignment.Stretch };
                if (sources.Count > 1) panel.Children.Add(choices);
                var load = new Button { Content = node.TagName == "audio" ? "Play audio" : "Play video" };
                var status = new TextBlock { TextWrapping = TextWrapping.Wrap };
                int version = _version;
                media.MediaFailed += (s, e) => { if (version == _version) status.Text = "Windows could not play this format. Choose another source or use Web view."; };
                load.Click += (s, e) => { if (version != _version) return; status.Text = ""; media.Source = ((NativeMediaSource)choices.SelectedItem).Uri; media.Play(); };
                choices.SelectionChanged += (s, e) => { media.Stop(); media.Source = null; };
                _media.Add(media); panel.Children.Add(media); panel.Children.Add(load); panel.Children.Add(status);
                var open = new HyperlinkButton { Content = "Open media file", NavigateUri = sources[0].Uri };
                panel.Children.Add(open);
            }
            else panel.Children.Add(new TextBlock { Text = "Media available in Web view.", TextWrapping = TextWrapping.Wrap });
            AppendBlocks(panel, node.Children, node);
            return panel;
        }

        private static bool IsSvg(Uri uri) => uri != null && (uri.AbsolutePath.EndsWith(".svg", StringComparison.OrdinalIgnoreCase) ||
            uri.AbsolutePath.IndexOf("/math/render/svg/", StringComparison.OrdinalIgnoreCase) >= 0 || uri.AbsolutePath.IndexOf("/math/render/svg", StringComparison.OrdinalIgnoreCase) >= 0);

        private void LimitImageWidth(Image image)
        {
            double width = _root.ActualWidth > 0 ? _root.ActualWidth : 320;
            for (var parent = VisualTreeHelper.GetParent(image); parent != null; parent = VisualTreeHelper.GetParent(parent))
            {
                var element = parent as FrameworkElement;
                // Inline/button ancestors can measure to zero before the bitmap loads.
                // Their current size must not permanently constrain their own image.
                if (element != null)
                {
                    if (!double.IsInfinity(element.MaxWidth) && element.MaxWidth > 0) width = Math.Min(width, element.MaxWidth);
                    if (!double.IsNaN(element.Width) && element.Width > 0) width = Math.Min(width, element.Width);
                }
                if (parent == _root) break;
            }
            image.MaxWidth = Math.Max(1, width - 4);
        }

        private FrameworkElement Table(NativeArticleNode node)
        {
            var grid = new Grid { Margin = new Thickness(0, 8, 0, 8) };
            var occupied = new HashSet<string>();
            var rows = Descendants(node).Where(n => n.Kind == NativeArticleNodeKind.TableRow).Take(1025).ToList();
            if (rows.Count > 1024) throw new NotSupportedException("This table requires Web view.");
            int maxColumn = 0;
            for (int row = 0; row < rows.Count; row++)
            {
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                int column = 0;
                foreach (var cell in rows[row].Children.Where(n => n.Kind == NativeArticleNodeKind.TableCell))
                {
                    while (column < 64 && Enumerable.Range(column, Math.Min(cell.ColumnSpan, 64 - column)).Any(c => occupied.Contains(row + ":" + c))) column++;
                    if (column >= 64) throw new NotSupportedException("This wide table requires Web view.");
                    int columns = Math.Min(cell.ColumnSpan, 64 - column), rowSpan = Math.Min(cell.RowSpan, rows.Count - row);
                    for (int r = row; r < row + rowSpan; r++) for (int c = column; c < column + columns; c++) occupied.Add(r + ":" + c);
                    var panel = new StackPanel { MinWidth = 80, MaxWidth = 360 };
                    AppendBlocks(panel, cell.Children, cell);
                    var border = new Border { Child = panel, BorderThickness = new Thickness(0.5), BorderBrush = ArticleVisualStyle.Brush(cell.Style.BorderColor ?? node.Style.BorderColor) ?? new SolidColorBrush(Colors.Gray),
                        Background = ArticleVisualStyle.Brush(cell.Style.Background ?? rows[row].Style.Background ?? node.Style.Background),
                        Padding = ArticleVisualStyle.Edges(cell.Style.Padding, new Thickness(8)) };
                    if (cell.Style.VerticalAlignment == "top") panel.VerticalAlignment = VerticalAlignment.Top;
                    else if (cell.Style.VerticalAlignment == "bottom") panel.VerticalAlignment = VerticalAlignment.Bottom;
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

        private static string PlainText(NativeArticleNode node) => node.Kind == NativeArticleNodeKind.Text ? node.Text : string.Concat(node.Children.Select(PlainText));

        private static void PromoteImageLayout(NativeArticleNode node)
        {
            if (!(node.Style.IsThumbnail || node.Style.Float != null || node.TagName == "figure" || node.Kind == NativeArticleNodeKind.Paragraph)) return;
            var images = Descendants(node).Where(n => n.Kind == NativeArticleNodeKind.Image).ToList();
            if (images.Count != 1) return;
            var image = images[0];
            if (node.Style.Width == null && (node.Style.IsThumbnail || node.Style.Float != null || node.TagName == "figure") && image.ImageWidth > 0)
                node.Style.Width = new ArticleLength { Value = image.ImageWidth + 20, Unit = "px" };
            if (node.Style.PositionExplicit && node.Style.Float == null) { image.Style.Float = null; return; }
            if (node.Style.Float != null)
            {
                if (!node.Style.PositionExplicit && image.HasSourceLayout && image.Style.Float != null) node.Style.Float = image.Style.Float;
                image.Style.Float = null; return;
            }
            bool onlyImage = node.Kind != NativeArticleNodeKind.Paragraph || string.IsNullOrWhiteSpace(PlainText(node));
            if (onlyImage && image.Style.Float != null)
            {
                node.Style.Float = image.Style.Float;
                node.Style.Width = image.Style.Width ?? (image.ImageWidth > 0 ? new ArticleLength { Value = image.ImageWidth + 20, Unit = "px" } : null);
                image.Style.Float = null;
            }
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
