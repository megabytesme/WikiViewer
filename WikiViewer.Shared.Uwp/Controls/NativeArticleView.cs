using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Threading;
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
using Windows.UI.Xaml.Markup;
using Windows.UI.ViewManagement;

namespace WikiViewer.Shared.Uwp.Controls
{
    /// <summary>An opt-in reading view for server-expanded content. Page scripts and CSS are not executed.</summary>
    public sealed class NativeArticleView : UserControl
    {
        private readonly ArticleFlowPanel _root = new ArticleFlowPanel { MaxWidth = 1100, Margin = new Thickness(20, 56, 20, 80), HorizontalAlignment = HorizontalAlignment.Stretch };
        private readonly ArticleThemePalette _palette = new ArticleThemePalette();
        private readonly AccessibilitySettings _accessibility = new AccessibilitySettings();
        private readonly UISettings _uiSettings = new UISettings();
        private bool _themeSubscribed;
        private readonly Dictionary<string, FrameworkElement> _anchors = new Dictionary<string, FrameworkElement>(StringComparer.Ordinal);
        private readonly List<Image> _images = new List<Image>();
        private readonly List<MediaElement> _media = new List<MediaElement>();
        private readonly Dictionary<string, NativeArticleSection> _anchorSections = new Dictionary<string, NativeArticleSection>(StringComparer.Ordinal);
        private readonly List<RenderScope> _scopes = new List<RenderScope>();
        private RenderScope _scope;
        private ListView _sections;
        private sealed class RenderScope
        {
            public bool Active = true;
            public readonly CancellationTokenSource Cancellation = new CancellationTokenSource();
            public readonly List<Image> Images = new List<Image>();
            public readonly List<MediaElement> Media = new List<MediaElement>();
            public readonly Dictionary<string, FrameworkElement> Anchors = new Dictionary<string, FrameworkElement>();
        }
        private Func<Uri, Task> _navigate;
        private Func<string, CancellationToken, Task<string>> _resolveImage;
        private CancellationTokenSource _assetCancellation = new CancellationTokenSource();
        private int _version;
        private bool _interacted;
        public bool CanUpdateLayout => !_interacted && (_sections != null ? (FindScrollViewer(_sections)?.VerticalOffset ?? 0) < 1 : _scroll.VerticalOffset < 1);
        private Uri _pageUri;
        private readonly ScrollViewer _scroll = new ScrollViewer { HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        public Thickness ContentMargin { get => _root.Margin; set => _root.Margin = value; }
        public string UnsupportedContentMessage { get; set; } = "Some content is available in Web view. Use the reader button to switch views.";

        public NativeArticleView()
        {
            AddHandler(UIElement.PointerPressedEvent, new Windows.UI.Xaml.Input.PointerEventHandler((s, e) => _interacted = true), true);
            KeyDown += (s, e) => _interacted = true;
            _scroll.Content = _root;
            Content = _scroll;
            Loaded += (s, e) =>
            {
                UpdatePalette();
                if (_themeSubscribed) return;
                _themeSubscribed = true;
                _accessibility.HighContrastChanged += OnHighContrastChanged;
                _uiSettings.ColorValuesChanged += OnSystemColorsChanged;
            };
            Unloaded += (s, e) =>
            {
                _themeSubscribed = false;
                _accessibility.HighContrastChanged -= OnHighContrastChanged;
                _uiSettings.ColorValuesChanged -= OnSystemColorsChanged;
                ClearArticle();
            };
#if UWP_1809
            ActualThemeChanged += (s, e) => UpdatePalette();
#endif
            _root.SizeChanged += (s, e) => { foreach (var image in _images.ToArray()) LimitImageWidth(image); };
            SizeChanged += (s, e) => { foreach (var image in _images.ToArray()) LimitImageWidth(image); };
        }

        private void UpdatePalette() => _palette.Update(this, _accessibility.HighContrast, _uiSettings);
        private async void OnHighContrastChanged(AccessibilitySettings sender, object args) => await RefreshPaletteAsync();
        private async void OnSystemColorsChanged(UISettings sender, object args) => await RefreshPaletteAsync();
        private async Task RefreshPaletteAsync()
        {
            if (!_themeSubscribed) return;
            try { await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () => { if (_themeSubscribed) UpdatePalette(); }); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine("Native theme update: " + ex.GetType().Name); }
        }

        public void ShowArticle(ArticleContent content, Uri pageUri, Func<Uri, Task> navigate, Func<string, CancellationToken, Task<string>> resolveImage)
        {
            try
            {
            ClearArticle();
            var document = NativeArticleDocumentService.Parse(content, pageUri);
            RenderDocument(document, pageUri, navigate, resolveImage);
            }
            catch (Exception ex) { _ = LogFailureAsync(ex); throw; }
        }

        public async Task<bool> ShowArticleAsync(ArticleContent content, Uri pageUri, Func<Uri, Task> navigate, Func<string, CancellationToken, Task<string>> resolveImage)
        {
            ClearArticle();
            int version = _version;
            try
            {
                double? width = ActualWidth > 0 ? (double?)ActualWidth : null;
                var document = await NativeDocumentCache.GetAsync(content, pageUri, width);
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

        public async Task<bool> TryUpdateStylesAsync(ArticleContent content)
        {
            if (!CanUpdateLayout || _pageUri == null) return false;
            int version = _version;
            var page = _pageUri; var navigate = _navigate; var resolve = _resolveImage;
            double? width = ActualWidth > 0 ? (double?)ActualWidth : null;
            // Keep readable controls in place while preparing optional styles. Input or
            // navigation during parsing means the prepared layout waits for an explicit reload.
            var document = await NativeDocumentCache.GetAsync(content, page, width);
            if (version != _version || !CanUpdateLayout) return false;
            ClearArticle();
            RenderDocument(document, page, navigate, resolve);
            return true;
        }

        private void RenderDocument(NativeArticleDocument document, Uri pageUri, Func<Uri, Task> navigate, Func<string, CancellationToken, Task<string>> resolveImage)
        {
            _navigate = navigate;
            _resolveImage = resolveImage;
            _pageUri = pageUri;
            UpdatePalette();
            _scroll.Background = _palette.Get(document.Background, ArticleColorRole.Background);
            var foreground = _palette.Get(document.Foreground, ArticleColorRole.Text, document.ColorContext);
            if (foreground != null) { Foreground = foreground; _scroll.Foreground = foreground; }
            else { ClearValue(Control.ForegroundProperty); _scroll.ClearValue(Control.ForegroundProperty); }
            var sections = ArticleSectionService.Split(document.Nodes);
            if (document.Nodes.Count >= 100 && sections.Count >= 4)
            {
                ShowSections(sections, document.UnsupportedFeatures.Count > 0);
                _sections.Background = _palette.Get(document.Background, ArticleColorRole.Background);
                if (foreground != null) _sections.Foreground = foreground;
                return;
            }
            if (document.UnsupportedFeatures.Count > 0)
                _root.Children.Add(new TextBlock { Text = UnsupportedContentMessage, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12), Opacity = 0.75 });
            AppendBlocks(_root, document.Nodes);
            _root.UpdateLayout();
            _scroll.ChangeView(null, 0, null, true);
        }

        private void ShowSections(List<NativeArticleSection> sections, bool unsupported)
        {
            foreach (var section in sections) foreach (string id in section.Anchors) if (!_anchorSections.ContainsKey(id)) _anchorSections[id] = section;
            _sections = new ListView { SelectionMode = ListViewSelectionMode.None, IsItemClickEnabled = false,
                HorizontalContentAlignment = HorizontalAlignment.Stretch, IsTabStop = false };
            Windows.UI.Xaml.Automation.AutomationProperties.SetAutomationId(_sections, "ArticleSections");
            Windows.UI.Xaml.Automation.AutomationProperties.SetName(_sections, "Article sections");
            _sections.ItemsPanel = (ItemsPanelTemplate)XamlReader.Load("<ItemsPanelTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'><ItemsStackPanel/></ItemsPanelTemplate>");
            _sections.ItemTemplate = (DataTemplate)XamlReader.Load("<DataTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'><ContentControl HorizontalContentAlignment='Stretch'/></DataTemplate>");
            var style = new Style(typeof(ListViewItem));
            style.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));
            style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(0)));
            style.Setters.Add(new Setter(FrameworkElement.MinHeightProperty, 0d));
            _sections.ItemContainerStyle = style;
            if (unsupported) _sections.Header = new TextBlock { Text = UnsupportedContentMessage, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(20, 56, 20, 8) };
            _sections.ContainerContentChanging += (sender, args) =>
            {
                if (args.Phase == 0 && args.ItemContainer.ContentTemplateRoot == null && !args.InRecycleQueue)
                    args.RegisterUpdateCallback((list, next) => RenderSection(next, sections.Count));
                else RenderSection(args, sections.Count);
            };
            _sections.ItemsSource = sections;
            Content = _sections;
        }

        private void RenderSection(ContainerContentChangingEventArgs args, int count)
        {
            var host = args.ItemContainer.ContentTemplateRoot as ContentControl;
            if (host == null) return;
            var previous = host.Tag as RenderScope;
            if (previous != null) ReleaseScope(previous);
            host.Content = null; host.Tag = null;
            args.Handled = true;
            if (args.InRecycleQueue) return;
            var section = args.Item as NativeArticleSection;
            if (section == null) return;
            Windows.UI.Xaml.Automation.AutomationProperties.SetName(args.ItemContainer, section.Title);
            var scope = new RenderScope(); _scopes.Add(scope); _scope = scope; host.Tag = scope;
            try
            {
                var margin = ContentMargin;
                var panel = new ArticleFlowPanel { MaxWidth = 1100, HorizontalAlignment = HorizontalAlignment.Stretch,
                    Margin = new Thickness(margin.Left, section.Index == 0 ? margin.Top : 0, margin.Right, section.Index == count - 1 ? margin.Bottom : 0) };
                AppendBlocks(panel, section.Nodes); host.Content = panel;
            }
            catch (Exception ex)
            {
                ReleaseScope(scope); _ = LogFailureAsync(ex);
                host.Content = new TextBlock { Text = UnsupportedContentMessage, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(20) };
            }
            finally { _scope = null; }
        }

        private void ReleaseScope(RenderScope scope)
        {
            if (!scope.Active) return;
            scope.Active = false;
            scope.Cancellation.Cancel(); scope.Cancellation.Dispose();
            foreach (var image in scope.Images) { _images.Remove(image); image.Source = null; }
            foreach (var media in scope.Media) { _media.Remove(media); media.Stop(); media.Source = null; }
            foreach (var anchor in scope.Anchors) { FrameworkElement current; if (_anchors.TryGetValue(anchor.Key, out current) && current == anchor.Value) _anchors.Remove(anchor.Key); }
            _scopes.Remove(scope);
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
            _assetCancellation.Cancel(); _assetCancellation.Dispose(); _assetCancellation = new CancellationTokenSource();
            _interacted = false;
            _version++;
            foreach (var scope in _scopes.ToArray()) ReleaseScope(scope);
            _scope = null; _anchorSections.Clear();
            if (_sections != null) { _sections.ItemsSource = null; _sections = null; Content = _scroll; }
            foreach (var media in _media) { media.Stop(); media.Source = null; }
            _media.Clear();
            _root.Children.Clear();
            _anchors.Clear();
            _images.Clear();
            _palette.Clear();
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
            return ArticleVisualStyle.Block(content, node, _palette);
        }

        private FrameworkElement Collapsible(NativeArticleNode node)
        {
            var panel = new StackPanel();
            var body = new ContentControl { HorizontalContentAlignment = HorizontalAlignment.Stretch };
            string title = string.Concat(Descendants(node).Where(n => n.TagName == "summary" || n.TagName == "caption" || n.Kind == NativeArticleNodeKind.Heading)
                .Take(1).Select(n => PlainText(n)));
            if (string.IsNullOrWhiteSpace(title)) title = node.Kind == NativeArticleNodeKind.Table ? "Table" : "Section";
            var toggle = new ToggleButton { Content = title, IsChecked = !node.Style.InitiallyCollapsed, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left };
            var scope = _scope;
            Action update = () =>
            {
                bool open = toggle.IsChecked == true;
                if (open && body.Content == null)
                {
                    var previous = _scope; _scope = scope;
                    try { body.Content = RenderBlockCore(node); }
                    finally { _scope = previous; }
                }
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
            if (node.Kind == NativeArticleNodeKind.Math) return new ScrollViewer { Content = MathView(node), HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled };
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
                if (node.Style.Position == "relative" && node.Children.Any(n => n.Style.Position == "absolute"))
                {
                    var overlay = new ArticlePositionPanel { PositionStyle = node.Style }; AppendBlocks(overlay, node.Children, node); return overlay;
                }
                if (node.Style.Display == "grid" || node.Style.Display == "gallery" || node.Style.Display == "flex" && node.Style.FlexDirection == "row")
                {
                    var tiles = new ArticleTilePanel { Columns = node.Style.GridColumns > 0 ? node.Style.GridColumns : node.Style.Display == "flex" && !node.Style.Wrap ? node.Children.Count(IsBlock) : 0,
                        Gap = node.Style.Gap?.Resolve(800) ?? 8, FlowStyle = node.Style };
                    foreach (var child in node.Children.Where(c => !c.Style.Hidden && (c.Kind != NativeArticleNodeKind.Text || !string.IsNullOrWhiteSpace(c.Text))))
                    {
                        var item = IsBlock(child) ? RenderBlock(child) : ArticleVisualStyle.Block(Text(new[] { child }, node), child, _palette);
                        Register(child.Id, item); tiles.Children.Add(item);
                    }
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
            ArticleVisualStyle.Text(text, node, _palette);
            if (node.Kind == NativeArticleNodeKind.Preformatted)
            { text.FontFamily = new FontFamily("Consolas"); text.TextWrapping = TextWrapping.NoWrap; return new ScrollViewer { Content = text, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled }; }
            if (node.Kind == NativeArticleNodeKind.Paragraph && node.Style.Width == null && node.Style.Background == null && node.Style.BorderWidth == null &&
                node.Style.Padding.Left == null && node.Style.Padding.Right == null && node.Style.Padding.Top == null && node.Style.Padding.Bottom == null)
                return new ArticleTextFlowPanel(text);
            return text;
        }

        private RichTextBlock Text(IEnumerable<NativeArticleNode> nodes, NativeArticleNode owner = null)
        {
            var text = new RichTextBlock { IsTextSelectionEnabled = true, FontSize = 18, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 8) };
            var interaction = new ArticleLinkInteraction(text);
            var paragraph = new Paragraph();
            foreach (var node in nodes) AppendInline(paragraph.Inlines, node, text, interaction);
            text.Blocks.Add(paragraph);
            if (owner != null) ArticleVisualStyle.Text(text, owner, _palette);
            return text;
        }

        private void AppendInline(InlineCollection inlines, NativeArticleNode node, FrameworkElement anchor, ArticleLinkInteraction interaction, bool insideLink = false)
        {
            if (node.Style.Hidden) return;
            Register(node.Id, anchor);
            if (node.Kind == NativeArticleNodeKind.Text) { inlines.Add(new Run { Text = node.Text ?? "" }); return; }
            if (node.Kind == NativeArticleNodeKind.LineBreak) { inlines.Add(new LineBreak()); return; }
            if (node.Kind == NativeArticleNodeKind.Image) { inlines.Add(new InlineUIContainer { Child = Image(node) }); return; }
            if (node.Kind == NativeArticleNodeKind.Media) { inlines.Add(new InlineUIContainer { Child = Media(node) }); return; }
            if (node.Kind == NativeArticleNodeKind.Math) { inlines.Add(new InlineUIContainer { Child = MathView(node) }); return; }
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
                if (Descendants(node).Any(n => n.Kind == NativeArticleNodeKind.Media))
                {
                    // Players have their own interactive controls and cannot live inside Hyperlink.
                    var player = new ArticleFlowPanel(); AppendBlocks(player, node.Children, node);
                    var destination = new HyperlinkButton { Content = "Open linked page" };
                    destination.Click += async (s, e) => await Navigate(node.Uri);
                    player.Children.Add(destination); inlines.Add(new InlineUIContainer { Child = player }); return;
                }
                var image = Descendants(node).FirstOrDefault(n => n.Kind == NativeArticleNodeKind.Image);
                if (image != null || Descendants(node).Any(n => n.Kind == NativeArticleNodeKind.Math))
                {
                    var label = new ArticleFlowPanel();
                    // Preserve every image and text node in the link, including gallery/image labels.
                    AppendBlocks(label, node.Children, node);
                    var button = new Button { Content = label, Padding = new Thickness(0), BorderThickness = new Thickness(0), Background = new SolidColorBrush(Colors.Transparent) };
                    button.Click += async (s, e) => await Navigate(node.Uri);
                    inlines.Add(new InlineUIContainer { Child = button }); return;
                }
                var link = new Hyperlink();
                int version = _version; var scope = _scope;
                interaction.Add(link, async () => { if (version == _version && scope?.Active != false) await Navigate(node.Uri); });
                span = link;
            }
            else if (node.Kind == NativeArticleNodeKind.Strong) span = new Bold();
            else if (node.Kind == NativeArticleNodeKind.Emphasis) span = new Italic();
            else if (node.Kind == NativeArticleNodeKind.Underline || node.Style.TextDecoration != null && node.Style.TextDecoration.Contains("underline")) span = new Underline();
            else span = new Span();
            if (node.Kind == NativeArticleNodeKind.Code) span.FontFamily = new FontFamily("Consolas");
            ArticleVisualStyle.Inline(span, node, _palette, insideLink);
            foreach (var child in node.Children) AppendInline(span.Inlines, child, anchor, interaction, insideLink || span is Hyperlink);
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
            var host = new Grid { MinHeight = Math.Min(32, node.ImageWidth > 0 ? node.ImageWidth : 32) }; host.Children.Add(image);
            double indicatorSize = Math.Min(32, node.ImageWidth > 0 ? node.ImageWidth : 32);
            var progress = new ProgressRing { Width = indicatorSize, Height = indicatorSize, IsActive = false, Visibility = Visibility.Collapsed, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            host.Children.Add(progress);
            Windows.UI.Xaml.Automation.AutomationProperties.SetName(progress, "Loading image");
            bool complete = false;
            Action finished = () => { complete = true; progress.IsActive = false; progress.Visibility = Visibility.Collapsed; host.MinHeight = 0; };
            var fallback = new TextBlock { Text = (node.Text ?? "Image") + " (available in Web view)", TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed };
            host.Children.Add(fallback);
            Action failed = () => { finished(); image.Visibility = Visibility.Collapsed; fallback.Visibility = Visibility.Visible; };
            Windows.UI.Xaml.Automation.AutomationProperties.SetName(image, node.Text ?? "");
            _images.Add(image);
            var scope = _scope; scope?.Images.Add(image);
            var cancellationToken = scope?.Cancellation.Token ?? _assetCancellation.Token;
            int version = _version;
            var resolveImage = _resolveImage;
            bool started = false;
            image.Loaded += async (s, e) =>
            {
                LimitImageWidth(image);
                if (started || version != _version || scope?.Active == false) return;
                started = true;
                _ = ShowImageProgressAsync(progress, () => !complete && version == _version && scope?.Active != false);
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
                        finished();
                        return;
                    }
#endif
                    var candidates = node.ImageSources.Count > 0 ? node.ImageSources : new List<Uri> { node.Uri };
                    foreach (var remote in candidates)
                    {
                        string resolved = null;
                        try { if (resolveImage != null) resolved = await resolveImage(remote.AbsoluteUri, cancellationToken); }
                        catch (Exception ex) { System.Diagnostics.Debug.WriteLine("Native reader image cache: " + ex.Message); }
                        if (version != _version || scope?.Active == false) return;
                        Uri local;
                        if (Uri.TryCreate(resolved, UriKind.Absolute, out local) && await LoadImageSourceAsync(image, local, IsSvg(remote))) { finished(); return; }
                        if (version != _version || scope?.Active == false) return;
                        if (!System.Net.NetworkInformation.NetworkInterface.GetIsNetworkAvailable())
                        { fallback.Text = (node.Text ?? "Image") + " — unavailable offline"; failed(); return; }
                        if (await LoadImageSourceAsync(image, remote, IsSvg(remote))) { finished(); return; }
                        if (version != _version || scope?.Active == false) return;
                    }
                    failed();
                }
                catch (Exception ex) { if (version == _version) failed(); System.Diagnostics.Debug.WriteLine("Native reader image: " + ex.Message); }
            };
            return host;
        }

        private static async Task ShowImageProgressAsync(ProgressRing progress, Func<bool> pending)
        {
            await Task.Delay(120);
            if (pending()) { progress.IsActive = true; progress.Visibility = Visibility.Visible; }
        }

        private static async Task<bool> LoadImageSourceAsync(Image image, Uri uri, bool vector)
        {
            var complete = new TaskCompletionSource<bool>();
#if UWP_1809
            if (vector)
            {
                var svg = new SvgImageSource();
                svg.Opened += (s, e) => complete.TrySetResult(true);
                svg.OpenFailed += (s, e) => complete.TrySetResult(false);
                image.Source = svg; svg.UriSource = uri;
            }
            else
#endif
            {
                var bitmap = new BitmapImage();
                bitmap.ImageOpened += (s, e) => complete.TrySetResult(true);
                bitmap.ImageFailed += (s, e) => complete.TrySetResult(false);
                image.Source = bitmap; bitmap.UriSource = uri;
            }
            return await Task.WhenAny(complete.Task, Task.Delay(TimeSpan.FromSeconds(15))) == complete.Task && await complete.Task;
        }

        private FrameworkElement MathView(NativeArticleNode node)
        {
            var host = new ContentControl { Content = ArticleMathView.Create(node, _palette), IsTabStop = false, FlowDirection = FlowDirection.LeftToRight };
            var color = _palette.Get(node.Style.Foreground, ArticleColorRole.Text, node.Style.ColorContext);
            if (color != null) host.Foreground = color;
            return host;
        }

        private FrameworkElement Media(NativeArticleNode node)
        {
            var panel = new StackPanel { Margin = new Thickness(0, 8, 0, 8) };
            if (node.PosterUri != null) panel.Children.Add(Image(new NativeArticleNode { Kind = NativeArticleNodeKind.Image, Uri = node.PosterUri, Text = "Video poster", ImageWidth = 480 }));
            var sources = NativeMediaService.PlaybackSources(node.MediaSources);
            if (sources.Count > 0)
            {
                var media = new MediaElement { AutoPlay = false, AreTransportControlsEnabled = true, MaxHeight = node.TagName == "audio" ? 80 : 480, MinHeight = 64, Stretch = Stretch.Uniform };
                var choices = new ComboBox { ItemsSource = sources, DisplayMemberPath = "Label", SelectedIndex = 0, HorizontalAlignment = HorizontalAlignment.Stretch };
                if (sources.Count > 1) panel.Children.Add(choices);
                var load = new Button { Content = node.TagName == "audio" ? "Play audio" : "Play video" };
                var status = new TextBlock { TextWrapping = TextWrapping.Wrap };
                var buffering = new ProgressBar { IsIndeterminate = true, Visibility = Visibility.Collapsed, MaxWidth = 480, Height = 4 };
                Windows.UI.Xaml.Automation.AutomationProperties.SetName(buffering, "Loading media");
                int version = _version;
                var scope = _scope;
                var cancellationToken = scope?.Cancellation.Token ?? _assetCancellation.Token;
                var attempted = new HashSet<Uri>();
                bool cached = false, changingChoice = false, playing = false;
                int request = 0;
                var resolveMedia = _resolveImage;
                var open = new HyperlinkButton { Content = "Open media file", NavigateUri = sources[0].Uri };
                Action start = () =>
                {
                    var selected = choices.SelectedItem as NativeMediaSource;
                    if (selected == null || version != _version || scope?.Active == false) return;
                    request++; cached = false; playing = true; attempted.Add(selected.Uri);
                    status.Text = "Loading " + selected.Label + "…"; open.NavigateUri = selected.Uri;
                    buffering.IsIndeterminate = true; buffering.Visibility = Visibility.Visible;
                    media.Source = selected.Uri;
                };
                media.MediaOpened += (s, e) =>
                {
                    if (version != _version || scope?.Active == false || !playing) return;
                    status.Text = ""; buffering.Visibility = Visibility.Collapsed; media.Play();
                };
                media.BufferingProgressChanged += (s, e) =>
                {
                    if (version != _version || scope?.Active == false || !playing) return;
                    double value = media.BufferingProgress;
                    buffering.IsIndeterminate = value <= 0; buffering.Value = value * 100;
                    buffering.Visibility = value < 1 ? Visibility.Visible : Visibility.Collapsed;
                };
                media.MediaFailed += async (s, e) =>
                {
                    if (version != _version || scope?.Active == false || !playing) return;
                    var selected = choices.SelectedItem as NativeMediaSource;
                    if (selected == null) return;
                    int current = request;
                    if (!cached && resolveMedia != null)
                    {
                        cached = true;
                        status.Text = "Trying a downloaded copy…";
                        buffering.IsIndeterminate = true; buffering.Visibility = Visibility.Visible;
                        string local = null;
                        try { local = await resolveMedia(selected.Uri.AbsoluteUri, cancellationToken); }
                        catch (Exception ex) { System.Diagnostics.Debug.WriteLine("Native media cache: " + ex.Message); }
                        if (version != _version || scope?.Active == false || current != request) return;
                        Uri uri;
                        if (Uri.TryCreate(local, UriKind.Absolute, out uri) && uri != selected.Uri) { media.Source = uri; return; }
                    }
                    // Leave the failing routed event before replacing its media source.
                    await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Low, () =>
                    {
                        if (version != _version || scope?.Active == false || current != request) return;
                        int next = sources.FindIndex(source => !attempted.Contains(source.Uri));
                        if (next >= 0)
                        {
                            changingChoice = true; choices.SelectedIndex = next; changingChoice = false; start();
                        }
                        else { playing = false; buffering.Visibility = Visibility.Collapsed; status.Text = "Windows could not play the available sources. " + e.ErrorMessage + " Use Open media file or Web view."; }
                    });
                };
                load.Click += (s, e) => { attempted.Clear(); start(); };
                choices.SelectionChanged += (s, e) =>
                {
                    if (changingChoice) return;
                    request++; playing = false; media.Stop(); media.Source = null; status.Text = "";
                    buffering.Visibility = Visibility.Collapsed;
                    var selected = choices.SelectedItem as NativeMediaSource; if (selected != null) open.NavigateUri = selected.Uri;
                };
                _media.Add(media); _scope?.Media.Add(media); panel.Children.Add(media); panel.Children.Add(load); panel.Children.Add(status);
                panel.Children.Add(buffering);
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
            double width = ActualWidth > 0 ? Math.Min(1100, ActualWidth) : 320;
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
                    var panel = new ArticleFlowPanel();
                    AppendBlocks(panel, cell.Children, cell);
                    var border = new Border { Child = panel, Tag = cell, BorderThickness = new Thickness(0.5), BorderBrush = _palette.Get(cell.Style.BorderColor ?? node.Style.BorderColor, ArticleColorRole.Border, cell.Style.ColorContext),
                        Background = _palette.Get(cell.Style.Background ?? rows[row].Style.Background ?? node.Style.Background, ArticleColorRole.Background),
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
            wrapper.Children.Add(new ArticleTableView(grid));
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
        {
            if (string.IsNullOrEmpty(id)) return;
            if (!_anchors.ContainsKey(id)) _anchors.Add(id, element);
            if (_scope != null && !_scope.Anchors.ContainsKey(id)) _scope.Anchors[id] = element;
        }

        private async Task Navigate(Uri uri)
        {
            int version = _version;
            try
            {
                FrameworkElement anchor;
                if (NativeArticleDocumentService.IsSamePage(_pageUri, uri) && !string.IsNullOrEmpty(uri.Fragment))
                {
                    string id = Uri.UnescapeDataString(uri.Fragment.TrimStart('#'));
                    NativeArticleSection section;
                    if (_sections != null && _anchorSections.TryGetValue(id, out section))
                    {
                        _sections.ScrollIntoView(section, ScrollIntoViewAlignment.Leading); _sections.UpdateLayout();
                        await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Low, () => { });
                        if (version != _version || _sections == null) return;
                        if (_anchors.TryGetValue(id, out anchor))
                        {
#if UWP_1809
                            // Let the virtualizing list resolve its content coordinates after realization.
                            anchor.StartBringIntoView(new BringIntoViewOptions { AnimationDesired = false, VerticalAlignmentRatio = 0 });
#else
                            var viewer = FindScrollViewer(_sections);
                            if (viewer != null) viewer.ChangeView(null, viewer.VerticalOffset + anchor.TransformToVisual(viewer).TransformPoint(new Point()).Y, null, true);
#endif
                        }
                        return;
                    }
                    if (_anchors.TryGetValue(id, out anchor)) { _scroll.ChangeView(null, anchor.TransformToVisual(_root).TransformPoint(new Point()).Y, null); return; }
                }
                if (_navigate != null) await _navigate(uri);
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine("Native reader navigation: " + ex.Message); }
        }

        private static ScrollViewer FindScrollViewer(DependencyObject root)
        {
            if (root is ScrollViewer viewer) return viewer;
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            { var found = FindScrollViewer(VisualTreeHelper.GetChild(root, i)); if (found != null) return found; }
            return null;
        }
    }
}
