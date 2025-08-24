using System;
using System.Linq;
using System.Threading.Tasks;
using _1703_UWP.ViewModels;
using WikiViewer.Core.Models;
using WikiViewer.Shared.Uwp.Pages;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace _1703_UWP.Pages
{
    public sealed partial class MainPage : MainPageBase
    {
        public MainPage()
        {
            this.InitializeComponent();
            this.SizeChanged += MainPage_SizeChanged;
            this.PageHeaderControl.SearchTextChanged += base.SearchBox_TextChanged;
            this.PageHeaderControl.SearchQuerySubmitted += base.SearchBox_QuerySubmitted;
        }

        protected override Frame ContentFrame => this.PageContentFrame;
        protected override AutoSuggestBox SearchBox => this.PageHeaderControl.SearchBox;
        protected override Grid AppTitleBarGrid => this.AppTitleBar;
        protected override ColumnDefinition LeftPaddingColumn => this.TitleBarLeftPaddingColumn;
        protected override ColumnDefinition RightPaddingColumn => this.TitleBarRightPaddingColumn;

        protected override Panel GetWorkerHost() => this.WorkerWebViewHost;

        protected override Type GetArticleViewerPageType() => typeof(ArticleViewerPage);

        protected override Type GetFavouritesPageType() => typeof(FavouritesPage);

        protected override Type GetLoginPageType() => typeof(_1703_UWP.LoginPage);

        protected override Type GetSettingsPageType() => typeof(SettingsPage);

        private void MainPage_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            double width = e.NewSize.Width;

            if (width >= 1000)
            {
                NavSplitView.DisplayMode = SplitViewDisplayMode.Inline;
                NavSplitView.OpenPaneLength = 250;
                NavSplitView.IsPaneOpen = true;
            }
            else if (width >= 640)
            {
                NavSplitView.DisplayMode = SplitViewDisplayMode.CompactInline;
                NavSplitView.CompactPaneLength = 44;
                NavSplitView.OpenPaneLength = 250;
                NavSplitView.IsPaneOpen = false;
            }
            else
            {
                NavSplitView.DisplayMode = SplitViewDisplayMode.Overlay;
                NavSplitView.CompactPaneLength = 44;
                NavSplitView.OpenPaneLength = 250;
                NavSplitView.IsPaneOpen = false;
            }
        }

        protected override void SetPageTitle_Platform(string title)
        {
            this.PageHeaderControl.Title = title;
        }

        protected override void ShowConnectionInfoBar(
            string title,
            string message,
            bool showActionButton,
            bool isClosable
        )
        {
            this.InfoBarTitle.Text = title;
            this.InfoBarMessage.Text = message;
            this.InfoBarButton.Visibility = showActionButton
                ? Visibility.Visible
                : Visibility.Collapsed;
            this.ConnectionInfoBar.Visibility = Visibility.Visible;
        }

        protected override async Task ShowDialogAsync(string title, string message)
        {
            var dialog = new ContentDialog
            {
                Title = title,
                Content = message,
                CloseButtonText = "OK",
            };
            await dialog.ShowAsync();
        }

        protected override void HideConnectionInfoBar() =>
            this.ConnectionInfoBar.Visibility = Visibility.Collapsed;

        protected override void ClearWikiNavItems()
        {
            this.NavListView.Items.Clear();
        }

        protected override void AddWikiNavItem(WikiInstance wiki)
        {
            this.NavListView.Items.Add(new WikiNavItemViewModel(wiki));
        }

        protected override void AddStandardNavItems()
        {
            return;
        }

        protected override bool TryGoBack()
        {
            if (ContentFrame?.Content is ArticleViewerPage avp && avp.CanGoBackInPage)
                return avp.GoBackInPage();
            if (ContentFrame?.CanGoBack == true)
            {
                ContentFrame.GoBack();
                return true;
            }
            return false;
        }

        private void PageHeader_HamburgerClick(object sender, RoutedEventArgs e)
        {
            this.NavSplitView.IsPaneOpen = !this.NavSplitView.IsPaneOpen;
        }

        private void ContentFrame_Navigated(
            object sender,
            Windows.UI.Xaml.Navigation.NavigationEventArgs e
        )
        {
            if (ContentFrame == null)
                return;
            bool canGoBackInPage =
                (ContentFrame.Content as ArticleViewerPage)?.CanGoBackInPage == true;
            bool canGoBack = ContentFrame.CanGoBack || canGoBackInPage;
            Windows
                .UI.Core.SystemNavigationManager.GetForCurrentView()
                .AppViewBackButtonVisibility = canGoBack
                ? Windows.UI.Core.AppViewBackButtonVisibility.Visible
                : Windows.UI.Core.AppViewBackButtonVisibility.Collapsed;

            if (e.SourcePageType == GetFavouritesPageType())
            {
                this.PageHeaderControl.Title = "Favourites";
            }
            else if (e.SourcePageType == GetSettingsPageType())
            {
                this.PageHeaderControl.Title = "Settings";
            }
            else if (
                e.SourcePageType == GetArticleViewerPageType()
                || e.SourcePageType == typeof(EditPage)
                || e.SourcePageType == typeof(WikiDetailPage)
            )
            {
                this.PageHeaderControl.Title = "";
            }

            this.NavFooterListView.SelectionChanged -= NavListView_SelectionChanged;

            if (e.SourcePageType == GetSettingsPageType())
            {
                var settingsItem = NavFooterListView
                    .Items.OfType<ListViewItem>()
                    .FirstOrDefault(i => "settings".Equals(i.Tag));
                if (settingsItem != null)
                    NavFooterListView.SelectedItem = settingsItem;
                else
                    this.NavFooterListView.SelectedItem = null;
            }
            else if (e.SourcePageType == GetFavouritesPageType())
            {
                var favItem = NavFooterListView
                    .Items.OfType<ListViewItem>()
                    .FirstOrDefault(i => "favourites".Equals(i.Tag));
                if (favItem != null)
                    NavFooterListView.SelectedItem = favItem;
                else
                    this.NavFooterListView.SelectedItem = null;
            }
            else
            {
                this.NavFooterListView.SelectedItem = null;
            }
            this.NavFooterListView.SelectionChanged += NavListView_SelectionChanged;
        }

        private void NavListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (e.AddedItems.FirstOrDefault() is ListViewItem item)
            {
                HandleItemSelection(item);
            }
        }

        private void NavListView_ItemClick(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is ListViewItem item)
            {
                HandleItemSelection(item);
            }
        }

        private void HandleItemSelection(ListViewItem item)
        {
            if (item?.Tag is string tag)
            {
                if (tag == "favourites")
                {
                    NavigateToPage(GetFavouritesPageType(), null);
                }
                else if (tag == "accounts")
                {
                    ShowUserAccountFlyout(item);
                }
                else if (tag == "settings")
                {
                    NavigateToPage(GetSettingsPageType(), null);
                }
            }
        }

        private void HandleWikiNavigation(WikiInstance wiki, string action)
        {
            var pageTitle = action == "random" ? "Special:Random" : "Main Page";
            NavigateToPage(
                GetArticleViewerPageType(),
                new ArticleNavigationParameter { WikiId = wiki.Id, PageTitle = pageTitle }
            );
        }

        private void MainWikiButton_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is WikiNavItemViewModel vm)
            {
                HandleWikiNavigation(vm.Wiki, "home");
            }
        }

        private void ExpanderButton_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is WikiNavItemViewModel vm)
            {
                vm.IsExpanded = !vm.IsExpanded;
            }
        }

        private void SubItemButton_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as FrameworkElement;
            if (button?.DataContext is WikiNavItemViewModel vm)
            {
                string action = button.Tag as string;
                HandleWikiNavigation(vm.Wiki, action);
            }
        }
    }
}
