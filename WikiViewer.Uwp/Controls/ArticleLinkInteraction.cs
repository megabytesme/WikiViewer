using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Windows.Devices.Input;
using Windows.Foundation;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Documents;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Core;

namespace WikiViewer.Uwp.Controls
{
    /// <summary>Keep inline wrapping/selection while routing touch taps through the same action as Click.</summary>
    internal sealed class ArticleLinkInteraction
    {
        private sealed class Link
        {
            public Hyperlink Element;
            public Func<Task> Navigate;
            public CoreDispatcher Dispatcher;
            private long _lastActivation = long.MinValue;
            private bool _pending;
            public async Task Activate()
            {
                long now = Stopwatch.GetTimestamp();
                // Some Windows versions raise both native Click and the routed touch tap.
                if (_pending || _lastActivation != long.MinValue && now - _lastActivation < Stopwatch.Frequency / 3) return;
                _pending = true;
                try
                {
                    // Complete text selection/input routing before replacing or scrolling its visual tree.
                    await Dispatcher.RunAsync(CoreDispatcherPriority.Low, () => { });
                    _lastActivation = Stopwatch.GetTimestamp();
                    await Navigate();
                }
                finally { _pending = false; }
            }
        }
        private readonly List<Link> _links = new List<Link>();
        public ArticleLinkInteraction(RichTextBlock text)
        {
            text.Tag = this;
            Attach(text, text.GetPositionFromPoint);
        }
        public void Add(Hyperlink element, Func<Task> navigate)
        {
            var link = new Link { Element = element, Navigate = navigate, Dispatcher = element.Dispatcher }; _links.Add(link);
            element.Click += async (s, e) => await link.Activate();
        }
        public static void AttachOverflow(RichTextBlockOverflow overflow, RichTextBlock source)
            => (source.Tag as ArticleLinkInteraction)?.Attach(overflow, overflow.GetPositionFromPoint);

        private void Attach(FrameworkElement element, Func<Point, TextPointer> positionAt)
        {
            element.AddHandler(UIElement.TappedEvent, new TappedEventHandler(async (sender, args) =>
            {
                if (args.PointerDeviceType == PointerDeviceType.Mouse || IsEmbeddedControl(args.OriginalSource as DependencyObject, element)) return;
                try
                {
                    var position = positionAt(args.GetPosition(element));
                    if (position == null) return;
                    var link = _links.FirstOrDefault(item => position.Offset >= item.Element.ContentStart.Offset && position.Offset <= item.Element.ContentEnd.Offset);
                    if (link == null) return;
                    args.Handled = true;
                    await link.Activate();
                }
                catch (Exception ex) { Debug.WriteLine("Native link tap: " + ex.GetType().Name); }
            }), true);
        }
        private static bool IsEmbeddedControl(DependencyObject source, FrameworkElement text)
        {
            while (source != null && source != text)
            {
                if (source is ButtonBase || source is MediaElement) return true;
                if (!(source is UIElement)) break;
                source = VisualTreeHelper.GetParent(source);
            }
            return false;
        }
    }
}
