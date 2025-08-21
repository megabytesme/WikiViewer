using System;
using System.Threading.Tasks;
using Microsoft.Web.WebView2.Core;
using WikiViewer.Core;
using Windows.Foundation;
using Windows.Storage;
using Windows.UI.Xaml.Controls;

namespace _1809_UWP.Pages
{
    public sealed partial class ArticleViewerPage
    {
        public ArticleViewerPage() => this.InitializeComponent();

        protected override TextBlock ArticleTitleTextBlock => ArticleTitle;
        protected override TextBlock LoadingTextBlock => LoadingText;
        protected override TextBlock LastUpdatedTextBlock => LastUpdatedText;
        protected override Grid LoadingOverlayGrid => LoadingOverlay;
        protected override Grid VerificationPanelGrid => VerificationPanel;
        protected override AppBarButton EditAppBarButton => EditButton;
        protected override AppBarButton FavoriteAppBarButton => FavoriteButton;

        protected override Type GetEditPageType() => typeof(EditPage);

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
            StorageFile articleFile = await ApplicationData.Current.LocalFolder.CreateFileAsync(
                "article.html",
                CreationCollisionOption.ReplaceExisting
            );
            await FileIO.WriteTextAsync(articleFile, html);
            ArticleDisplayWebView.CoreWebView2.Navigate(
                $"https://{AppSettings.GetVirtualHostName()}/article.html"
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
                            StartArticleFetch();
                        }
                    );
                }
            };
            VerificationWebView.CoreWebView2.NavigationCompleted += successHandler;
            VerificationWebView.CoreWebView2.Navigate(url);
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
            var tempFolder = ApplicationData.Current.LocalFolder.Path;
            ArticleDisplayWebView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                AppSettings.GetVirtualHostName(),
                tempFolder,
                CoreWebView2HostResourceAccessKind.Allow
            );
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

            if (string.IsNullOrEmpty(args.Uri) || !args.Uri.StartsWith("http"))
            {
                return;
            }

            var uri = new Uri(args.Uri);

            if (uri.Host.Equals(AppSettings.Host, StringComparison.OrdinalIgnoreCase))
            {
                string clickedPath = uri.AbsolutePath;
                string newTitle = null;
                string articlePathPrefix = $"/{AppSettings.ArticlePath}";

                if (
                    !string.IsNullOrEmpty(AppSettings.ArticlePath)
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
            await Windows.System.Launcher.LaunchUriAsync(uri);
        }
    }
}
