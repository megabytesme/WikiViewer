using System;
using System.Threading.Tasks;
using Microsoft.Web.WebView2.Core;
using Newtonsoft.Json;
using WikiViewer.Shared.Uwp.Pages;
using WikiViewer.Shared.Uwp.Services;
using Windows.Foundation;
using Windows.Storage;
using Windows.UI.Xaml.Controls;

namespace _1809_UWP.Pages
{
    public sealed partial class ArticleViewerPage
    {
        private bool _isVirtualHostMappingSet = false;

        public ArticleViewerPage() => this.InitializeComponent();

        protected override TextBlock ArticleTitleTextBlock => ArticleTitle;
        protected override TextBlock LoadingTextBlock => LoadingText;
        protected override TextBlock LastUpdatedTextBlock => LastUpdatedText;
        protected override Grid LoadingOverlayGrid => LoadingOverlay;
        protected override Grid VerificationPanelGrid => VerificationPanel;
        protected override AppBarButton EditAppBarButton => EditButton;
        protected override AppBarButton FavoriteAppBarButton => FavoriteButton;

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
            if (ArticleDisplayWebView.CoreWebView2 == null)
                await ArticleDisplayWebView.EnsureCoreWebView2Async();

            if (!_isVirtualHostMappingSet && _pageWikiContext != null)
            {
                var tempFolder = ApplicationData.Current.LocalFolder.Path;
                ArticleDisplayWebView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                    GetVirtualHostName(),
                    tempFolder,
                    CoreWebView2HostResourceAccessKind.Allow
                );
                _isVirtualHostMappingSet = true;
            }

            StorageFile articleFile = await ApplicationData.Current.LocalFolder.CreateFileAsync(
                "article.html",
                CreationCollisionOption.ReplaceExisting
            );
            await FileIO.WriteTextAsync(articleFile, html);
            ArticleDisplayWebView.CoreWebView2.Navigate(
                $"https://{GetVirtualHostName()}/article.html"
            );
        }

        protected override async void ShowVerificationPanel(string url)
        {
            HideLoadingOverlay();
            VerificationPanel.Visibility = Windows.UI.Xaml.Visibility.Visible;
            await VerificationWebView.EnsureCoreWebView2Async();
            TypedEventHandler<
                CoreWebView2,
                CoreWebView2NavigationCompletedEventArgs
            > successHandler = null;
            successHandler = async (sender, args) =>
            {
                if (args.IsSuccess && !sender.Source.Contains("challenges.cloudflare.com"))
                {
                    VerificationWebView.CoreWebView2.NavigationCompleted -= successHandler;
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
            VerificationWebView.CoreWebView2.NavigationCompleted += successHandler;
            VerificationWebView.CoreWebView2.Navigate(url);
        }

        protected override async Task ExecuteScriptInWebViewAsync(string script)
        {
            try
            {
                if (ArticleDisplayWebView?.CoreWebView2 != null)
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
            string virtualHostUrl = $"https://{GetVirtualHostName()}{localPath.Replace('\\', '/')}";
            string escapedOriginalUrl = JsonConvert.ToString(originalUrl);
            string escapedVirtualUrl = JsonConvert.ToString(virtualHostUrl);
            return $"var imgs = document.querySelectorAll('img[src=' + {escapedOriginalUrl} + ']'); for (var i = 0; i < imgs.length; i++) {{ imgs[i].src = {escapedVirtualUrl}; }}";
        }

        protected override void InitializePlatformControls()
        {
            Environment.SetEnvironmentVariable("WEBVIEW2_DEFAULT_BACKGROUND_COLOR", "00FFFFFF");
            _ = InitializeWebView2Async();
        }

        protected override void UninitializePlatformControls()
        {
            if (ArticleDisplayWebView?.CoreWebView2 != null)
                ArticleDisplayWebView.CoreWebView2.NavigationStarting -=
                    ArticleDisplayWebView_NavigationStarting;
            ArticleDisplayWebView?.Close();
            VerificationWebView?.Close();
        }

        private async Task InitializeWebView2Async()
        {
            await ArticleDisplayWebView.EnsureCoreWebView2Async();
            ArticleDisplayWebView.CoreWebView2.NavigationStarting +=
                ArticleDisplayWebView_NavigationStarting;
        }

        private async void ArticleDisplayWebView_NavigationStarting(
            CoreWebView2 sender,
            CoreWebView2NavigationStartingEventArgs args
        )
        {
            if (args.Uri.EndsWith("/article.html", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            args.Cancel = true;

            if (string.IsNullOrEmpty(args.Uri))
                return;

            var uri = new Uri(args.Uri);

            if (_pageWikiContext != null)
            {
                string fullPathAndQuery = uri.PathAndQuery;

                if (
                    fullPathAndQuery.Contains(
                        "Special:UserLogin",
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                {
                    Frame.Navigate(GetLoginPageType(), _pageWikiContext.Id);
                    return;
                }
                if (
                    fullPathAndQuery.Contains(
                        "Special:CreateAccount",
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                {
                    Frame.Navigate(GetCreateAccountPageType(), _pageWikiContext.Id);
                    return;
                }

                if (uri.Host.Equals(GetVirtualHostName(), StringComparison.OrdinalIgnoreCase))
                {
                    string clickedPath = uri.AbsolutePath;
                    string newTitle = null;
                    string articlePathPrefix = $"/{_pageWikiContext.ArticlePath}";

                    if (
                        !string.IsNullOrEmpty(_pageWikiContext.ArticlePath)
                        && clickedPath.StartsWith(articlePathPrefix)
                    )
                        newTitle = clickedPath.Substring(articlePathPrefix.Length);
                    else
                        newTitle = clickedPath.TrimStart('/');

                    if (!string.IsNullOrEmpty(newTitle))
                    {
                        _pageTitleToFetch = Uri.UnescapeDataString(newTitle);
                        _articleHistory.Push(_pageTitleToFetch);
                        StartArticleFetch();
                        return;
                    }
                }
            }

            await Windows.System.Launcher.LaunchUriAsync(uri);
        }
    }
}
