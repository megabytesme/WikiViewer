using Shared_Code;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using Windows.Storage;
using Windows.Storage.Streams;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;
using Windows.Web;

namespace _1703_UWP
{
    public sealed partial class ArticleViewerPage : Page
    {
        private string _pageTitleToFetch = "";
        private string _verificationUrl = null;
        private bool _isInitialized = false;
        private readonly Stack<string> _articleHistory = new Stack<string>();
        public bool CanGoBackInPage => _articleHistory.Count > 1;

        public ArticleViewerPage()
        {
            this.InitializeComponent();
            AuthService.AuthenticationStateChanged += OnAuthenticationStateChanged;
        }

        private void OnAuthenticationStateChanged(object sender, EventArgs e)
        {
            _ = Dispatcher.RunAsync(
                Windows.UI.Core.CoreDispatcherPriority.Normal,
                () => { EditButton.Visibility = AuthService.IsLoggedIn ? Visibility.Visible : Visibility.Collapsed; }
            );
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            if (e.Parameter is string url && (url.StartsWith("http") || url.StartsWith("https")))
            {
                _verificationUrl = url;
                _pageTitleToFetch = "";
            }
            else if (e.Parameter is string pageTitle && !string.IsNullOrEmpty(pageTitle))
            {
                _pageTitleToFetch = pageTitle.Replace(' ', '_');
                if (_articleHistory.Count == 0 || _articleHistory.Peek() != _pageTitleToFetch)
                {
                    _articleHistory.Clear();
                    _articleHistory.Push(_pageTitleToFetch);
                }
            }
            EditButton.Visibility = AuthService.IsLoggedIn ? Visibility.Visible : Visibility.Collapsed;
            UpdateFavoriteButton();
        }

        private void Page_Loaded(object sender, RoutedEventArgs e)
        {
            DataTransferManager.GetForCurrentView().DataRequested += OnDataRequested;
            if (_isInitialized) return;

            ArticleDisplayWebView.NavigationStarting += ArticleDisplayWebView_NavigationStarting;
            this.Unloaded += ArticleViewerPage_Unloaded;
            _isInitialized = true;
            if (!string.IsNullOrEmpty(_pageTitleToFetch) || !string.IsNullOrEmpty(_verificationUrl))
            {
                StartArticleFetch();
            }
        }

        private void ArticleViewerPage_Unloaded(object sender, RoutedEventArgs e)
        {
            DataTransferManager.GetForCurrentView().DataRequested -= OnDataRequested;
            AuthService.AuthenticationStateChanged -= OnAuthenticationStateChanged;
            if (ArticleDisplayWebView != null) ArticleDisplayWebView.NavigationStarting -= ArticleDisplayWebView_NavigationStarting;
            this.Unloaded -= ArticleViewerPage_Unloaded;
        }

        private async void StartArticleFetch()
        {
            if (!string.IsNullOrEmpty(_verificationUrl))
            {
                ShowVerificationPanelAndRetry(_verificationUrl);
                return;
            }
            if (!_isInitialized || VerificationPanel.Visibility == Visibility.Visible) return;

            ReviewRequestService.IncrementPageLoadCount();
            ReviewRequestService.TryRequestReview();
            var fetchStopwatch = Stopwatch.StartNew();
            ShowLoadingOverlay();
            LastUpdatedText.Visibility = Visibility.Collapsed;
            var displayTitle = _pageTitleToFetch.Replace('_', ' ');
            ArticleTitle.Text = displayTitle;
            LoadingText.Text = $"Loading '{displayTitle}'...";

            try
            {
                var (processedHtml, resolvedTitle) = await ArticleProcessingService.FetchAndCacheArticleAsync(_pageTitleToFetch, fetchStopwatch, MainPage.ApiWorker);
                if (_pageTitleToFetch.Equals("random", StringComparison.OrdinalIgnoreCase))
                {
                    _pageTitleToFetch = resolvedTitle.Replace(' ', '_');
                    _articleHistory.Pop();
                    _articleHistory.Push(_pageTitleToFetch);
                    ArticleTitle.Text = resolvedTitle;
                }
                await DisplayProcessedHtml(processedHtml);
                var lastUpdated = await ArticleProcessingService.FetchLastUpdatedTimestampAsync(_pageTitleToFetch, MainPage.ApiWorker);
                if (lastUpdated.HasValue)
                {
                    LastUpdatedText.Text = $"Last updated: {lastUpdated.Value.ToLocalTime():g}";
                    LastUpdatedText.Visibility = Visibility.Visible;
                }
            }
            catch (NeedsUserVerificationException ex)
            {
                ShowVerificationPanelAndRetry(ex.Url);
            }
            catch (Exception ex)
            {
                ArticleTitle.Text = "An error occurred";
                LoadingText.Text = ex.Message;
            }
            finally
            {
                HideLoadingOverlay();
                UpdateFavoriteButton();
            }
        }

        private async Task DisplayProcessedHtml(string html)
        {
            string finalHtml = html.Replace("src=\"/cache/", $"src=\"ms-local-stream://{AppSettings.GetVirtualHostName()}/cache/");
            StorageFolder localFolder = ApplicationData.Current.LocalFolder;
            StorageFile articleFile = await localFolder.CreateFileAsync("article.html", CreationCollisionOption.ReplaceExisting);
            await FileIO.WriteTextAsync(articleFile, finalHtml);
            Uri localUri = ArticleDisplayWebView.BuildLocalStreamUri(AppSettings.GetVirtualHostName(), "/article.html");
            ArticleDisplayWebView.NavigateToLocalStreamUri(localUri, new LocalStreamResolver());
        }

        private async void ArticleDisplayWebView_NavigationStarting(WebView sender, WebViewNavigationStartingEventArgs args)
        {
            if (args.Uri == null || args.Uri.Scheme == "ms-local-stream") return;
            args.Cancel = true;

            string newTitle = null;
            if (args.Uri.Host.Equals(AppSettings.Host, StringComparison.OrdinalIgnoreCase))
            {
                string clickedPath = args.Uri.AbsolutePath;
                string articlePathPrefix = $"/{AppSettings.ArticlePath}";
                if (!string.IsNullOrEmpty(AppSettings.ArticlePath) && clickedPath.StartsWith(articlePathPrefix))
                {
                    newTitle = clickedPath.Substring(articlePathPrefix.Length);
                }
                else
                {
                    newTitle = clickedPath.TrimStart('/');
                }
                if (!string.IsNullOrEmpty(newTitle))
                {
                    _pageTitleToFetch = Uri.UnescapeDataString(newTitle);
                    _articleHistory.Push(_pageTitleToFetch);
                    StartArticleFetch();
                    return;
                }
            }
            await Windows.System.Launcher.LaunchUriAsync(args.Uri);
        }

        public bool GoBackInPage()
        {
            if (CanGoBackInPage)
            {
                _articleHistory.Pop();
                _pageTitleToFetch = _articleHistory.Peek();
                StartArticleFetch();
                return true;
            }
            return false;
        }

        private void ShowLoadingOverlay()
        {
            LoadingOverlay.IsHitTestVisible = true;
            FadeInAnimation.Begin();
        }

        private void HideLoadingOverlay()
        {
            void onAnimationCompleted(object s, object e)
            {
                LoadingOverlay.IsHitTestVisible = false;
                FadeOutAnimation.Completed -= onAnimationCompleted;
            }
            FadeOutAnimation.Completed += onAnimationCompleted;
            FadeOutAnimation.Begin();
        }

        private void EditButton_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(_pageTitleToFetch))
            {
                Frame.Navigate(typeof(EditPage), _pageTitleToFetch);
            }
        }

        private async void FavoriteButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_pageTitleToFetch)) return;
            if (FavouritesService.IsFavourite(_pageTitleToFetch))
            {
                await FavouritesService.RemoveFavoriteAsync(_pageTitleToFetch);
            }
            else
            {
                await FavouritesService.AddFavoriteAsync(_pageTitleToFetch);
            }
            UpdateFavoriteButton();
        }

        private void UpdateFavoriteButton()
        {
            if (string.IsNullOrEmpty(_pageTitleToFetch) || _pageTitleToFetch.Equals("random", StringComparison.OrdinalIgnoreCase))
            {
                FavoriteButton.Visibility = Visibility.Collapsed;
                return;
            }
            FavoriteButton.Visibility = Visibility.Visible;
            if (FavouritesService.IsFavourite(_pageTitleToFetch))
            {
                FavoriteButton.Label = "Remove from Favourites";
                FavoriteButton.Icon = new SymbolIcon(Symbol.UnFavorite);
            }
            else
            {
                FavoriteButton.Label = "Add to Favourites";
                FavoriteButton.Icon = new SymbolIcon(Symbol.Favorite);
            }
        }

        private void ShareButton_Click(object sender, RoutedEventArgs e)
        {
            DataTransferManager.ShowShareUI();
        }

        private void OnDataRequested(DataTransferManager sender, DataRequestedEventArgs args)
        {
            DataRequest request = args.Request;
            if (!string.IsNullOrEmpty(_pageTitleToFetch) && !_pageTitleToFetch.Equals("random", StringComparison.OrdinalIgnoreCase))
            {
                request.Data.Properties.Title = ArticleTitle.Text;
                request.Data.Properties.Description = $"Check out this article on {AppSettings.Host}.";
                request.Data.SetWebLink(new Uri(AppSettings.GetWikiPageUrl(_pageTitleToFetch)));
            }
            else
            {
                request.FailWithDisplayText("There is no article loaded to share.");
            }
        }

        private void ShowVerificationPanelAndRetry(string url)
        {
            HideLoadingOverlay();
            VerificationPanel.Visibility = Visibility.Visible;
            TypedEventHandler<WebView, WebViewNavigationCompletedEventArgs> successHandler = null;
            successHandler = async (sender, args) =>
            {
                if (args.IsSuccess && !args.Uri.Host.Contains("challenges.cloudflare.com"))
                {
                    VerificationWebView.NavigationCompleted -= successHandler;
                    await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                    {
                        VerificationPanel.Visibility = Visibility.Collapsed;
                        StartArticleFetch();
                    });
                }
            };
            VerificationWebView.NavigationCompleted += successHandler;
            VerificationWebView.Navigate(new Uri(url));
        }

        private class LocalStreamResolver : IUriToStreamResolver
        {
            public IAsyncOperation<IInputStream> UriToStreamAsync(Uri uri)
            {
                if (uri == null) throw new Exception();
                string path = uri.AbsolutePath;
                return GetContent(path).AsAsyncOperation();
            }

            private async Task<IInputStream> GetContent(string path)
            {
                try
                {
                    Uri localUri = new Uri("ms-appdata:///local" + path);
                    StorageFile f = await StorageFile.GetFileFromApplicationUriAsync(localUri);
                    IRandomAccessStream stream = await f.OpenAsync(FileAccessMode.Read);
                    return stream.GetInputStreamAt(0);
                }
                catch (Exception) { throw new Exception("Invalid path"); }
            }
        }
    }
}