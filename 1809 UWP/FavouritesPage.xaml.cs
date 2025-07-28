using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;

namespace _1809_UWP
{
    public sealed partial class FavouritesPage : Page
    {
        private readonly ObservableCollection<FavouriteItem> _favouritesCollection = new ObservableCollection<FavouriteItem>();

        public FavouritesPage()
        {
            this.InitializeComponent();
            FavouritesGridView.ItemsSource = _favouritesCollection;
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            FavouritesService.FavouritesChanged += OnFavouritesChanged;
            LoadFavourites();
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);
            FavouritesService.FavouritesChanged -= OnFavouritesChanged;
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

                foreach (string title in rawFavourites)
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
                }

                FavouritesGridView.Visibility = Visibility.Visible;
                NoFavouritesTextBlock.Visibility = Visibility.Collapsed;
            }
            else
            {
                FavouritesGridView.Visibility = Visibility.Collapsed;
                NoFavouritesTextBlock.Visibility = Visibility.Visible;
                NoFavouritesTextBlock.Text = AuthService.IsLoggedIn ? "Your watchlist is empty." : "You haven't added any Favourites yet.";
            }
        }

        private void ArticleButton_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.Tag is FavouriteItem item)
            {
                (Window.Current.Content as Frame)?.Navigate(typeof(ArticleViewerPage), item.ArticlePageTitle);
            }

        }

        private void TalkButton_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.Tag is FavouriteItem item)
            {
                (Window.Current.Content as Frame)?.Navigate(typeof(ArticleViewerPage), item.TalkPageTitle);
            }
        }

        private void FavouritesGridView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            BottomCommandBar.Visibility = FavouritesGridView.SelectedItems.Any() ? Visibility.Visible : Visibility.Collapsed;
        }

        private async void DeleteButton_Click(object sender, RoutedEventArgs e)
        {
            var itemsToDelete = FavouritesGridView.SelectedItems.Cast<FavouriteItem>().ToList();

            foreach (var item in itemsToDelete)
            {
                if (item.IsArticleAvailable)
                {
                    await FavouritesService.RemoveFavoriteAsync(item.ArticlePageTitle);
                }
                if (item.IsTalkAvailable)
                {
                    await FavouritesService.RemoveFavoriteAsync(item.TalkPageTitle);
                }
            }
        }
    }
}