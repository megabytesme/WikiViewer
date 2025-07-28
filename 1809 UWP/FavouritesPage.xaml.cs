using System.Linq;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;

namespace _1809_UWP
{
    public sealed partial class FavouritesPage : Page
    {
        public FavouritesPage()
        {
            this.InitializeComponent();
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

        private void FavouritesListView_ItemClick(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is string pageTitle)
            {
                (Window.Current.Content as Frame)?.Navigate(typeof(ArticleViewerPage), pageTitle);
            }
        }

        private void LoadFavourites()
        {
            var Favourites = FavouritesService.GetFavourites();
            if (Favourites.Any())
            {
                FavouritesListView.ItemsSource = Favourites;
                FavouritesListView.Visibility = Visibility.Visible;
                NoFavouritesTextBlock.Visibility = Visibility.Collapsed;
            }
            else
            {
                FavouritesListView.Visibility = Visibility.Collapsed;
                NoFavouritesTextBlock.Visibility = Visibility.Visible;
                NoFavouritesTextBlock.Text = AuthService.IsLoggedIn ? "Your watchlist is empty." : "You haven't added any Favourites yet.";
            }
        }
    }
}