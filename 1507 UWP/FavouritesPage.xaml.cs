using System;
using WikiViewer.Core.Services;
using WikiViewer.Shared.Uwp.Pages;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;

namespace _1507_UWP.Pages
{
    public sealed partial class FavouritesPage : FavouritesPageBase
    {
        public FavouritesPage() => this.InitializeComponent();

        protected override GridView FavouritesGridView => FavouritesGridViewControl;
        protected override ListView FavouritesListView => FavouritesListViewControl;
        protected override TextBlock NoFavouritesTextBlock => NoFavouritesTextBlockControl;
        protected override CommandBar BottomCommandBar => BottomCommandBarControl;
        protected override ScrollViewer GridViewScrollViewer => GridViewScrollViewerControl;
        protected override ScrollViewer ListViewScrollViewer => ListViewScrollViewerControl;
        protected override AppBarButton ViewToggleButton => ViewToggleButtonControl;
        protected override AppBarButton DeleteButton => DeleteButtonControl;
        protected override Grid LoadingOverlay => LoadingOverlayControl;
        protected override ComboBox WikiFilterComboBox => WikiFilterComboBoxControl;
        protected override ComboBox SortComboBox => SortComboBoxControl;
        protected override AutoSuggestBox SearchBox => SearchBoxControl;

        protected override Type GetArticleViewerPageType() => typeof(ArticleViewerPage);

        protected override Type GetEditPageType() => typeof(EditPage);

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

            _activeWikiFilter = "";

            var wikiFlyout = (
                (MenuFlyout)((AppBarButton)BottomCommandBar.PrimaryCommands[1]).Flyout
            );
            wikiFlyout.Items.Clear();

            var allItem = new MenuFlyoutItem { Text = "All Wikis", Tag = "" };
            allItem.Click += WikiFilterMenuItem_Click;
            wikiFlyout.Items.Add(allItem);

            foreach (var wiki in WikiManager.GetWikis())
            {
                var item = new MenuFlyoutItem { Text = wiki.Name, Tag = wiki.Id.ToString() };
                item.Click += WikiFilterMenuItem_Click;
                wikiFlyout.Items.Add(item);
            }

            ApplyFavouritesViewAsync();
        }

        protected override void ShowLoadingOverlay()
        {
            var animation =
                this.Resources["FadeInAnimation"] as Windows.UI.Xaml.Media.Animation.Storyboard;
            LoadingOverlayControl.IsHitTestVisible = true;
            animation.Begin();
        }

        protected override void HideLoadingOverlay()
        {
            var animation =
                this.Resources["FadeOutAnimation"] as Windows.UI.Xaml.Media.Animation.Storyboard;
            void onAnimationCompleted(object s, object e)
            {
                LoadingOverlayControl.IsHitTestVisible = false;
                animation.Completed -= onAnimationCompleted;
            }
            animation.Completed += onAnimationCompleted;
            animation.Begin();
        }

        private void SearchFlyout_Opened(object sender, object e)
        {
            SearchBoxControl.Focus(FocusState.Programmatic);
        }
    }
}
