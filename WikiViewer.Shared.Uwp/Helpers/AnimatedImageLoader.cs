using WikiViewer.Shared.Uwp.Converters;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace WikiViewer.Shared.Uwp.Helpers
{
    public static class AnimatedImageLoader
    {
        public static readonly DependencyProperty SourceObjectProperty =
            DependencyProperty.RegisterAttached(
                "SourceObject",
                typeof(object),
                typeof(AnimatedImageLoader),
                new PropertyMetadata(null, OnSourceObjectChanged)
            );

        public static object GetSourceObject(DependencyObject obj) =>
            obj.GetValue(SourceObjectProperty);

        public static void SetSourceObject(DependencyObject obj, object value) =>
            obj.SetValue(SourceObjectProperty, value);

        private static void OnSourceObjectChanged(
            DependencyObject d,
            DependencyPropertyChangedEventArgs e
        )
        {
            if (!(d is WebView webView) || !(e.NewValue is NotifyTaskCompletion<string> ntc))
            {
                return;
            }

            void LoadHtml(string dataUri)
            {
                if (!string.IsNullOrEmpty(dataUri))
                {
                    string html = GetAnimatedImageHtml(dataUri);
                    webView.NavigateToString(html);
                }
            }

            if (ntc.IsSuccessfullyCompleted)
            {
                LoadHtml(ntc.Result);
            }
            else
            {
                ntc.PropertyChanged += (sender, args) =>
                {
                    if (args.PropertyName == nameof(ntc.Result))
                    {
                        LoadHtml(ntc.Result);
                    }
                };
            }
        }

        private static string GetAnimatedImageHtml(string dataUri)
        {
            return $@"
                <!DOCTYPE html><html><head><style>
                body {{ margin: 0; overflow: hidden; background-color: transparent; }}
                img {{
                    position: absolute; left: 50%; top: 50%; height: 100%; width: auto;
                    min-width: 100%; min-height: 100%; object-fit: cover;
                    transform: translate(-50%, -50%);
                    animation: pan 30s infinite alternate ease-in-out;
                }}
                @keyframes pan {{
                    0% {{ transform: translate(-45%, -50%); }}
                    100% {{ transform: translate(-55%, -50%); }}
                }}
                </style></head><body><img src='{dataUri}'></body></html>";
        }
    }
}
