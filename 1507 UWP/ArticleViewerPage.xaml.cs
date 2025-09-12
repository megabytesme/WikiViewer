using System;
using System.Threading.Tasks;
using Newtonsoft.Json;
using WikiViewer.Core;
using WikiViewer.Core.Services;
using WikiViewer.Shared.Uwp.Pages;
using WikiViewer.Shared.Uwp.Services;
using Windows.Foundation;
using Windows.Storage;
using Windows.UI.ViewManagement;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace _1507_UWP.Pages
{
    public sealed partial class ArticleViewerPage
    {
        private readonly UISettings _uiSettings = new UISettings();

        public ArticleViewerPage() => this.InitializeComponent();

        protected override TextBlock ArticleTitleTextBlock => new TextBlock();
        protected override TextBlock LoadingTextBlock => LoadingText;
        protected override TextBlock LastUpdatedTextBlock => LastUpdatedText;
        protected override Grid LoadingOverlayGrid => LoadingOverlay;
        protected override Grid VerificationPanelGrid => VerificationPanel;
        protected override AppBarButton EditAppBarButton => EditButton;
        protected override AppBarButton FavoriteAppBarButton => FavoriteButton;
        protected override AppBarButton RefreshAppBarButton => RefreshButton;

        protected override Type GetArticleViewerPageType() => typeof(ArticleViewerPage);

        protected override Type GetEditPageType() => typeof(EditPage);

        protected override Type GetLoginPageType() => typeof(LoginPage);

        protected override Type GetCreateAccountPageType() => typeof(CreateAccountPage);

        private async Task UpdateWebViewThemeAsync()
        {
            if (ArticleDisplayWebView == null)
                return;
            string theme =
                Application.Current.RequestedTheme == ApplicationTheme.Dark ? "dark" : "light";

            string script =
                $@"
                var theme = '{theme}';
                if (theme === 'dark') {{
                    document.documentElement.classList.add('dark-theme');
                }} else {{
                    document.documentElement.classList.remove('dark-theme');
                }}";
            try
            {
                await ArticleDisplayWebView.InvokeScriptAsync("eval", new string[] { script });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[WebViewTheme] Failed to set theme: {ex.Message}"
                );
            }
        }

        private async void ThemeChanged(UISettings sender, object args)
        {
            await Dispatcher.RunAsync(
                Windows.UI.Core.CoreDispatcherPriority.Normal,
                async () =>
                {
                    await UpdateWebViewThemeAsync();
                }
            );
        }

        protected override void InitializePlatformControls()
        {
            ArticleDisplayWebView.NavigationStarting += ArticleDisplayWebView_NavigationStarting;
            ArticleDisplayWebView.NavigationCompleted += ArticleDisplayWebView_NavigationCompleted;
            _uiSettings.ColorValuesChanged += ThemeChanged;
            if (!AppSettings.IsCustomThemingEnabled)
            {
                ArticleDisplayWebView.DefaultBackgroundColor = Windows.UI.Colors.White;
            }
        }

        protected override void UninitializePlatformControls()
        {
            ArticleDisplayWebView.NavigationStarting -= ArticleDisplayWebView_NavigationStarting;
            ArticleDisplayWebView.NavigationCompleted -= ArticleDisplayWebView_NavigationCompleted;
            _uiSettings.ColorValuesChanged -= ThemeChanged;
        }

        private async void ArticleDisplayWebView_NavigationCompleted(
            WebView sender,
            WebViewNavigationCompletedEventArgs args
        )
        {
            if (args.IsSuccess)
            {
                await UpdateWebViewThemeAsync();
            }
        }

        protected override async Task DisplayProcessedHtmlAsync(string html)
        {
            StorageFolder cacheFolder = await ApplicationData.Current.LocalFolder.CreateFolderAsync(
                "cache",
                CreationCollisionOption.OpenIfExists
            );
            StorageFile articleFile = await cacheFolder.CreateFileAsync(
                "article.html",
                CreationCollisionOption.ReplaceExisting
            );
            await FileIO.WriteTextAsync(articleFile, html);
            Uri localUri = new Uri($"ms-appdata:///local/cache/article.html");
            ArticleDisplayWebView.Navigate(localUri);
        }

        protected override async Task ExecuteScriptInWebViewAsync(string script)
        {
            try
            {
                if (ArticleDisplayWebView == null)
                    return;

                if (
                    ArticleDisplayWebView.Source == null
                    || ArticleDisplayWebView.Source.Scheme != "ms-appdata"
                )
                    return;

                string wrappedScript =
                    $@"
            (function() {{
                if (document.readyState === 'loading') {{
                    document.addEventListener('DOMContentLoaded', function() {{
                        {script}
                    }});
                }} else {{
                    {script}
                }}
            }})();";

                await ArticleDisplayWebView.InvokeScriptAsync("eval", new[] { wrappedScript });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[1703-JS] Script injection failed: {ex.Message}"
                );
            }
        }

        protected override string GetImageUpdateScript(string originalUrl, string localPath)
        {
            string fileName = System.IO.Path.GetFileName(localPath);
            string finalLocalUrl = fileName;
            string escapedOriginalUrl = JsonConvert.ToString(originalUrl);
            string escapedLocalUrl = JsonConvert.ToString(finalLocalUrl);

            return $@"
        (function() {{
            var selector = 'img[src=' + {escapedOriginalUrl} + ']';
            var imgs = document.querySelectorAll(selector);
            for (var i = 0; i < imgs.length; i++) {{
                imgs[i].src = {escapedLocalUrl};
            }}
        }})();";
        }

        private async void ArticleDisplayWebView_NavigationStarting(
            WebView sender,
            WebViewNavigationStartingEventArgs args
        )
        {
            args.Cancel = true;

            if (args.Uri == null)
                return;

            if (args.Uri.Scheme == "ms-appdata" || args.Uri.Scheme == "ms-local-stream")
            {
                args.Cancel = false;
                return;
            }

            var targetWiki = WikiManager.GetWikiByHost(args.Uri.Host);

            if (targetWiki != null)
            {
                string articlePathWithSlashes = $"/{targetWiki.ArticlePath.Trim('/')}/";

                if (targetWiki.ArticlePath.Trim() == "")
                {
                    articlePathWithSlashes = "/";
                }

                if (
                    args.Uri.AbsolutePath.StartsWith(
                        articlePathWithSlashes,
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                {
                    string pageTitle = args.Uri.AbsolutePath.Substring(
                        articlePathWithSlashes.Length
                    );

                    pageTitle = Uri.UnescapeDataString(pageTitle);

                    if (!string.IsNullOrEmpty(pageTitle))
                    {
                        if (
                            pageTitle.StartsWith("File:", StringComparison.OrdinalIgnoreCase)
                            || pageTitle.StartsWith("Image:", StringComparison.OrdinalIgnoreCase)
                        )
                        {
                            _ = ShowImageViewerAsync(pageTitle);
                            return;
                        }

                        if (HandleSpecialLink(args.Uri))
                        {
                            return;
                        }

                        if (targetWiki.Id == _pageWikiContext.Id)
                        {
                            NavigateToInternalPage(pageTitle);
                        }
                        else
                        {
                            Frame.Navigate(
                                typeof(ArticleViewerPage),
                                new ArticleNavigationParameter
                                {
                                    WikiId = targetWiki.Id,
                                    PageTitle = pageTitle,
                                }
                            );
                        }
                        return;
                    }
                }
            }

            await ShowWikiDetectionPromptAsync(args.Uri);
        }

        protected override void ShowLoadingOverlay()
        {
            LoadingOverlay.IsHitTestVisible = true;
            FadeInAnimation.Begin();
        }

        protected override void HideLoadingOverlay()
        {
            void OnAnimationCompleted(object s, object e)
            {
                LoadingOverlay.IsHitTestVisible = false;
                FadeOutAnimation.Completed -= OnAnimationCompleted;
            }
            FadeOutAnimation.Completed += OnAnimationCompleted;
            FadeOutAnimation.Begin();
        }

        protected override void ShowVerificationPanel(string url)
        {
            HideLoadingOverlay();
            VerificationPanel.Visibility = Visibility.Visible;
            TypedEventHandler<WebView, WebViewNavigationCompletedEventArgs> successHandler = null;
            successHandler = async (sender, args) =>
            {
                if (args.IsSuccess && !args.Uri.Host.Contains("challenges.cloudflare.com"))
                {
                    sender.NavigationCompleted -= successHandler;
                    await Dispatcher.RunAsync(
                        Windows.UI.Core.CoreDispatcherPriority.Normal,
                        () =>
                        {
                            VerificationPanel.Visibility = Visibility.Collapsed;
                            _verificationUrl = null;

                            if (_isVerificationOnlyFlow)
                            {
                                var mainPage = this.FindParent<MainPageBase>();
                                if (mainPage != null)
                                {
                                    _ = mainPage._postVerificationAction?.Invoke();
                                    mainPage._postVerificationAction = null;
                                }

                                if (Frame.CanGoBack)
                                {
                                    Frame.GoBack();
                                }
                            }
                            else
                            {
                                StartArticleFetch();
                            }
                        }
                    );
                }
            };
            VerificationWebView.NavigationCompleted += successHandler;
            VerificationWebView.Navigate(new Uri(url));
        }

        protected override void UpdateRefreshButtonVisibility()
        {
            RefreshAppBarButton.Visibility = AppSettings.ShowCssRefreshButton
                ? Visibility.Visible
                : Visibility.Collapsed;
        }
    }
}
