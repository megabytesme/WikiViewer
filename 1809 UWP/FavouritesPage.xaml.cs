using System;
using WikiViewer.Shared.Uwp.Pages;
using Windows.UI.Xaml.Controls;

namespace _1809_UWP.Pages
{
    public sealed partial class FavouritesPage : FavouritesPageBase
    {
        public FavouritesPage() => this.InitializeComponent();

        protected override GridView FavouritesGridViewControl => FavouritesGridView;
        protected override TextBlock NoFavouritesTextBlock => NoFavouritesTextBlockControl;
        protected override CommandBar BottomCommandBar => PageBottomCommandBar;
        protected override ListView FavouritesListViewControl => FavouritesListView;
        protected override ScrollViewer GridViewScrollViewerControl => GridViewScrollViewer;
        protected override ScrollViewer ListViewScrollViewerControl => ListViewScrollViewer;
        protected override AppBarButton ViewToggleButtonControl => ViewToggleButton;

        protected override Type GetArticleViewerPageType() => typeof(ArticleViewerPage);

        protected override Type GetEditPageType() => typeof(EditPage);

        protected override void ShowLoadingOverlay()
        {
            LoadingOverlay.IsHitTestVisible = true;
            FadeInAnimation.Begin();
        }

        protected override void HideLoadingOverlay()
        {
            void onAnimationCompleted(object s, object e)
            {
                LoadingOverlay.IsHitTestVisible = false;
                FadeOutAnimation.Completed -= onAnimationCompleted;
            }
            FadeOutAnimation.Completed += onAnimationCompleted;
            FadeOutAnimation.Begin();
        }
    }
}
