using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HtmlAgilityPack;
using WikiViewer.Core.Managers;
using WikiViewer.Core.Models;
using WikiViewer.Core.Services;
using WikiViewer.Shared.Uwp.Services;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;

namespace WikiViewer.Shared.Uwp.Pages
{
    public abstract class FavouritesPageBase : Page
    {
        private readonly ObservableCollection<FavouriteItem> _unifiedFavourites =
            new ObservableCollection<FavouriteItem>();
        private bool _isGridView = true;
        protected string _activeWikiFilter = "";
        protected string _activeSortTag = "TitleAsc";
        private string _searchQuery = "";
        private readonly ObservableCollection<FavouriteItem> _visibleFavourites =
            new ObservableCollection<FavouriteItem>();
        private CancellationTokenSource _searchDebounceToken;

        protected abstract ListView FavouritesListView { get; }
        protected abstract GridView FavouritesGridView { get; }
        protected abstract TextBlock NoFavouritesTextBlock { get; }
        protected abstract CommandBar BottomCommandBar { get; }
        protected abstract ScrollViewer GridViewScrollViewer { get; }
        protected abstract ScrollViewer ListViewScrollViewer { get; }
        protected abstract AppBarButton ViewToggleButton { get; }
        protected abstract AppBarButton DeleteButton { get; }
        protected abstract Grid LoadingOverlay { get; }
        protected abstract ComboBox WikiFilterComboBox { get; }
        protected abstract ComboBox SortComboBox { get; }
        protected abstract AutoSuggestBox SearchBox { get; }
        protected abstract void ShowLoadingOverlay();
        protected abstract void HideLoadingOverlay();
        protected abstract Type GetArticleViewerPageType();
        protected abstract Type GetEditPageType();

        public FavouritesPageBase()
        {
            this.Loaded += FavouritesPage_Loaded;
        }

        private void FavouritesPage_Loaded(object sender, RoutedEventArgs e)
        {
            FavouritesGridView.ItemsSource = _visibleFavourites;
            FavouritesListView.ItemsSource = _visibleFavourites;
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            FavouritesService.FavouritesChanged += OnFavouritesChanged;
            ArticleCacheManager.ArticleCached += OnArticleCached;
            LoadAllFavourites();
            _ = PreCacheFavouriteArticlesAsync();
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);
            FavouritesService.FavouritesChanged -= OnFavouritesChanged;
            ArticleCacheManager.ArticleCached -= OnArticleCached;
            _unifiedFavourites.Clear();
        }

        private async void OnArticleCached(object sender, ArticleCachedEventArgs e)
        {
            await Dispatcher.RunAsync(
                Windows.UI.Core.CoreDispatcherPriority.Normal,
                async () =>
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
            );
        }

        private void OnFavouritesChanged(object sender, EventArgs e)
        {
            _ = Dispatcher.RunAsync(
                Windows.UI.Core.CoreDispatcherPriority.Normal,
                () =>
                {
                    LoadAllFavourites();
                    _ = PreCacheFavouriteArticlesAsync();
                }
            );
        }

        private async Task PreCacheFavouriteArticlesAsync()
        {
            var articlesToCache = new Dictionary<string, WikiInstance>();
            foreach (var item in _unifiedFavourites)
            {
                if (
                    !string.IsNullOrEmpty(item.ArticlePageTitle)
                    && !await ArticleCacheManager.IsArticleCachedAsync(
                        item.ArticlePageTitle,
                        item.WikiId
                    )
                )
                {
                    articlesToCache[item.ArticlePageTitle] = WikiManager.GetWikiById(item.WikiId);
                }
            }

            if (articlesToCache.Any())
            {
                await BackgroundCacheService.CacheArticlesAsync(articlesToCache);
            }
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
            }

            NoFavouritesTextBlock.Visibility = _unifiedFavourites.Any()
                ? Visibility.Collapsed
                : Visibility.Visible;

            WikiFilterComboBox.Items.Clear();
            WikiFilterComboBox.Items.Add(new ComboBoxItem { Content = "All Wikis", Tag = "" });

            foreach (var wiki in WikiManager.GetWikis())
            {
                WikiFilterComboBox.Items.Add(
                    new ComboBoxItem { Content = wiki.Name, Tag = wiki.Id }
                );
            }

            WikiFilterComboBox.SelectedIndex = 0;
            SortComboBox.SelectedIndex = 0;
        }

        protected void WikiFilterComboBox_SelectionChanged(
            object sender,
            SelectionChangedEventArgs e
        )
        {
            _activeWikiFilter =
                (WikiFilterComboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "";
            ApplyFavouritesViewAsync();
        }

        protected void SortComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            _activeSortTag =
                (SortComboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "TitleAsc";
            ApplyFavouritesViewAsync();
        }

        protected void SearchBoxControl_TextChanged(
            object sender,
            AutoSuggestBoxTextChangedEventArgs e
        )
        {
            if (e.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
            {
                _searchDebounceToken?.Cancel();
                _searchDebounceToken = new CancellationTokenSource();
                var token = _searchDebounceToken.Token;

                Task.Delay(250, token)
                    .ContinueWith(t =>
                    {
                        if (t.IsCanceled)
                            return;

                        Dispatcher.RunAsync(
                            Windows.UI.Core.CoreDispatcherPriority.Normal,
                            () =>
                            {
                                _searchQuery = SearchBox.Text?.Trim() ?? "";
                                ApplyFavouritesViewAsync();
                            }
                        );
                    });
            }
        }

        protected async void ApplyFavouritesViewAsync()
        {
            ShowLoadingOverlay();

            var filteredSorted = await Task.Run(() =>
            {
                var filtered = _unifiedFavourites.Where(item =>
                {
                    bool matchesWiki =
                        string.IsNullOrEmpty(_activeWikiFilter)
                        || (
                            Guid.TryParse(_activeWikiFilter, out Guid wikiId)
                            && item.WikiId == wikiId
                        );

                    bool matchesSearch =
                        string.IsNullOrWhiteSpace(_searchQuery)
                        || item.DisplayTitle?.IndexOf(
                            _searchQuery,
                            StringComparison.OrdinalIgnoreCase
                        ) >= 0;

                    return matchesWiki && matchesSearch;
                });

                switch (_activeSortTag)
                {
                    case "TitleAsc":
                        filtered = filtered.OrderBy(i => i.DisplayTitle);
                        break;
                    case "TitleDesc":
                        filtered = filtered.OrderByDescending(i => i.DisplayTitle);
                        break;
                    case "WikiAsc":
                        filtered = filtered.OrderBy(i => i.WikiName);
                        break;
                    case "WikiDesc":
                        filtered = filtered.OrderByDescending(i => i.WikiName);
                        break;
                }

                return filtered.ToList();
            });

            _visibleFavourites.Clear();
            foreach (var item in filteredSorted)
                _visibleFavourites.Add(item);

            NoFavouritesTextBlock.Visibility = _visibleFavourites.Any()
                ? Visibility.Collapsed
                : Visibility.Visible;

            HideLoadingOverlay();
        }

        private async Task FindAndSetLeadImage(FavouriteItem item, WikiInstance wiki)
        {
            if (wiki == null || string.IsNullOrEmpty(item.ArticlePageTitle))
                return;

            string cachedHtml = await ArticleCacheManager.GetCachedArticleHtmlAsync(
                item.ArticlePageTitle,
                wiki.Id
            );
            if (string.IsNullOrEmpty(cachedHtml))
            {
                return;
            }

            var doc = new HtmlDocument();
            doc.LoadHtml(cachedHtml);

            var firstImageNode = doc.DocumentNode.SelectSingleNode("//img[@src]");
            if (firstImageNode == null)
                return;

            string originalThumbnailSrc = firstImageNode.GetAttributeValue("src", "");
            if (string.IsNullOrEmpty(originalThumbnailSrc))
                return;

            string absoluteThumbnailUrl = new Uri(
                new Uri(wiki.BaseUrl),
                originalThumbnailSrc
            ).AbsoluteUri;

            var imageLinkNode = doc.DocumentNode.SelectSingleNode(
                $"//a[contains(@href, '/{wiki.ArticlePath}File:')]"
            );
            if (imageLinkNode == null)
            {
                string localUriNoLink = await MediaCacheService.GetLocalUriAsync(
                    absoluteThumbnailUrl,
                    wiki
                );
                if (!string.IsNullOrEmpty(localUriNoLink))
                {
                    item.ImageUrl = localUriNoLink;
                }
                return;
            }

            string fileHref = imageLinkNode.GetAttributeValue("href", "");
            string fileTitle = fileHref.Substring(fileHref.IndexOf("File:"));
            if (string.IsNullOrEmpty(fileTitle))
                return;

            using (var worker = App.ApiWorkerFactory.CreateApiWorker(wiki))
            {
                await worker.InitializeAsync(wiki.BaseUrl);
                string url =
                    $"{wiki.ApiEndpoint}?action=query&prop=imageinfo&iiprop=url&format=json&titles={Uri.EscapeDataString(fileTitle)}";
                string json = await worker.GetJsonFromApiAsync(url);
                if (string.IsNullOrEmpty(json))
                    return;

                var imageResponse =
                    Newtonsoft.Json.JsonConvert.DeserializeObject<ImageQueryResponse>(json);
                string highResUrl = imageResponse
                    ?.query?.pages?.Values.FirstOrDefault()
                    ?.imageinfo?.FirstOrDefault()
                    ?.url;

                if (!string.IsNullOrEmpty(highResUrl))
                {
                    string localUri = await MediaCacheService.GetLocalUriAsync(
                        highResUrl,
                        wiki,
                        absoluteThumbnailUrl
                    );
                    if (!string.IsNullOrEmpty(localUri))
                    {
                        item.ImageUrl = localUri;
                    }
                }
            }
        }

        protected void FavouritesControl_ContainerContentChanging(
            ListViewBase sender,
            ContainerContentChangingEventArgs args
        )
        {
            if (args.Phase != 0)
                return;

            var item = args.Item as FavouriteItem;
            if (item == null)
                return;

            if (
                string.IsNullOrEmpty(item.ImageUrl)
                || item.ImageUrl.Contains("Square150x150Logo.png")
            )
            {
                item.ImageUrl = "ms-appx:///Assets/Square150x150Logo.png";
                var wiki = WikiManager.GetWikiById(item.WikiId);
                if (wiki != null)
                {
                    _ = FindAndSetLeadImage(item, wiki);
                }
            }
        }

        protected async void NavigateToArticleIfItExists(string pageTitle, WikiInstance wiki)
        {
            ShowLoadingOverlay();
            try
            {
                var worker = SessionManager.GetAnonymousWorkerForWiki(wiki);
                await worker.InitializeAsync(wiki.BaseUrl);
                bool pageExists = await ArticleProcessingService.PageExistsAsync(
                    pageTitle,
                    worker,
                    wiki
                );

                if (pageExists)
                {
                    Frame.Navigate(
                        GetArticleViewerPageType(),
                        new ArticleNavigationParameter { WikiId = wiki.Id, PageTitle = pageTitle }
                    );
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
                            Frame.Navigate(
                                GetEditPageType(),
                                new ArticleNavigationParameter
                                {
                                    WikiId = wiki.Id,
                                    PageTitle = pageTitle,
                                }
                            );
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
                (FavouritesGridView.SelectedItems.Any() || FavouritesListView.SelectedItems.Any())
                    ? Visibility.Visible
                    : Visibility.Collapsed;
        }

        protected async void DeleteButton_Click(object sender, RoutedEventArgs e)
        {
            var itemsToDelete = (
                _isGridView ? FavouritesGridView.SelectedItems : FavouritesListView.SelectedItems
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
            ViewToggleButton.Icon = new SymbolIcon(_isGridView ? Symbol.List : Symbol.ViewAll);
            ViewToggleButton.Label = _isGridView ? "List View" : "Grid View";
        }

        protected void WikiFilterMenuItem_Click(object sender, RoutedEventArgs e)
        {
            var menuItem = (MenuFlyoutItem)sender;
            _activeWikiFilter = menuItem.Tag?.ToString() ?? "";
            ApplyFavouritesViewAsync();
        }

        protected void SortMenuItem_Click(object sender, RoutedEventArgs e)
        {
            var menuItem = (MenuFlyoutItem)sender;
            _activeSortTag = menuItem.Tag?.ToString() ?? "TitleAsc";
            ApplyFavouritesViewAsync();
        }
    }
}
