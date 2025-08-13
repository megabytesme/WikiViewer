﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using Microsoft.Web.WebView2.Core;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using Windows.Storage;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;

namespace _1809_UWP
{
    public sealed partial class ArticleViewerPage : Page
    {
        private string _pageTitleToFetch = "";
        private string _verificationUrl = null;
        public static string GetVirtualHostName() => $"local-content.{AppSettings.Host}";
        private bool _isInitialized = false;
        private readonly Stack<string> _articleHistory = new Stack<string>();
        private readonly double _titleBarHeight = 0;
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
                () =>
                {
                    EditButton.Visibility = AuthService.IsLoggedIn
                        ? Visibility.Visible
                        : Visibility.Collapsed;
                }
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

            EditButton.Visibility = AuthService.IsLoggedIn
                ? Visibility.Visible
                : Visibility.Collapsed;
            UpdateFavoriteButton();
        }

        private async void Page_Loaded(object sender, RoutedEventArgs e)
        {
            Environment.SetEnvironmentVariable("WEBVIEW2_DEFAULT_BACKGROUND_COLOR", "00FFFFFF");

            DataTransferManager.GetForCurrentView().DataRequested += OnDataRequested;
            if (_isInitialized)
                return;

            try
            {
                await ArticleDisplayWebView.EnsureCoreWebView2Async();

                var tempFolder = ApplicationData.Current.LocalFolder.Path;
                ArticleDisplayWebView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                    GetVirtualHostName(),
                    tempFolder,
                    CoreWebView2HostResourceAccessKind.Allow
                );

                ArticleDisplayWebView.CoreWebView2.NavigationStarting +=
                    ArticleDisplayWebView_NavigationStarting;

                this.Unloaded += ArticleViewerPage_Unloaded;
                _isInitialized = true;

                if (
                    !string.IsNullOrEmpty(_pageTitleToFetch)
                    || !string.IsNullOrEmpty(_verificationUrl)
                )
                {
                    StartArticleFetch();
                }
            }
            catch (Exception ex)
            {
                ArticleTitle.Text = "Error initializing WebView2";
                LoadingText.Text = ex.Message;
                HideLoadingOverlay();
            }
        }

        private void ArticleViewerPage_Unloaded(object sender, RoutedEventArgs e)
        {
            DataTransferManager.GetForCurrentView().DataRequested -= OnDataRequested;
            AuthService.AuthenticationStateChanged -= OnAuthenticationStateChanged;

            if (ArticleDisplayWebView?.CoreWebView2 != null)
            {
                ArticleDisplayWebView.CoreWebView2.NavigationCompleted -=
                    ArticleDisplayWebView_ContentNavigationCompleted;
                ArticleDisplayWebView.CoreWebView2.NavigationStarting -=
                    ArticleDisplayWebView_NavigationStarting;
            }
            ArticleDisplayWebView?.Close();

            if (VerificationWebView?.CoreWebView2 != null)
            {
                VerificationWebView.Close();
            }

            this.Unloaded -= ArticleViewerPage_Unloaded;
        }

        private async void StartArticleFetch()
        {
            if (!string.IsNullOrEmpty(_verificationUrl))
            {
                ShowVerificationPanelAndRetry(_verificationUrl);
                return;
            }

            if (!_isInitialized || VerificationPanel.Visibility == Visibility.Visible)
                return;

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
                var worker = MainPage.ApiWorker;

                var (processedHtml, resolvedTitle) =
                    await ArticleProcessingService.FetchAndCacheArticleAsync(
                        _pageTitleToFetch,
                        fetchStopwatch,
                        false,
                        worker
                    );

                if (_pageTitleToFetch.Equals("random", StringComparison.OrdinalIgnoreCase))
                {
                    _pageTitleToFetch = resolvedTitle.Replace(' ', '_');
                    _articleHistory.Pop();
                    _articleHistory.Push(_pageTitleToFetch);
                    ArticleTitle.Text = resolvedTitle;
                }

                await DisplayProcessedHtml(processedHtml);

                var lastUpdated = await ArticleProcessingService.FetchLastUpdatedTimestampAsync(
                    _pageTitleToFetch,
                    worker
                );
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
            if (ArticleDisplayWebView.CoreWebView2 == null)
            {
                await ArticleDisplayWebView.EnsureCoreWebView2Async();
            }

            StorageFolder localFolder = ApplicationData.Current.LocalFolder;
            StorageFile articleFile = await localFolder.CreateFileAsync(
                "article.html",
                CreationCollisionOption.ReplaceExisting
            );
            await FileIO.WriteTextAsync(articleFile, html);

            ArticleDisplayWebView.CoreWebView2.Navigate($"https://{GetVirtualHostName()}/article.html");
        }

        private async void ArticleDisplayWebView_NavigationStarting(
            CoreWebView2 sender,
            CoreWebView2NavigationStartingEventArgs args
        )
        {
            Uri uri;
            try
            {
                uri = new Uri(args.Uri);
            }
            catch
            {
                args.Cancel = true;
                return;
            }

            if (uri.Host.Equals(GetVirtualHostName(), StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            args.Cancel = true;

            if (uri.Host.Equals(AppSettings.Host, StringComparison.OrdinalIgnoreCase))
            {
                string newTitle = null;
                string wikiPathSegment = $"/{AppSettings.ArticlePath}";
                string indexPathSegment = $"/{AppSettings.ScriptPath}index.php";

                if (uri.AbsolutePath.StartsWith(wikiPathSegment))
                {
                    newTitle = uri.AbsolutePath.Substring(wikiPathSegment.Length);
                }
                else if (uri.AbsolutePath.StartsWith(indexPathSegment) && uri.Query.Contains("title="))
                {
                    var queryParams = System.Web.HttpUtility.ParseQueryString(uri.Query);
                    newTitle = queryParams["title"];
                }

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
            if (string.IsNullOrEmpty(_pageTitleToFetch))
                return;

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
            if (
                string.IsNullOrEmpty(_pageTitleToFetch)
                || _pageTitleToFetch.Equals("random", StringComparison.OrdinalIgnoreCase)
            )
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
            if (
                !string.IsNullOrEmpty(_pageTitleToFetch)
                && !_pageTitleToFetch.Equals("random", StringComparison.OrdinalIgnoreCase)
            )
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

        private void ArticleDisplayWebView_ContentNavigationCompleted(
            CoreWebView2 sender,
            CoreWebView2NavigationCompletedEventArgs args
        )
        {
            if (args.IsSuccess)
            {
                UpdateWebViewPadding();
            }
        }

        private async void UpdateWebViewPadding()
        {
            if (ArticleDisplayWebView?.CoreWebView2 != null && _titleBarHeight > 0)
            {
                await ArticleDisplayWebView.CoreWebView2.ExecuteScriptAsync(
                    $"document.body.style.paddingTop = '{_titleBarHeight}px';"
                );
            }
        }

        private async void ShowVerificationPanelAndRetry(string url)
        {
            HideLoadingOverlay();
            VerificationPanel.Visibility = Visibility.Visible;
            await VerificationWebView.EnsureCoreWebView2Async();

            TypedEventHandler<CoreWebView2, CoreWebView2NavigationCompletedEventArgs> successHandler = null;
            successHandler = async (sender, args) =>
            {
                if (args.IsSuccess && !sender.Source.Contains("challenges.cloudflare.com"))
                {
                    VerificationWebView.CoreWebView2.NavigationCompleted -= successHandler;

                    await Dispatcher.RunAsync(
                        Windows.UI.Core.CoreDispatcherPriority.Normal,
                        () =>
                        {
                            Debug.WriteLine("[Captcha] Verification successful. Resuming original page load.");

                            VerificationPanel.Visibility = Visibility.Collapsed;

                            StartArticleFetch();
                        }
                    );
                }
            };

            VerificationWebView.CoreWebView2.NavigationCompleted += successHandler;
            VerificationWebView.CoreWebView2.Navigate(url);
        }
    }
}