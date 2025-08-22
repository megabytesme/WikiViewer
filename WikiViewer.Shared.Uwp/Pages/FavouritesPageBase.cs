using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using HtmlAgilityPack;
using WikiViewer.Core.Models;
using WikiViewer.Core.Services;
using WikiViewer.Shared.Uwp.Managers;
using WikiViewer.Shared.Uwp.Services;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Navigation;

namespace WikiViewer.Shared.Uwp.Pages
{
    public class FavouriteGroup
    {
        public WikiInstance Wiki { get; set; }
        public ObservableCollection<FavouriteItem> Favourites { get; set; }
    }

    public abstract class FavouritesPageBase : Page
    {
        private static bool _isCachingInProgress = false;
        private readonly ObservableCollection<FavouriteItem> _unifiedFavourites =
            new ObservableCollection<FavouriteItem>();
        private bool _isGridView = true;

        protected abstract ListView FavouritesListView { get; }
        protected abstract GridView FavouritesGridView { get; }
        protected abstract TextBlock NoFavouritesTextBlock { get; }
        protected abstract CommandBar BottomCommandBar { get; }
        protected abstract ScrollViewer GridViewScrollViewer { get; }
        protected abstract ScrollViewer ListViewScrollViewer { get; }
        protected abstract AppBarButton ViewToggleButton { get; }
        protected abstract AppBarButton DeleteButton { get; }
        protected abstract Grid LoadingOverlay { get; }
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

            FavouritesGridView.ItemsSource = _unifiedFavourites;
            FavouritesListView.ItemsSource = _unifiedFavourites;

            LoadAllFavourites();
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
                var articlesToCache = new Dictionary<string, WikiInstance>();
                foreach (var item in _unifiedFavourites)
                {
                    if (!string.IsNullOrEmpty(item.ArticlePageTitle))
                    {
                        if (
                            !await ArticleCacheManager.IsArticleCachedAsync(
                                item.ArticlePageTitle,
                                item.WikiId
                            )
                        )
                        {
                            articlesToCache[item.ArticlePageTitle] = WikiManager.GetWikiById(
                                item.WikiId
                            );
                        }
                    }
                }

                if (articlesToCache.Any())
                {
                    await BackgroundCacheService.CacheArticlesAsync(articlesToCache);
                }
            }
            finally
            {
                _isCachingInProgress = false;
            }
        }

        private async void OnArticleCached(object sender, ArticleCachedEventArgs e)
        {
            var itemToUpdate = _unifiedFavourites.FirstOrDefault(item =>
                item.ArticlePageTitle == e.PageTitle
            );
            if (itemToUpdate != null)
            {
                var wiki = WikiManager.GetWikiById(itemToUpdate.WikiId);
                await FindAndSetLeadImage(itemToUpdate, wiki);
            }
        }

        private void OnFavouritesChanged(object sender, EventArgs e)
        {
            _ = Dispatcher.RunAsync(
                Windows.UI.Core.CoreDispatcherPriority.Normal,
                LoadAllFavourites
            );
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

        private void LoadAllFavourites()
        {
            _unifiedFavourites.Clear();
            var allWikis = WikiManager.GetWikis();
            var tempList = new List<FavouriteItem>();

            foreach (var wiki in allWikis)
            {
                var rawFavourites = FavouritesService.GetFavourites(wiki.Id);
                if (!rawFavourites.Any())
                    continue;

                var groupedFavourites = new Dictionary<string, FavouriteItem>();
                foreach (var title in rawFavourites)
                {
                    string baseTitle = GetBaseTitle(title);
                    if (!groupedFavourites.ContainsKey(baseTitle))
                    {
                        groupedFavourites[baseTitle] = new FavouriteItem(baseTitle)
                        {
                            WikiId = wiki.Id,
                            WikiName = wiki.Name,
                        };
                    }
                    if (title.StartsWith("Talk:") || title.StartsWith("User talk:"))
                        groupedFavourites[baseTitle].TalkPageTitle = title;
                    else
                        groupedFavourites[baseTitle].ArticlePageTitle = title;
                }
                tempList.AddRange(groupedFavourites.Values);
            }

            foreach (var item in tempList.OrderBy(i => i.DisplayTitle))
            {
                _unifiedFavourites.Add(item);
                var wiki = WikiManager.GetWikiById(item.WikiId);
                _ = FindAndSetLeadImage(item, wiki);
            }

            NoFavouritesTextBlock.Visibility = _unifiedFavourites.Any()
                ? Visibility.Collapsed
                : Visibility.Visible;
            if (!_unifiedFavourites.Any())
            {
                NoFavouritesTextBlock.Text =
                    "You haven't added any Favourites yet across any of your wikis.";
            }
        }

        private async Task FindAndSetLeadImage(FavouriteItem item, WikiInstance wiki)
        {
            item.ImageUrl = "ms-appx:///Assets/Square150x150Logo.png";
            if (wiki == null || string.IsNullOrEmpty(item.ArticlePageTitle))
                return;

            string cachedHtml = await ArticleCacheManager.GetCachedArticleHtmlAsync(
                item.ArticlePageTitle,
                wiki.Id
            );
            if (string.IsNullOrEmpty(cachedHtml))
                return;

            var doc = new HtmlAgilityPack.HtmlDocument();
            doc.LoadHtml(cachedHtml);
            var firstImageNode = doc.DocumentNode.SelectSingleNode("//img[@src]");
            if (firstImageNode != null)
            {
                string src = firstImageNode.GetAttributeValue("src", "");
                if (!string.IsNullOrEmpty(src))
                {
                    item.ImageUrl = src.StartsWith("/")
                        ? $"ms-appdata:///local{src}"
                        : $"ms-appdata:///local/cache/{src}";
                }
            }
        }

        protected async void NavigateToArticleIfItExists(string pageTitle, WikiInstance wiki)
        {
            ShowLoadingOverlay();
            try
            {
                var worker = App.ApiWorkerFactory.CreateApiWorker(wiki.PreferredConnectionMethod);
                await worker.InitializeAsync(wiki.BaseUrl);
                bool pageExists = await ArticleProcessingService.PageExistsAsync(pageTitle, worker);

                if (pageExists)
                {
                    SessionManager.SetCurrentWiki(wiki);
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
                        SessionManager.SetCurrentWiki(wiki);
                        var accountForWiki = AccountManager
                            .GetAccountsForWiki(wiki.Id)
                            .FirstOrDefault(a => a.IsLoggedIn);
                        if (accountForWiki == null)
                        {
                            await new ContentDialog
                            {
                                Title = "Login Required",
                                Content =
                                    "You must be logged in to create or edit pages on this wiki.",
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
            if ((sender as FrameworkElement)?.DataContext is FavouriteItem item)
            {
                var wiki = WikiManager.GetWikiById(item.WikiId);
                if (wiki != null && item.IsArticleAvailable)
                {
                    NavigateToArticleIfItExists(item.ArticlePageTitle, wiki);
                }
            }
        }

        protected void TalkButton_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is FavouriteItem item)
            {
                var wiki = WikiManager.GetWikiById(item.WikiId);
                if (wiki != null && item.IsTalkAvailable)
                {
                    NavigateToArticleIfItExists(item.TalkPageTitle, wiki);
                }
            }
        }

        private FavouriteItem FindItemFromDataContext(object dataContext)
        {
            return dataContext as FavouriteItem;
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
            DeleteButton.Visibility =
                (
                    FavouritesGridView.SelectedItems.Any()
                    || FavouritesListView.SelectedItems.Any()
                )
                    ? Visibility.Visible
                    : Visibility.Collapsed;
        }

        protected async void DeleteButton_Click(object sender, RoutedEventArgs e)
        {
            var itemsToDelete = (
                _isGridView
                    ? FavouritesGridView.SelectedItems
                    : FavouritesListView.SelectedItems
            )
                .Cast<FavouriteItem>()
                .ToList();
            var itemsByWiki = itemsToDelete.GroupBy(item => item.WikiId);

            foreach (var group in itemsByWiki)
            {
                var wikiId = group.Key;
                var wiki = WikiManager.GetWikiById(wikiId);
                var account = AccountManager
                    .GetAccountsForWiki(wikiId)
                    .FirstOrDefault(a => a.IsLoggedIn);
                var authService =
                    (account != null)
                        ? new AuthenticationService(account, wiki, App.ApiWorkerFactory)
                        : null;

                var titlesToRemove = group
                    .Select(item => item.ArticlePageTitle)
                    .Where(t => !string.IsNullOrEmpty(t))
                    .ToList();

                if (titlesToRemove.Any())
                {
                    await FavouritesService.RemoveMultipleFavoritesAsync(
                        titlesToRemove,
                        wikiId,
                        authService
                    );
                }
            }

            FavouritesGridView.SelectedItem = null;
            FavouritesListView.SelectedItem = null;
        }

        protected void ViewToggleButton_Click(object sender, RoutedEventArgs e)
        {
            _isGridView = !_isGridView;
            GridViewScrollViewer.Visibility = _isGridView
                ? Visibility.Visible
                : Visibility.Collapsed;
            ListViewScrollViewer.Visibility = _isGridView
                ? Visibility.Collapsed
                : Visibility.Visible;
            ViewToggleButton.Icon = new SymbolIcon(
                _isGridView ? Symbol.List : Symbol.ViewAll
            );
            ViewToggleButton.Label = _isGridView ? "List View" : "Grid View";
        }
    }
}
