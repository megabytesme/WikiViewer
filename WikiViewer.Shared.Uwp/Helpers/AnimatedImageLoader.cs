using System;
using System.Reflection;
using System.Xml.Linq;
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
                    html, body {{
                        margin: 0;
                        padding: 0;
                        width: 100%;
                        height: 100%;
                        overflow: hidden;
                    }}
                    img {{
                        position: absolute;
                        will-change: transform;
                    }}
                </style>
            </head>
            <body>
                <div class=""container"">
                    <img id='mainImage' src='{dataUri}'>
                </div>
                <script>
                    (function() {{
                        var img = document.getElementById('mainImage');
                        var dyn = document.createElement('style');
                        document.head.appendChild(dyn);

                        var axis = null;
                        var offsetPx = 0;
                        var minPx = 0;
                        var maxPx = 0;

                        function runPan() {{
                            var duration = Math.floor(Math.random() * 7) + 4;
                            var target = Math.round(Math.random() * (maxPx - minPx) + minPx);
                            if (Math.abs(target - offsetPx) < 2) {{
                                target = (target === maxPx) ? minPx : maxPx;
                            }}
                            var start = Math.round(offsetPx);
                            var end = Math.round(target);
                            offsetPx = end;

                            var css;
                            if (axis === 'horizontal') {{
                                css = '@keyframes panX {{' +
                                      'from {{ transform: translate(' + (-start) + 'px, -50%); }}' +
                                      'to {{ transform: translate(' + (-end) + 'px, -50%); }}' +
                                      '}}';
                                dyn.textContent = css;
                                img.style.animation = 'none';
                                void img.offsetWidth;
                                img.style.animation = 'panX ' + duration + 's ease-in-out forwards';
                            }} else {{
                                css = '@keyframes panY {{' +
                                      'from {{ transform: translate(-50%, ' + (-start) + 'px); }}' +
                                      'to {{ transform: translate(-50%, ' + (-end) + 'px); }}' +
                                      '}}';
                                dyn.textContent = css;
                                img.style.animation = 'none';
                                void img.offsetWidth;
                                img.style.animation = 'panY ' + duration + 's ease-in-out forwards';
                            }}

                            var pause = Math.floor(Math.random() * 700) + 300;
                            setTimeout(runPan, duration * 1000 + pause);
                        }}

                        function init() {{
                            var cw = document.body.clientWidth;
                            var ch = document.body.clientHeight;
                            var iw = img.naturalWidth;
                            var ih = img.naturalHeight;
                            var iar = iw / ih;
                            var car = cw / ch;

                            if (iar >= car) {{
                                axis = 'horizontal';
                                img.style.height = '100%';
                                img.style.width = 'auto';
                                img.style.top = '50%';
                                img.style.left = '0';
                                var scaledW = ch * iar;
                                maxPx = Math.max(0, Math.round(scaledW - cw));
                                minPx = 0;
                                offsetPx = Math.round(maxPx / 2);
                                img.style.transform = 'translate(' + (-offsetPx) + 'px, -50%)';
                            }} else {{
                                axis = 'vertical';
                                img.style.width = '100%';
                                img.style.height = 'auto';
                                img.style.left = '50%';
                                img.style.top = '0';
                                var scaledH = cw / iar;
                                maxPx = Math.max(0, Math.round(scaledH - ch));
                                minPx = 0;
                                offsetPx = Math.round(maxPx / 2);
                                img.style.transform = 'translate(-50%, ' + (-offsetPx) + 'px)';
                            }}

                            runPan();
                        }}

                        if (img.complete) {{
                            init();
                        }} else {{
                            img.onload = init;
                        }}
                    }})();
                </script>
            </body>
            </html>";
        }
    }
}
