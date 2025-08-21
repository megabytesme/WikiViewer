using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using HtmlAgilityPack;
using WikiViewer.Core;
using WikiViewer.Core.Models;
using WikiViewer.Core.Services;
using WikiViewer.Shared.Uwp.Managers;
using WikiViewer.Shared.Uwp.Services;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;

namespace WikiViewer.Shared.Uwp.Pages
{
    public abstract class FavouritesPageBase : Page
    {
        private static bool _isCachingInProgress = false;
        private readonly ObservableCollection<FavouriteItem> _favouritesCollection =
            new ObservableCollection<FavouriteItem>();
        private bool _isGridView = true;
        protected abstract ListView FavouritesListViewControl { get; }
        protected abstract ScrollViewer GridViewScrollViewerControl { get; }
        protected abstract ScrollViewer ListViewScrollViewerControl { get; }
        protected abstract AppBarButton ViewToggleButtonControl { get; }
        protected abstract AppBarButton DeleteButtonControl { get; }

        protected abstract GridView FavouritesGridViewControl { get; }
        protected abstract TextBlock NoFavouritesTextBlock { get; }
        protected abstract CommandBar BottomCommandBar { get; }
        protected abstract void ShowLoadingOverlay();
        protected abstract void HideLoadingOverlay();
        protected abstract Type GetArticleViewerPageType();
        protected abstract Type GetEditPageType();

        public FavouritesPageBase()
        {
            this.Loaded += OnLoaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            FavouritesService.FavouritesChanged += OnFavouritesChanged;
            BackgroundCacheService.ArticleCached += OnArticleCached;
            FavouritesGridViewControl.ItemsSource = _favouritesCollection;
            FavouritesListViewControl.ItemsSource = _favouritesCollection;
            LoadFavourites();
            _ = CheckAndStartCachingFavouritesAsync();
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);
            FavouritesService.FavouritesChanged -= OnFavouritesChanged;
            BackgroundCacheService.ArticleCached -= OnArticleCached;
        }

        private async Task CheckAndStartCachingFavouritesAsync()
        {
            if (_isCachingInProgress)
                return;
            try
            {
                _isCachingInProgress = true;
                var allFavouriteArticles = FavouritesService
                    .GetFavourites()
                    .Where(t => !t.StartsWith("Talk:") && !t.StartsWith("User talk:"))
                    .ToList();
                if (!allFavouriteArticles.Any())
                    return;
                var checkTasks = allFavouriteArticles.Select(async title => new
                {
                    Title = title,
                    IsCached = await ArticleCacheManager.GetCacheMetadataAsync(title) != null,
                });
                var cacheStatusResults = await Task.WhenAll(checkTasks);
                var titlesToCache = cacheStatusResults
                    .Where(r => !r.IsCached)
                    .Select(r => r.Title)
                    .ToList();
                if (titlesToCache.Any())
                {
                    await BackgroundCacheService.CacheFavouritesAsync(titlesToCache);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine(
                    $"[FAV PAGE] An error occurred during background cache check: {ex.Message}"
                );
            }
            finally
            {
                _isCachingInProgress = false;
            }
        }

        private async void OnArticleCached(object sender, ArticleCachedEventArgs e)
        {
            var itemToUpdate = _favouritesCollection.FirstOrDefault(item =>
                item.ArticlePageTitle == e.PageTitle
            );
            if (itemToUpdate != null)
                await FindAndSetLeadImage(itemToUpdate);
        }

        private void OnFavouritesChanged(object sender, EventArgs e)
        {
            _ = Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, LoadFavourites);
        }

        private string GetBaseTitle(string fullTitle)
        {
            if (fullTitle.StartsWith("Talk:"))
                return fullTitle.Substring("Talk:".Length);
            if (fullTitle.StartsWith("User talk:"))
                return fullTitle.Substring("User talk:".Length);
            if (fullTitle.StartsWith("User:"))
                return fullTitle.Substring("User:".Length);
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
                    if (!groupedFavourites.ContainsKey(baseTitle))
                        groupedFavourites[baseTitle] = new FavouriteItem(baseTitle);
                    if (title.StartsWith("Talk:") || title.StartsWith("User talk:"))
                        groupedFavourites[baseTitle].TalkPageTitle = title;
                    else
                        groupedFavourites[baseTitle].ArticlePageTitle = title;
                }
                foreach (var item in groupedFavourites.Values.OrderBy(i => i.DisplayTitle))
                {
                    _favouritesCollection.Add(item);
                    _ = FindAndSetLeadImage(item);
                }
                FavouritesGridViewControl.Visibility = Visibility.Visible;
                NoFavouritesTextBlock.Visibility = Visibility.Collapsed;
            }
            else
            {
                FavouritesGridViewControl.Visibility = Visibility.Collapsed;
                NoFavouritesTextBlock.Visibility = Visibility.Visible;
                NoFavouritesTextBlock.Text = AuthService.IsLoggedIn
                    ? "Your watchlist is empty."
                    : "You haven't added any Favourites yet.";
            }
        }

        private async Task FindAndSetLeadImage(FavouriteItem item)
        {
            item.ImageUrl = "ms-appx:///Assets/Square150x150Logo.png";
            if (string.IsNullOrEmpty(item.ArticlePageTitle))
                return;

            string cachedHtml = await ArticleCacheManager.GetCachedArticleHtmlAsync(
                item.ArticlePageTitle
            );
            if (string.IsNullOrEmpty(cachedHtml))
                return;

            var doc = new HtmlDocument();
            doc.LoadHtml(cachedHtml);

            var firstImageNode = doc.DocumentNode.SelectSingleNode("//img[@src]");

            if (firstImageNode != null)
            {
                string src = firstImageNode.GetAttributeValue("src", "");
                if (!string.IsNullOrEmpty(src))
                {
                    if (!src.StartsWith("/"))
                    {
                        item.ImageUrl = $"ms-appdata:///local/cache/{src}";
                    }
                    else
                    {
                        item.ImageUrl = $"ms-appdata:///local{src}";
                    }
                }
            }
        }

        protected async void NavigateToArticleIfItExists(string pageTitle)
        {
            ShowLoadingOverlay();
            try
            {
                var worker = MainPageBase.ApiWorker;
                bool pageExists = await ArticleProcessingService.PageExistsAsync(pageTitle, worker);
                if (pageExists)
                {
                    App.Navigate(GetArticleViewerPageType(), pageTitle);
                }
                else
                {
                    var dialog = new ContentDialog
                    {
                        Title = "Page Does Not Exist",
                        Content =
                            $"The page \"{pageTitle.Replace('_', ' ')}\" has not been created yet. Would you like to create it?",
                        PrimaryButtonText = "Create",
                        CloseButtonText = "Cancel",
                    };
                    var result = await dialog.ShowAsync();
                    if (result == ContentDialogResult.Primary)
                    {
                        if (!AuthService.IsLoggedIn)
                        {
                            await new ContentDialog
                            {
                                Title = "Login Required",
                                Content = "You must be logged in to create or edit pages.",
                                CloseButtonText = "OK",
                            }.ShowAsync();
                        }
                        else
                        {
                            App.Navigate(GetEditPageType(), pageTitle);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                await new ContentDialog
                {
                    Title = "An Error Occurred",
                    Content =
                        $"Could not verify the page's status. Please try again.\n\nError: {ex.Message}",
                    CloseButtonText = "OK",
                }.ShowAsync();
            }
            finally
            {
                HideLoadingOverlay();
            }
        }

        protected void ArticleButton_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.Tag is FavouriteItem item && item.IsArticleAvailable)
                NavigateToArticleIfItExists(item.ArticlePageTitle);
        }

        protected void TalkButton_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.Tag is FavouriteItem item && item.IsTalkAvailable)
                NavigateToArticleIfItExists(item.TalkPageTitle);
        }

        protected void FavouritesGridView_SelectionChanged(
            object sender,
            SelectionChangedEventArgs e
        ) => UpdateSelection();

        protected void FavouritesListView_SelectionChanged(
            object sender,
            SelectionChangedEventArgs e
        ) => UpdateSelection();

        private void UpdateSelection()
        {
            var gridSelectionCount = FavouritesGridViewControl.SelectedItems.Count;
            var listSelectionCount = FavouritesListViewControl.SelectedItems.Count;

            DeleteButtonControl.Visibility = (gridSelectionCount > 0 || listSelectionCount > 0) ? Visibility.Visible : Visibility.Collapsed;
        }

        protected async void DeleteButton_Click(object sender, RoutedEventArgs e)
        {
            var itemsToDelete = (_isGridView ? FavouritesGridViewControl.SelectedItems : FavouritesListViewControl.SelectedItems)
                                .Cast<FavouriteItem>().ToList();

            var titlesToRemove = new HashSet<string>();
            foreach (var item in itemsToDelete)
            {
                if (item.IsArticleAvailable) titlesToRemove.Add(item.ArticlePageTitle);
                if (item.IsTalkAvailable) titlesToRemove.Add(item.TalkPageTitle);
            }

            foreach (var title in titlesToRemove)
            {
                await FavouritesService.RemoveFavoriteAsync(title);
            }

            FavouritesGridViewControl.SelectedItem = null;
            FavouritesListViewControl.SelectedItem = null;
        }

        protected void ViewToggleButton_Click(object sender, RoutedEventArgs e)
        {
            _isGridView = !_isGridView;
            if (_isGridView)
            {
                GridViewScrollViewerControl.Visibility = Visibility.Visible;
                ListViewScrollViewerControl.Visibility = Visibility.Collapsed;
                ViewToggleButtonControl.Icon = new SymbolIcon(Symbol.List);
                ViewToggleButtonControl.Label = "List View";
            }
            else
            {
                GridViewScrollViewerControl.Visibility = Visibility.Collapsed;
                ListViewScrollViewerControl.Visibility = Visibility.Visible;
                ViewToggleButtonControl.Icon = new SymbolIcon(Symbol.ViewAll);
                ViewToggleButtonControl.Label = "Grid View";
            }
        }
    }
}
