using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using WikiViewer.Core.Models;
using WikiViewer.Core.Services;
using WikiViewer.Shared.Uwp.Services;
using Windows.ApplicationModel.DataTransfer;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;

namespace WikiViewer.Shared.Uwp.Pages
{
    public abstract class ArticleViewerPageBase : Page
    {
        protected string _pageTitleToFetch = "";
        protected string _verificationUrl = null;
        protected bool _isInitialized = false;
        protected readonly Stack<string> _articleHistory = new Stack<string>();
        public bool CanGoBackInPage => _articleHistory.Count > 1;

        protected abstract TextBlock ArticleTitleTextBlock { get; }
        protected abstract TextBlock LoadingTextBlock { get; }
        protected abstract TextBlock LastUpdatedTextBlock { get; }
        protected abstract Grid LoadingOverlayGrid { get; }
        protected abstract Grid VerificationPanelGrid { get; }
        protected abstract AppBarButton EditAppBarButton { get; }
        protected abstract AppBarButton FavoriteAppBarButton { get; }
        protected abstract void ShowLoadingOverlay();
        protected abstract void HideLoadingOverlay();
        protected abstract Task DisplayProcessedHtmlAsync(string html);
        protected abstract void ShowVerificationPanel(string url);
        protected abstract void InitializePlatformControls();
        protected abstract void UninitializePlatformControls();
        protected abstract Type GetEditPageType();

        public ArticleViewerPageBase()
        {
            AuthenticationService.AuthenticationStateChanged += OnAuthenticationStateChanged;
            this.Loaded += Page_Loaded;
            this.Unloaded += Page_Unloaded;
        }

        private void OnAuthenticationStateChanged(
            object sender,
            AuthenticationStateChangedEventArgs e
        )
        {
            if (e.Wiki.Id == SessionManager.CurrentWiki?.Id)
            {
                _ = Dispatcher.RunAsync(
                    Windows.UI.Core.CoreDispatcherPriority.Normal,
                    () =>
                        EditAppBarButton.Visibility = e.IsLoggedIn
                            ? Visibility.Visible
                            : Visibility.Collapsed
                );
            }
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
            EditAppBarButton.Visibility = SessionManager.IsLoggedIn
                ? Visibility.Visible
                : Visibility.Collapsed;
            UpdateFavoriteButton();
        }

        protected void Page_Loaded(object sender, RoutedEventArgs e)
        {
            DataTransferManager.GetForCurrentView().DataRequested += OnDataRequested;
            if (_isInitialized)
                return;

            InitializePlatformControls();
            _isInitialized = true;
            if (!string.IsNullOrEmpty(_pageTitleToFetch) || !string.IsNullOrEmpty(_verificationUrl))
            {
                StartArticleFetch();
            }
        }

        private void Page_Unloaded(object sender, RoutedEventArgs e)
        {
            DataTransferManager.GetForCurrentView().DataRequested -= OnDataRequested;
            AuthenticationService.AuthenticationStateChanged -= OnAuthenticationStateChanged;
            UninitializePlatformControls();
        }

        protected async void StartArticleFetch()
        {
            if (!string.IsNullOrEmpty(_verificationUrl))
            {
                ShowVerificationPanel(_verificationUrl);
                return;
            }
            if (!_isInitialized || VerificationPanelGrid.Visibility == Visibility.Visible)
                return;

            ReviewRequestService.IncrementPageLoadCount();
            ReviewRequestService.TryRequestReview();
            var fetchStopwatch = Stopwatch.StartNew();
            ShowLoadingOverlay();
            LastUpdatedTextBlock.Visibility = Visibility.Collapsed;
            var displayTitle = _pageTitleToFetch.Replace('_', ' ');
            ArticleTitleTextBlock.Text = displayTitle;
            LoadingTextBlock.Text = $"Loading '{displayTitle}'...";

            try
            {
                var (processedHtml, resolvedTitle) =
                    await ArticleProcessingService.FetchAndCacheArticleAsync(
                        _pageTitleToFetch,
                        fetchStopwatch,
                        SessionManager.CurrentApiWorker
                    );
                if (_pageTitleToFetch.Equals("random", StringComparison.OrdinalIgnoreCase))
                {
                    _pageTitleToFetch = resolvedTitle.Replace(' ', '_');
                    _articleHistory.Pop();
                    _articleHistory.Push(_pageTitleToFetch);
                    ArticleTitleTextBlock.Text = resolvedTitle;
                }
                await DisplayProcessedHtmlAsync(processedHtml);
                var lastUpdated = await ArticleProcessingService.FetchLastUpdatedTimestampAsync(
                    _pageTitleToFetch,
                    SessionManager.CurrentApiWorker
                );
                if (lastUpdated.HasValue)
                {
                    LastUpdatedTextBlock.Text =
                        $"Last updated: {lastUpdated.Value.ToLocalTime():g}";
                    LastUpdatedTextBlock.Visibility = Visibility.Visible;
                }
            }
            catch (NeedsUserVerificationException ex)
            {
                ShowVerificationPanel(ex.Url);
            }
            catch (Exception ex)
            {
                ArticleTitleTextBlock.Text = "An error occurred";
                LoadingTextBlock.Text = ex.Message;
            }
            finally
            {
                HideLoadingOverlay();
                UpdateFavoriteButton();
            }
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

        protected void EditButton_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(_pageTitleToFetch))
            {
                App.Navigate(GetEditPageType(), _pageTitleToFetch);
            }
        }

        protected async void FavoriteButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_pageTitleToFetch))
                return;

            var currentWiki = SessionManager.CurrentWiki;
            var currentAccount = SessionManager.CurrentAccount;
            AuthenticationService authService = null;
            if (currentAccount != null)
            {
                authService = new AuthenticationService(
                    currentAccount,
                    currentWiki,
                    App.ApiWorkerFactory
                );
            }

            if (FavouritesService.IsFavourite(_pageTitleToFetch, currentWiki.Id))
            {
                await FavouritesService.RemoveFavoriteAsync(
                    _pageTitleToFetch,
                    currentWiki.Id,
                    authService
                );
            }
            else
            {
                await FavouritesService.AddFavoriteAsync(
                    _pageTitleToFetch,
                    currentWiki.Id,
                    authService
                );
            }
            UpdateFavoriteButton();
        }

        protected void UpdateFavoriteButton()
        {
            if (FavoriteAppBarButton == null)
                return;
            if (
                string.IsNullOrEmpty(_pageTitleToFetch)
                || _pageTitleToFetch.Equals("random", StringComparison.OrdinalIgnoreCase)
            )
            {
                FavoriteAppBarButton.Visibility = Visibility.Collapsed;
                return;
            }
            FavoriteAppBarButton.Visibility = Visibility.Visible;
            if (FavouritesService.IsFavourite(_pageTitleToFetch, SessionManager.CurrentWiki.Id))
            {
                FavoriteAppBarButton.Label = "Remove from Favourites";
                (FavoriteAppBarButton.Icon as SymbolIcon).Symbol = Symbol.UnFavorite;
            }
            else
            {
                FavoriteAppBarButton.Label = "Add to Favourites";
                (FavoriteAppBarButton.Icon as SymbolIcon).Symbol = Symbol.Favorite;
            }
        }

        protected void ShareButton_Click(object sender, RoutedEventArgs e)
        {
            DataTransferManager.ShowShareUI();
        }

        private void OnDataRequested(DataTransferManager sender, DataRequestedEventArgs args)
        {
            DataRequest request = args.Request;
            if (
                !string.IsNullOrEmpty(_pageTitleToFetch)
                && !_pageTitleToFetch.Equals("random", StringComparison.OrdinalIgnoreCase)
            )
            {
                request.Data.Properties.Title = ArticleTitleTextBlock.Text;
                request.Data.Properties.Description =
                    $"Check out this article on {SessionManager.CurrentWiki.Host}.";
                request.Data.SetWebLink(
                    new Uri(SessionManager.CurrentWiki.GetWikiPageUrl(_pageTitleToFetch))
                );
            }
            else
            {
                request.FailWithDisplayText("There is no article loaded to share.");
            }
        }
    }
}
