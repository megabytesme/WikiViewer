using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using WikiViewer.Core.Models;
using WikiViewer.Core.Services;
using Windows.Foundation;
using Windows.Graphics.Printing;
using Windows.Storage;
using Windows.UI;
using Windows.UI.Text;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Documents;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Imaging;
using Windows.UI.Xaml.Printing;

namespace WikiViewer.Uwp.Services
{
    /// <summary>Prints a complete document, independently of the reader's recycled viewport.</summary>
    public sealed class ArticlePrintService : IDisposable
    {
        private static ArticlePrintService _active;
        private readonly Canvas _host;
        private readonly PrintDocument _document = new PrintDocument();
        private readonly IPrintDocumentSource _source;
        private readonly PrintManager _manager;
        private readonly List<Grid> _pages = new List<Grid>();
        private RichTextBlock _text;
        private ImageSource _image;
        private string _title;
        private bool _disposed;
        public event EventHandler<string> Failed;

        public ArticlePrintService(Canvas host)
        {
            if (!PrintManager.IsSupported()) throw new NotSupportedException("Printing is unavailable on this device.");
            _host = host ?? throw new ArgumentNullException(nameof(host));
            _source = _document.DocumentSource;
            _manager = PrintManager.GetForCurrentView();
            // Article and image dialogs share the current window's print contract.
            _active?.Dispose();
            _active = this;
            _manager.PrintTaskRequested += TaskRequested;
            _document.Paginate += Paginate;
            _document.GetPreviewPage += GetPreview;
            _document.AddPages += AddPages;
        }

        public async Task PrintArticleAsync(string title, ArticleContent content, Uri page, Func<string, Task<string>> resolveImage)
        {
            _title = title;
            var model = await Task.Run(() => NativeArticleDocumentService.Parse(content, page));
            if (_disposed) return;
            _text = new RichTextBlock { FontSize = 12, Foreground = new SolidColorBrush(Colors.Black), IsTextSelectionEnabled = false };
            var heading = new Paragraph { FontSize = 22, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 12) };
            heading.Inlines.Add(new Run { Text = title });
            _text.Blocks.Add(heading);
            var link = new Paragraph { FontSize = 10, Margin = new Thickness(0, 0, 0, 16) };
            link.Inlines.Add(new Run { Text = page.AbsoluteUri });
            _text.Blocks.Add(link);
            foreach (var node in model.Nodes) await AddBlockAsync(node, resolveImage);
            await ShowAsync();
        }

        private async Task AddBlockAsync(NativeArticleNode node, Func<string, Task<string>> resolve)
        {
            if (_disposed) return;
            if (node.Kind == NativeArticleNodeKind.List)
            {
                int number = node.ListStart;
                foreach (var item in node.Children)
                {
                    var p = NewParagraph(item);
                    p.Inlines.Add(new Run { Text = node.Ordered ? (item.ListValue ?? number) + ". " : "• " });
                    number = (item.ListValue ?? number) + 1;
                    foreach (var child in item.Children) await AddInlineAsync(p.Inlines, child, resolve);
                    _text?.Blocks.Add(p);
                }
                return;
            }
            if (node.Kind == NativeArticleNodeKind.Table || node.Kind == NativeArticleNodeKind.Container)
            {
                foreach (var child in node.Children) await AddBlockAsync(child, resolve);
                return;
            }
            var paragraph = NewParagraph(node);
            await AddInlineAsync(paragraph.Inlines, node, resolve);
            _text?.Blocks.Add(paragraph);
        }

        private static Paragraph NewParagraph(NativeArticleNode node) => new Paragraph
        {
            Margin = new Thickness(node.Kind == NativeArticleNodeKind.Quote ? 16 : 0, 0, 0, 8),
            FontSize = node.Kind == NativeArticleNodeKind.Heading ? Math.Max(13, 22 - node.Level * 2) : 12,
            FontWeight = node.Kind == NativeArticleNodeKind.Heading ? FontWeights.Bold : FontWeights.Normal
        };

        private async Task AddInlineAsync(InlineCollection target, NativeArticleNode node, Func<string, Task<string>> resolve)
        {
            if (node.Kind == NativeArticleNodeKind.Text) { target.Add(new Run { Text = node.Text }); return; }
            if (node.Kind == NativeArticleNodeKind.LineBreak) { target.Add(new LineBreak()); return; }
            if (node.Kind == NativeArticleNodeKind.Image && node.Uri != null)
            {
                try
                {
                    var local = await resolve(node.Uri.AbsoluteUri);
                    if (Uri.TryCreate(local, UriKind.Absolute, out var uri))
                    {
                        ImageSource source;
                        var file = await StorageFile.GetFileFromApplicationUriAsync(uri);
                        using (var stream = await file.OpenReadAsync())
                        {
                            if (uri.AbsolutePath.EndsWith(".svg", StringComparison.OrdinalIgnoreCase))
                            {
                                var svg = new SvgImageSource();
                                if (await svg.SetSourceAsync(stream) != SvgImageSourceLoadStatus.Success) throw new InvalidOperationException("Image could not be decoded.");
                                source = svg;
                            }
                            else
                            {
                                var bitmap = new BitmapImage { DecodePixelWidth = node.ImageWidth > 0 ? Math.Min(840, node.ImageWidth * 2) : 840 };
                                await bitmap.SetSourceAsync(stream); source = bitmap;
                            }
                        }
                        if (_disposed) return;
                        target.Add(new InlineUIContainer { Child = new Image { Source = source, MaxWidth = 420, MaxHeight = 250, Stretch = Stretch.Uniform } });
                        return;
                    }
                }
                catch (Exception) { /* Preserve an image label if an asset is unavailable offline. */ }
                target.Add(new Run { Text = "[" + (node.Text ?? node.SourceFileName ?? "Image") + "]" });
                return;
            }
            if (node.Kind == NativeArticleNodeKind.Media)
            {
                target.Add(new Run { Text = "[Media: " + (node.Uri?.AbsoluteUri ?? node.Text ?? "available on the wiki") + "]" });
                return;
            }
            if (node.Kind == NativeArticleNodeKind.Math && !string.IsNullOrWhiteSpace(node.Text))
            {
                target.Add(new Run { Text = node.Text });
                return;
            }
            Span span = node.Kind == NativeArticleNodeKind.Strong ? new Bold() : node.Kind == NativeArticleNodeKind.Emphasis ? (Span)new Italic() : new Span();
            if (node.Kind == NativeArticleNodeKind.Code || node.Kind == NativeArticleNodeKind.Preformatted) span.FontFamily = new FontFamily("Consolas");
            foreach (var child in node.Children)
            {
                await AddInlineAsync(span.Inlines, child, resolve);
                if (node.Kind == NativeArticleNodeKind.TableRow) span.Inlines.Add(new Run { Text = "    |    " });
                else if (child.IsBlock) span.Inlines.Add(new LineBreak());
            }
            target.Add(span);
        }

        public async Task PrintImageAsync(string title, ImageSource image)
        {
            _title = title;
            _image = image ?? throw new InvalidOperationException("Wait for the image to finish loading before printing.");
            await ShowAsync();
        }

        private async Task ShowAsync()
        {
            if (_disposed) return;
            if (!await PrintManager.ShowPrintUIAsync()) Dispose();
        }

        private void TaskRequested(PrintManager sender, PrintTaskRequestedEventArgs e)
        {
            var task = e.Request.CreatePrintTask(_title ?? "Wiki page", args => args.SetSource(_source));
            task.Completed += async (s, args) => await _host.Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
            {
                if (!_disposed && args.Completion == PrintTaskCompletion.Failed) Failed?.Invoke(this, "The print job failed. Check the selected printer and try again.");
                Dispose();
            });
        }

        private void Paginate(object sender, PaginateEventArgs e)
        {
            foreach (var old in _pages) old.Children.Clear();
            _host.Children.Clear(); _pages.Clear();
            if (_text != null) { _text.OverflowContentTarget = null; }
            RichTextBlockOverflow previous = null;
            bool more;
            do
            {
                var description = e.PrintTaskOptions.GetPageDescription((uint)_pages.Count);
                double left = Math.Max(36, description.ImageableRect.Left), top = Math.Max(36, description.ImageableRect.Top);
                double right = Math.Max(36, description.PageSize.Width - description.ImageableRect.Right);
                double bottom = Math.Max(36, description.PageSize.Height - description.ImageableRect.Bottom);
                var page = new Grid { Width = description.PageSize.Width, Height = description.PageSize.Height, Background = new SolidColorBrush(Colors.White), RequestedTheme = ElementTheme.Light };
                FrameworkElement body;
                RichTextBlockOverflow overflow = null;
                if (_image != null) body = new Image { Source = _image, Stretch = Stretch.Uniform };
                else if (_pages.Count == 0) body = _text;
                else
                {
                    overflow = new RichTextBlockOverflow(); body = overflow;
                    if (previous == null) _text.OverflowContentTarget = overflow;
                    else previous.OverflowContentTarget = overflow;
                }
                body.Margin = new Thickness(left, top, right, bottom);
                page.Children.Add(body); _host.Children.Add(page); _pages.Add(page);
                page.Measure(description.PageSize); page.Arrange(new Rect(new Point(), description.PageSize));
                _host.InvalidateMeasure(); _host.UpdateLayout();
                more = _image == null && (overflow == null ? _text.HasOverflowContent : overflow.HasOverflowContent);
                previous = overflow;
            } while (more);
            _document.SetPreviewPageCount(_pages.Count, PreviewPageCountType.Final);
        }

        private void GetPreview(object sender, GetPreviewPageEventArgs e) => _document.SetPreviewPage(e.PageNumber, _pages[e.PageNumber - 1]);
        private void AddPages(object sender, AddPagesEventArgs e)
        {
            foreach (var page in _pages) _document.AddPage(page);
            _document.AddPagesComplete();
        }
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (ReferenceEquals(_active, this)) _active = null;
            _manager.PrintTaskRequested -= TaskRequested;
            _document.Paginate -= Paginate; _document.GetPreviewPage -= GetPreview; _document.AddPages -= AddPages;
            _host.Children.Clear(); _pages.Clear(); _text = null; _image = null;
        }
    }
}
