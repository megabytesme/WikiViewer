using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Windows.Foundation.Metadata;
using Windows.UI.Core;
using Windows.UI.ViewManagement;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;

namespace WikiViewer.Uwp.Services
{
    /// <summary>Refreshes app-owned material brushes without rebuilding a page or its controls.</summary>
    public static class AppearanceSurface
    {
        private static readonly List<WeakReference<FrameworkElement>> Surfaces = new List<WeakReference<FrameworkElement>>();
        private static readonly UISettings Settings = new UISettings();
        private static CoreDispatcher _dispatcher;

        public static readonly DependencyProperty ResourceKeyProperty = DependencyProperty.RegisterAttached(
            "ResourceKey", typeof(string), typeof(AppearanceSurface), new PropertyMetadata(null, OnResourceKeyChanged));

        static AppearanceSurface()
        {
            Settings.ColorValuesChanged += async (s, e) =>
            {
                var dispatcher = _dispatcher;
                if (dispatcher == null) return;
                try { await dispatcher.RunAsync(CoreDispatcherPriority.Normal, AppearanceService.Apply); }
                catch (Exception) { }
            };
        }

        public static string GetResourceKey(DependencyObject target) => (string)target.GetValue(ResourceKeyProperty);
        public static void SetResourceKey(DependencyObject target, string value) => target.SetValue(ResourceKeyProperty, value);

        private static void OnResourceKeyChanged(DependencyObject target, DependencyPropertyChangedEventArgs args)
        {
            if (!(target is FrameworkElement element)) return;
            _dispatcher = element.Dispatcher;
            if (args.OldValue == null)
            {
                Surfaces.Add(new WeakReference<FrameworkElement>(element));
                element.Loaded += (s, e) => Update((FrameworkElement)s);
            }
            Update(element);
        }

        internal static void Refresh()
        {
            for (int i = Surfaces.Count - 1; i >= 0; i--)
            {
                if (Surfaces[i].TryGetTarget(out var element)) Update(element);
                else Surfaces.RemoveAt(i);
            }
        }

        internal static object[] Describe() => Surfaces.Select(reference =>
        {
            if (!reference.TryGetTarget(out var element)) return null;
            Brush brush = element is Control control ? control.Background : element is Panel panel ? panel.Background : (element as Border)?.Background;
            return new { Name = element.Name, Resource = GetResourceKey(element), Brush = brush?.GetType().Name };
        }).Where(surface => surface != null).ToArray();

        private static void Update(FrameworkElement element)
        {
            var key = GetResourceKey(element);
            if (string.IsNullOrEmpty(key) || !(AppearanceService.GetBrush(key) is Brush brush)) return;
            if (element is Control control) control.Background = brush;
            else if (element is Panel panel) panel.Background = brush;
            else if (element is Border border)
            {
                border.Background = brush;
                if (key == "CardBackgroundFillColorDefaultBrush") border.CornerRadius = new CornerRadius(AppearanceService.CardRadius);
            }
            else if (element is Windows.UI.Xaml.Shapes.Shape shape) shape.Fill = brush;
            if (key == "CardBackgroundFillColorDefaultBrush" && element is Grid grid && ApiInformation.IsPropertyPresent("Windows.UI.Xaml.Controls.Grid", "CornerRadius"))
                SetGridRadius(grid);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void SetGridRadius(Grid grid) => grid.CornerRadius = new CornerRadius(AppearanceService.CardRadius);
    }
}
