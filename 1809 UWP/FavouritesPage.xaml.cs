using HtmlAgilityPack;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Windows.System;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;

namespace _1809_UWP
{
    public sealed partial class FavouritesPage : Page
    {
        private static bool _isCachingInProgress = false;
        private readonly ObservableCollection<FavouriteItem> _favouritesCollection =
            new ObservableCollection<FavouriteItem>();

        public FavouritesPage()
        {
            this.InitializeComponent();
            FavouritesGridView.ItemsSource = _favouritesCollection;
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            FavouritesService.FavouritesChanged += OnFavouritesChanged;
            BackgroundCacheService.ArticleCached += OnArticleCached;
            LoadFavourites();
            _ = CheckAndStartCachingFavouritesAsync();
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);
            FavouritesService.FavouritesChanged -= OnFavouritesChanged;
            BackgroundCacheService.ArticleCached -= OnArticleCached;
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

        private async Task CheckAndStartCachingFavouritesAsync()
        {
            if (_isCachingInProgress)
            {
                Debug.WriteLine("[FAV PAGE] Caching is already in progress. Skipping.");
                return;
            }

            try
            {
                _isCachingInProgress = true;

                var allFavouriteArticles = FavouritesService.GetFavourites()
                    .Where(t => !t.StartsWith("Talk:") && !t.StartsWith("User talk:"))
                    .ToList();

                if (!allFavouriteArticles.Any()) return;

                var checkTasks = allFavouriteArticles.Select(async title => new
                {
                    Title = title,
                    IsCached = await ArticleCacheManager.GetCacheMetadataAsync(title) != null
                });
                var cacheStatusResults = await Task.WhenAll(checkTasks);

                var titlesToCache = cacheStatusResults
                    .Where(r => !r.IsCached)
                    .Select(r => r.Title)
                    .ToList();

                if (titlesToCache.Any())
                {
                    Debug.WriteLine($"[FAV PAGE] Found {titlesToCache.Count} uncached favourites. Starting background cache task.");
                    await BackgroundCacheService.CacheFavouritesAsync(titlesToCache);
                    Debug.WriteLine("[FAV PAGE] Background cache task has completed.");
                }
                else
                {
                    Debug.WriteLine("[FAV PAGE] All favourites are already cached.");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[FAV PAGE] An error occurred during background cache check: {ex.Message}");
            }
            finally
            {
                _isCachingInProgress = false;
            }
        }

        private async void OnArticleCached(object sender, ArticleCachedEventArgs e)
        {
            var itemToUpdate = _favouritesCollection.FirstOrDefault(item => item.ArticlePageTitle == e.PageTitle);

            if (itemToUpdate != null)
            {
                Debug.WriteLine($"[FAV PAGE] Received cache update for '{e.PageTitle}'. Updating image.");
                await FindAndSetLeadImage(itemToUpdate);
            }
        }

        private void OnFavouritesChanged(object sender, System.EventArgs e)
        {
            _ = Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, LoadFavourites);
        }

        private string GetBaseTitle(string fullTitle)
        {
            if (fullTitle.StartsWith("Talk:"))
            {
                return fullTitle.Substring("Talk:".Length);
            }
            if (fullTitle.StartsWith("User talk:"))
            {
                return fullTitle.Substring("User talk:".Length);
            }
            if (fullTitle.StartsWith("User:"))
            {
                return fullTitle.Substring("User:".Length);
            }
            return fullTitle;
        }

        private void LoadFavourites()
        {
            _favouritesCollection.Clear();
            var rawFavourites = FavouritesService.GetFavourites();

            if (rawFavourites.Any())
            {
                var groupedFavourites = new Dictionary<string, FavouriteItem>();

                foreach (var title in rawFavourites)
                {
                    string baseTitle = GetBaseTitle(title);
                    bool isTalkPage = title.StartsWith("Talk:") || title.StartsWith("User talk:");

                    if (!groupedFavourites.ContainsKey(baseTitle))
                    {
                        groupedFavourites[baseTitle] = new FavouriteItem(baseTitle);
                    }

                    if (isTalkPage)
                    {
                        groupedFavourites[baseTitle].TalkPageTitle = title;
                    }
                    else
                    {
                        groupedFavourites[baseTitle].ArticlePageTitle = title;
                    }
                }

                foreach (var item in groupedFavourites.Values.OrderBy(i => i.DisplayTitle))
                {
                    _favouritesCollection.Add(item);
                    _ = FindAndSetLeadImage(item);
                }

                FavouritesGridView.Visibility = Visibility.Visible;
                NoFavouritesTextBlock.Visibility = Visibility.Collapsed;
            }
            else
            {
                FavouritesGridView.Visibility = Visibility.Collapsed;
                NoFavouritesTextBlock.Visibility = Visibility.Visible;
                NoFavouritesTextBlock.Text = AuthService.IsLoggedIn
                    ? "Your watchlist is empty."
                    : "You haven't added any Favourites yet.";
            }
        }

        private async Task FindAndSetLeadImage(FavouriteItem item)
        {
            if (string.IsNullOrEmpty(item.ArticlePageTitle)) return;

            string cachedHtml = await ArticleCacheManager.GetCachedArticleHtmlAsync(item.ArticlePageTitle);
            if (string.IsNullOrEmpty(cachedHtml)) return;

            var doc = new HtmlDocument();
            doc.LoadHtml(cachedHtml);

            var firstImageNode = doc.DocumentNode.SelectSingleNode($"//img[contains(@src, '{ArticleViewerPage.VirtualHostName}')]");
            if (firstImageNode != null)
            {
                string virtualHostUrl = firstImageNode.GetAttributeValue("src", null);
                if (!string.IsNullOrEmpty(virtualHostUrl))
                {
                    string uwpImageUrl = virtualHostUrl.Replace(
                        $"https://{ArticleViewerPage.VirtualHostName}",
                        "ms-appdata:///local"
                    );

                    item.ImageUrl = uwpImageUrl;
                }
            }
        }

        private async void NavigateToArticleIfItExists(string pageTitle)
        {
            ShowLoadingOverlay();

            try
            {
                var worker = MainPage.ApiWorker;
                bool pageExists = await ArticleProcessingService.PageExistsAsync(pageTitle, worker);

                if (pageExists)
                {
                    App.Navigate(typeof(ArticleViewerPage), pageTitle);
                }
                else
                {
                    var dialog = new ContentDialog
                    {
                        Title = "Page Does Not Exist",
                        Content = $"The page \"{pageTitle.Replace('_', ' ')}\" has not been created yet. Would you like to create it?",
                        PrimaryButtonText = "Create",
                        CloseButtonText = "Cancel",
                        XamlRoot = this.XamlRoot,
                    };

                    var result = await dialog.ShowAsync();

                    if (result == ContentDialogResult.Primary)
                    {
                        if (!AuthService.IsLoggedIn)
                        {
                            var loginDialog = new ContentDialog
                            {
                                Title = "Login Required",
                                Content = "You must be logged in to create or edit pages.",
                                CloseButtonText = "OK",
                                XamlRoot = this.XamlRoot,
                            };
                            await loginDialog.ShowAsync();
                        }
                        else
                        {
                            App.Navigate(typeof(EditPage), pageTitle);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                var errorDialog = new ContentDialog
                {
                    Title = "An Error Occurred",
                    Content = $"Could not verify the page's status. Please try again.\n\nError: {ex.Message}",
                    CloseButtonText = "OK",
                    XamlRoot = this.XamlRoot,
                };
                await errorDialog.ShowAsync();
            }
            finally
            {
                HideLoadingOverlay();
            }
        }

        private void ArticleButton_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.Tag is FavouriteItem item && item.IsArticleAvailable)
            {
                NavigateToArticleIfItExists(item.ArticlePageTitle);
            }
        }

        private void TalkButton_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.Tag is FavouriteItem item && item.IsTalkAvailable)
            {
                NavigateToArticleIfItExists(item.TalkPageTitle);
            }
        }

        private void FavouritesGridView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            BottomCommandBar.Visibility = FavouritesGridView.SelectedItems.Any()
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        private async void DeleteButton_Click(object sender, RoutedEventArgs e)
        {
            var itemsToDelete = FavouritesGridView.SelectedItems.Cast<FavouriteItem>().ToList();
            var titlesToRemove = new HashSet<string>();

            foreach (var item in itemsToDelete)
            {
                if (item.IsArticleAvailable)
                {
                    titlesToRemove.Add(item.ArticlePageTitle);
                }
                else if (item.IsTalkAvailable)
                {
                    titlesToRemove.Add(item.TalkPageTitle);
                }
            }

            foreach (var title in titlesToRemove)
            {
                await FavouritesService.RemoveFavoriteAsync(title);
            }
        }
    }
}