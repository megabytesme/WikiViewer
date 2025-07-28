using System.Linq;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;

namespace _1809_UWP
{
    public sealed partial class FavoritesPage : Page
    {
        public FavoritesPage()
        {
            this.InitializeComponent();
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            var favorites = FavoritesService.GetFavorites();
            if (favorites.Any())
            {
                FavoritesListView.ItemsSource = favorites;
                FavoritesListView.Visibility = Visibility.Visible;
                NoFavoritesTextBlock.Visibility = Visibility.Collapsed;
            }
            else
            {
                FavoritesListView.Visibility = Visibility.Collapsed;
                NoFavoritesTextBlock.Visibility = Visibility.Visible;
            }
        }

        private void FavoritesListView_ItemClick(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is string pageTitle)
            {
                (Window.Current.Content as Frame)?.Navigate(typeof(ArticleViewerPage), pageTitle);
            }
        }
    }
}