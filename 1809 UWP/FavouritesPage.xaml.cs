using System;
using WikiViewer.Shared.Uwp.Pages;
using Windows.UI.Xaml.Controls;

namespace _1809_UWP.Pages
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

        protected override Type GetArticleViewerPageType() => typeof(ArticleViewerPage);
        protected override Type GetEditPageType() => typeof(EditPage);

        protected override void ShowLoadingOverlay()
        {
            var animation = this.Resources["FadeInAnimation"] as Windows.UI.Xaml.Media.Animation.Storyboard;
            LoadingOverlayControl.IsHitTestVisible = true;
            animation.Begin();
        }

        protected override void HideLoadingOverlay()
        {
            var animation = this.Resources["FadeOutAnimation"] as Windows.UI.Xaml.Media.Animation.Storyboard;
            void onAnimationCompleted(object s, object e)
            {
                LoadingOverlayControl.IsHitTestVisible = false;
                animation.Completed -= onAnimationCompleted;
            }
            animation.Completed += onAnimationCompleted;
            animation.Begin();
        }
    }
}