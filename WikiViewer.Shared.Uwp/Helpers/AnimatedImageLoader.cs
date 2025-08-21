using System;
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
            var random = new Random();
            int duration = random.Next(10, 25);

            return $@"
        <!DOCTYPE html>
        <html>
        <head>
            <style>
                .container {{
                    width: 100vw;
                    height: 100vh;
                    overflow: hidden;
                    position: relative;
                    background-color: transparent;
                }}

                html, body {{margin: 0;
                    padding: 0;
                    width: 100%;
                    height: 100%;
                    overflow: hidden;
                }}

                img {{
                    position: absolute;
                }}

                img.pan-horizontal {{
                    height: 100%;
                    width: auto;
                    animation: pan-horizontal {duration}s infinite alternate ease-in-out;
                }}

                img.pan-vertical {{
                    width: 100%;
                    height: auto;
                    animation: pan-vertical {duration}s infinite alternate ease-in-out;
                }}

                @keyframes pan-horizontal {{
                    from {{ left: 0; transform: translate(0, -50%); top: 50%; }}
                    to   {{ left: 100%; transform: translate(-100%, -50%); top: 50%; }}
                }}

                @keyframes pan-vertical {{
                    from {{ top: 0; transform: translate(-50%, 0); left: 50%; }}
                    to   {{ top: 100%; transform: translate(-50%, -100%); left: 50%; }}
                }}
            </style>
        </head>
        <body>
            <div class=""container"">
                <img id='mainImage' src='{dataUri}'>
            </div>
            <script>
                const img = document.getElementById('mainImage');
                img.onload = function() {{
                    const imageAspectRatio = img.naturalWidth / img.naturalHeight;
                    const containerAspectRatio = document.body.clientWidth / document.body.clientHeight;

                    if (imageAspectRatio < containerAspectRatio) {{
                        img.classList.add('pan-vertical');
                    }} else {{
                        img.classList.add('pan-horizontal');
                    }}
                }};
                if (img.complete) {{
                    img.onload();
                }}
            </script>
        </body>
        </html>";
        }
    }
}
