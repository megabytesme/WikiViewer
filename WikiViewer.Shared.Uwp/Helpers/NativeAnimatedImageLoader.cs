using System;
using System.Diagnostics;
using System.Numerics;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.Storage;
using Windows.UI.Composition;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Hosting;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Imaging;

namespace WikiViewer.Shared.Uwp.Helpers
{
    public static class NativeAnimatedImageLoader
    {
        public static readonly DependencyProperty SourceUriProperty =
            DependencyProperty.RegisterAttached(
                "SourceUri",
                typeof(string),
                typeof(NativeAnimatedImageLoader),
                new PropertyMetadata(null, OnSourceUriChanged)
            );

        public static string GetSourceUri(DependencyObject obj) =>
            (string)obj.GetValue(SourceUriProperty);

        public static void SetSourceUri(DependencyObject obj, string value) =>
            obj.SetValue(SourceUriProperty, value);

        private const bool DebugLog = true;

        private static void Log(string msg, State s = null)
        {
            if (!DebugLog)
                return;
            var dc = s?.Image?.DataContext;
            var prop = dc?.GetType().GetTypeInfo().GetDeclaredProperty("DisplayTitle");
            var id = prop?.GetValue(dc) as string ?? dc?.ToString() ?? "?";
            Debug.WriteLine($"[{DateTime.Now:HH:mm:ss:fff}] [NativeAnimatedImage] [{id}] {msg}");
        }

        private sealed class State
        {
            public CancellationTokenSource Cts = new CancellationTokenSource();
            public Image Image;
            public FrameworkElement Container;
            public Visual Visual;
            public bool ReadyImage;
            public bool ReadySize;
            public double ContainerW,
                ContainerH;
            public double ImagePxW,
                ImagePxH;
            public bool IsHorizontal;
            public float MaxPx;
            public float OffsetPx;
            public readonly Random Rand = new Random();
        }

        private static readonly DependencyProperty StateProperty =
            DependencyProperty.RegisterAttached(
                "State",
                typeof(State),
                typeof(NativeAnimatedImageLoader),
                new PropertyMetadata(null)
            );

        private static State GetState(DependencyObject d) => (State)d.GetValue(StateProperty);

        private static void SetState(DependencyObject d, State s) => d.SetValue(StateProperty, s);

        private static void OnSourceUriChanged(
            DependencyObject d,
            DependencyPropertyChangedEventArgs e
        )
        {
            if (!(d is Image image))
                return;

            var old = GetState(image);
            if (old != null)
            {
                try
                {
                    old.Cts.Cancel();
                }
                catch { }
                Detach(image, old);
                SetState(image, null);
            }

            var uri = e.NewValue as string;
            if (string.IsNullOrWhiteSpace(uri))
            {
                image.Source = null;
                return;
            }

            var state = new State { Image = image };
            SetState(image, state);

            state.Container = image.Parent as FrameworkElement ?? image;
            ApplyClip(state.Container);

            state.Container.Loaded += Container_Loaded;
            state.Container.SizeChanged += Container_SizeChanged;
            image.ImageOpened += Image_ImageOpened;
            image.SizeChanged += Image_SizeChanged;
            image.Unloaded += Image_Unloaded;

            Log($"SourceUri set to {uri}", state);

            if (uri.EndsWith(".svg", StringComparison.OrdinalIgnoreCase))
                image.Source = new SvgImageSource(new Uri(uri));
            else
                image.Source = new BitmapImage(new Uri(uri));

            TryPrimeContainerSize(state);
            TryPrimeImagePixels(state);
            MaybeInit(state);
        }

        private static void Image_Unloaded(object sender, RoutedEventArgs e)
        {
            var image = (Image)sender;
            var state = GetState(image);
            if (state == null)
                return;
            try
            {
                state.Cts.Cancel();
            }
            catch { }
            Detach(image, state);
            SetState(image, null);
            Log("Unloaded - cancelled", state);
        }

        private static void Detach(Image image, State state)
        {
            image.ImageOpened -= Image_ImageOpened;
            image.SizeChanged -= Image_SizeChanged;
            image.Unloaded -= Image_Unloaded;
            if (state.Container != null)
            {
                state.Container.Loaded -= Container_Loaded;
                state.Container.SizeChanged -= Container_SizeChanged;
            }
        }

        private static void Container_Loaded(object sender, RoutedEventArgs e)
        {
            var container = (FrameworkElement)sender;
            var image = FindChildImage(container);
            if (image == null)
                return;
            var state = GetState(image);
            if (state == null)
                return;

            ApplyClip(container);
            TryPrimeContainerSize(state);
            Log($"Container Loaded: {state.ContainerW}x{state.ContainerH}", state);
            MaybeInit(state, forceReinit: true);
        }

        private static void Container_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            var container = (FrameworkElement)sender;
            var image = FindChildImage(container);
            if (image == null)
                return;
            var state = GetState(image);
            if (state == null)
                return;

            ApplyClip(container);
            state.ContainerW = e.NewSize.Width;
            state.ContainerH = e.NewSize.Height;
            state.ReadySize = state.ContainerW > 0 && state.ContainerH > 0;
            Log($"Container SizeChanged: {state.ContainerW}x{state.ContainerH}", state);
            MaybeInit(state, forceReinit: true);
        }

        private static void Image_ImageOpened(object sender, RoutedEventArgs e)
        {
            var image = (Image)sender;
            var state = GetState(image);
            if (state == null)
                return;
            TryPrimeImagePixels(state);
            state.ReadyImage = state.ImagePxW > 0 && state.ImagePxH > 0;
            Log($"ImageOpened: px={state.ImagePxW}x{state.ImagePxH}", state);
            MaybeInit(state);
        }

        private static void Image_SizeChanged(object sender, SizeChangedEventArgs e) { }

        private static Image FindChildImage(FrameworkElement container)
        {
            if (container is Image img)
                return img;
            if (VisualTreeHelper.GetChildrenCount(container) > 0)
            {
                if (VisualTreeHelper.GetChild(container, 0) is Image img2)
                    return img2;
            }
            return null;
        }

        private static void ApplyClip(FrameworkElement container)
        {
            if (container == null)
                return;
            container.Clip = new RectangleGeometry
            {
                Rect = new Rect(0, 0, container.ActualWidth, container.ActualHeight),
            };
        }

        private static void TryPrimeContainerSize(State s)
        {
            if (s.Container == null)
                return;
            s.ContainerW = s.Container.ActualWidth;
            s.ContainerH = s.Container.ActualHeight;
            s.ReadySize = s.ContainerW > 0 && s.ContainerH > 0;
        }

        private static async void TryPrimeImagePixels(State s)
        {
            if (s.Image?.Source is BitmapSource bs)
            {
                s.ImagePxW = bs.PixelWidth;
                s.ImagePxH = bs.PixelHeight;
            }
            else if (s.Image?.Source is SvgImageSource svg && svg.UriSource != null)
            {
                var aspect = await GetSvgAspectRatioAsync(svg.UriSource);
                if (aspect.HasValue)
                {
                    s.ImagePxH = 500;
                    s.ImagePxW = 500 * aspect.Value;
                }
                else
                {
                    s.ImagePxW = s.ContainerW > 0 ? s.ContainerW : 300;
                    s.ImagePxH = s.ContainerH > 0 ? s.ContainerH : 200;
                }
            }
            s.ReadyImage = s.ImagePxW > 0 && s.ImagePxH > 0;
            Log($"ImagePixels Primed (async): px={s.ImagePxW}x{s.ImagePxH}", s);
            MaybeInit(s);
        }

        private static async Task<double?> GetSvgAspectRatioAsync(Uri svgUri)
        {
            try
            {
                var file = await StorageFile.GetFileFromApplicationUriAsync(svgUri);
                var text = await FileIO.ReadTextAsync(file);
                var doc = new Windows.Data.Xml.Dom.XmlDocument();
                doc.LoadXml(text);

                var root = doc.DocumentElement;
                var viewBox = root?.GetAttribute("viewBox");
                if (!string.IsNullOrEmpty(viewBox))
                {
                    var parts = viewBox.Split(
                        new[] { ' ', ',' },
                        StringSplitOptions.RemoveEmptyEntries
                    );
                    if (
                        parts.Length == 4
                        && double.TryParse(
                            parts[2],
                            System.Globalization.NumberStyles.Any,
                            System.Globalization.CultureInfo.InvariantCulture,
                            out var vbWidth
                        )
                        && double.TryParse(
                            parts[3],
                            System.Globalization.NumberStyles.Any,
                            System.Globalization.CultureInfo.InvariantCulture,
                            out var vbHeight
                        )
                        && vbHeight > 0
                    )
                    {
                        return vbWidth / vbHeight;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SVG Aspect Ratio] Failed for {svgUri}: {ex.Message}");
            }
            return null;
        }

        private static void MaybeInit(State s, bool forceReinit = false)
        {
            if (!s.ReadySize || !s.ReadyImage)
                return;

            var iar = s.ImagePxW / s.ImagePxH;
            var car = s.ContainerW / s.ContainerH;

            double scaledW,
                scaledH;

            if (iar >= car)
            {
                s.IsHorizontal = true;
                scaledH = s.ContainerH;
                scaledW = scaledH * iar;
                s.MaxPx = (float)Math.Max(0, scaledW - s.ContainerW);

                s.Image.Width = scaledW;
                s.Image.Height = scaledH;
                s.Image.VerticalAlignment = VerticalAlignment.Center;
                s.Image.HorizontalAlignment = HorizontalAlignment.Left;
            }
            else
            {
                s.IsHorizontal = false;
                scaledW = s.ContainerW;
                scaledH = scaledW / iar;
                s.MaxPx = (float)Math.Max(0, scaledH - s.ContainerH);

                s.Image.Width = scaledW;
                s.Image.Height = scaledH;
                s.Image.HorizontalAlignment = HorizontalAlignment.Center;
                s.Image.VerticalAlignment = VerticalAlignment.Top;
            }

            s.OffsetPx = s.MaxPx / 2f;

            if (s.Visual == null)
                s.Visual = ElementCompositionPreview.GetElementVisual(s.Image);

            if (forceReinit)
            {
                try
                {
                    s.Cts.Cancel();
                }
                catch { }
                s.Cts = new CancellationTokenSource();
            }

            Log(
                $"Init: cw={s.ContainerW:0} ch={s.ContainerH:0} iw={s.ImagePxW:0} ih={s.ImagePxH:0} scaledW={scaledW:0} scaledH={scaledH:0} axis={(s.IsHorizontal ? "X" : "Y")} max={s.MaxPx:0}",
                s
            );

            _ = RunLoopAsync(s);
        }

        private static async Task RunLoopAsync(State s)
        {
            var compositor = s.Visual?.Compositor;
            if (compositor == null)
                return;
            var token = s.Cts.Token;

            if (s.IsHorizontal)
                s.Visual.Offset = new Vector3(-s.OffsetPx, 0, 0);
            else
                s.Visual.Offset = new Vector3(0, -s.OffsetPx, 0);

            while (!token.IsCancellationRequested)
            {
                float target = (float)(s.Rand.NextDouble() * s.MaxPx);
                if (Math.Abs(target - s.OffsetPx) < s.MaxPx * 0.1)
                    target = (s.OffsetPx > s.MaxPx / 2) ? 0 : s.MaxPx;

                var duration = TimeSpan.FromSeconds(s.Rand.Next(4, 11));
                var easing = compositor.CreateCubicBezierEasingFunction(
                    new Vector2(0.42f, 0f),
                    new Vector2(0.58f, 1f)
                );

                var anim = compositor.CreateScalarKeyFrameAnimation();
                anim.InsertKeyFrame(1f, -target, easing);
                anim.Duration = duration;

                if (s.IsHorizontal)
                    s.Visual.StartAnimation(nameof(s.Visual.Offset) + ".X", anim);
                else
                    s.Visual.StartAnimation(nameof(s.Visual.Offset) + ".Y", anim);

                s.OffsetPx = target;

                try
                {
                    await Task.Delay(
                        duration + TimeSpan.FromMilliseconds(s.Rand.Next(300, 1000)),
                        token
                    );
                }
                catch (TaskCanceledException)
                {
                    break;
                }
            }
        }
    }
}
