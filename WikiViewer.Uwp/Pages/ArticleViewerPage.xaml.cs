using System;
using System.Threading.Tasks;
using WikiViewer.Uwp.Controls;
using Newtonsoft.Json;
using WikiViewer.Core;
using WikiViewer.Core.Models;
using WikiViewer.Core.Services;
using WikiViewer.Uwp.Pages;
using WikiViewer.Uwp.Services;
using Windows.Foundation;
using Windows.Storage;
using Windows.UI.Xaml.Controls;

namespace WikiViewer.Uwp.Pages
{
    public sealed partial class ArticleViewerPage
    {
        private bool _webHandlersAttached;
        private bool _platformActive;
        protected override bool UsesNativeReader => NativeReaderButton.IsChecked == true;
        protected override Task<bool> TryUpdateNativeStylesAsync(ArticleContent content)
            => UsesNativeReader ? NativeReader.TryUpdateStylesAsync(content) : Task.FromResult(false);

        public ArticleViewerPage()
        {
            this.InitializeComponent();
            NativeReaderButton.IsChecked = AppSettings.UseNativeReaderByDefault;
            UpdateReaderVisibility();
        }

        private void UpdateReaderVisibility()
        {
            WikiViewer.Uwp.Controls.ReaderModeControl.Update(NativeReaderButton);
            NativeReader.Visibility = NativeReaderButton.IsChecked == true ? Windows.UI.Xaml.Visibility.Visible : Windows.UI.Xaml.Visibility.Collapsed;
            ArticleDisplayWebView.Visibility = NativeReaderButton.IsChecked == true ? Windows.UI.Xaml.Visibility.Collapsed : Windows.UI.Xaml.Visibility.Visible;
        }

        private void NativeReaderButton_Click(object sender, Windows.UI.Xaml.RoutedEventArgs e)
        {
            UpdateReaderVisibility();
            ArticleDisplayWebView.Stop();
            NativeReader.ClearArticle();
            StartArticleFetch();
        }

        protected override async Task<bool> TryDisplayNativeArticleAsync(ArticleContent content)
        {
            if (NativeReaderButton.IsChecked != true) return false;
            try
            {
            var wiki = _pageWikiContext;
            return await NativeReader.ShowArticleAsync(content, new Uri(wiki.GetWikiPageUrl(_pageTitleToFetch)), async uri =>
            {
                var target = WikiManager.GetWikiByHost(uri.Host);
                string prefix = "/" + (target?.ArticlePath ?? "").Trim('/');
                prefix = prefix.TrimEnd('/') + "/";
                if (target != null && uri.AbsolutePath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    HandleValidPageNavigation(uri.AbsolutePath.Substring(prefix.Length), target, uri);
                else await ShowWikiDetectionPromptAsync(uri);
            }, (url, token) => MediaCacheService.GetLocalUriAsync(url, wiki, cancellationToken: token));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Native reader fallback: " + ex.Message);
                NativeReader.ClearArticle();
                NativeReaderButton.IsChecked = false;
                UpdateReaderVisibility();
                return false;
            }
        }

        protected override TextBlock ArticleTitleTextBlock => ArticleTitle;
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

        private string GetVirtualHostName() => $"local-content.{_pageWikiContext.Host}";

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

        protected override async Task DisplayProcessedHtmlAsync(string html)
        {
            await InitializeBrowserAsync();
            if (!_platformActive) return;

            await ArticleDisplayWebView.ShowArticleHtmlAsync(html, GetVirtualHostName());
        }

        protected override async void ShowVerificationPanel(string url)
        {
            HideLoadingOverlay();
            VerificationPanel.Visibility = Windows.UI.Xaml.Visibility.Visible;
            await VerificationWebView.EnsureInitializedAsync();
            EventHandler<WikiNavigationCompletedEventArgs> successHandler = null;
            successHandler = async (sender, args) =>
            {
                if (args.IsSuccess && args.Uri != null && args.Uri.Host != "challenges.cloudflare.com")
                {
                    VerificationWebView.NavigationCompleted -= successHandler;
                    await Dispatcher.RunAsync(
                        Windows.UI.Core.CoreDispatcherPriority.Normal,
                        () =>
                        {
                            VerificationPanel.Visibility = Windows.UI.Xaml.Visibility.Collapsed;
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
            await VerificationWebView.NavigateAsync(new Uri(url));
        }

        protected override async Task ExecuteScriptInWebViewAsync(string script)
        {
            try
            {
                if (ArticleDisplayWebView?.IsReady == true)
                {
                    await ArticleDisplayWebView.ExecuteScriptAsync(script);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[1809-JS] Script injection failed: {ex.Message}"
                );
            }
        }

        protected override string GetImageUpdateScript(string originalUrl, string localPath)
        {
            string finalLocalUrl = WikiBrowser.LocalAssetUrl(GetVirtualHostName(), localPath);
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

        protected override void InitializePlatformControls()
        {
            _platformActive = true;
            if (AppSettings.IsCustomThemingEnabled)
            {
                Environment.SetEnvironmentVariable("WEBVIEW2_DEFAULT_BACKGROUND_COLOR", "00FFFFFF");
            }
            else
            {
                Environment.SetEnvironmentVariable("WEBVIEW2_DEFAULT_BACKGROUND_COLOR", "FFFFFFFF");
            }

        }

        protected override void UninitializePlatformControls()
        {
            _platformActive = false;
            _webHandlersAttached = false;
            NativeReader.ClearArticle();
            if (ArticleDisplayWebView?.IsReady == true)
                ArticleDisplayWebView.NavigationStarting -=
                    ArticleDisplayWebView_NavigationStarting;
            ArticleDisplayWebView?.Close();
            VerificationWebView?.Close();
        }

        private async Task InitializeBrowserAsync()
        {
            await ArticleDisplayWebView.EnsureInitializedAsync();
            if (!_platformActive || _webHandlersAttached) return;
            ArticleDisplayWebView.NavigationStarting +=
                ArticleDisplayWebView_NavigationStarting;
            _webHandlersAttached = true;
        }

        private async void ArticleDisplayWebView_NavigationStarting(
            object sender,
            WikiNavigationStartingEventArgs args
        )
        {
            args.Cancel = true;

            var uri = args.Uri;
            if (uri == null) return;

            if (uri.Scheme == "ms-appdata" || uri.Scheme == "about" || uri.Host.Equals(GetVirtualHostName(), StringComparison.OrdinalIgnoreCase))
            {
                args.Cancel = false;
                return;
            }

            var targetWiki = WikiManager.GetWikiByHost(uri.Host);

            if (targetWiki != null)
            {
                string cleanArticlePath = targetWiki.ArticlePath?.Trim('/');

                if (!string.IsNullOrEmpty(cleanArticlePath))
                {
                    string articlePathPrefix = $"/{cleanArticlePath}/";

                    if (
                        uri.AbsolutePath.StartsWith(
                            articlePathPrefix,
                            StringComparison.OrdinalIgnoreCase
                        )
                    )
                    {
                        string pageTitle = uri.AbsolutePath.Substring(articlePathPrefix.Length);
                        HandleValidPageNavigation(pageTitle, targetWiki, uri);
                        return;
                    }
                }
                else
                {
                    if (uri.AbsolutePath.Length > 1)
                    {
                        string pageTitle = uri.AbsolutePath.Substring(1);
                        HandleValidPageNavigation(pageTitle, targetWiki, uri);
                        return;
                    }
                }
            }

            await ShowWikiDetectionPromptAsync(uri);
        }

        private void HandleValidPageNavigation(
            string pageTitle,
            WikiInstance targetWiki,
            Uri originalUri
        )
        {
            pageTitle = Uri.UnescapeDataString(pageTitle);

            if (string.IsNullOrEmpty(pageTitle))
                return;

            if (
                pageTitle.StartsWith("File:", StringComparison.OrdinalIgnoreCase)
                || pageTitle.StartsWith("Image:", StringComparison.OrdinalIgnoreCase)
            )
            {
                _ = ShowImageViewerAsync(pageTitle);
                return;
            }

            if (HandleSpecialLink(originalUri))
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
                    GetArticleViewerPageType(),
                    new ArticleNavigationParameter { WikiId = targetWiki.Id, PageTitle = pageTitle }
                );
            }
        }

        protected override void UpdateRefreshButtonVisibility()
        {
            RefreshAppBarButton.Visibility = AppSettings.ShowCssRefreshButton
                ? Windows.UI.Xaml.Visibility.Visible
                : Windows.UI.Xaml.Visibility.Collapsed;
        }
    }
}
