using System;
using WikiViewer.Core.Models;
using WikiViewer.Shared.Uwp.Pages;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Data;

namespace _1703_UWP.Pages
{
    public sealed partial class MainPage : MainPageBase
    {
        public MainPage()
        {
            this.InitializeComponent();
        }

        protected override Frame ContentFrame => this.FindName("PageContentFrame") as Frame;
        protected override AutoSuggestBox SearchBox =>
            this.FindName("NavSearchBox") as AutoSuggestBox;
        protected override Grid AppTitleBarGrid => this.FindName("AppTitleBar") as Grid;
        protected override ColumnDefinition LeftPaddingColumn =>
            this.FindName("TitleBarLeftPaddingColumn") as ColumnDefinition;
        protected override ColumnDefinition RightPaddingColumn =>
            this.FindName("TitleBarRightPaddingColumn") as ColumnDefinition;

        protected override Panel GetWorkerHost() => this.FindName("WorkerWebViewHost") as Grid;

        protected override Type GetArticleViewerPageType() => typeof(ArticleViewerPage);

        protected override Type GetFavouritesPageType() => typeof(FavouritesPage);

        protected override Type GetLoginPageType() => typeof(_1703_UWP.LoginPage);

        protected override Type GetSettingsPageType() => typeof(SettingsPage);

        protected override void ShowConnectionInfoBar(
            string title,
            string message,
            bool showActionButton
        )
        {
            (this.FindName("InfoBarTitle") as TextBlock).Text = title;
            (this.FindName("InfoBarMessage") as TextBlock).Text = message;
            (this.FindName("InfoBarButton") as Button).Visibility = showActionButton
                ? Visibility.Visible
                : Visibility.Collapsed;
            (this.FindName("ConnectionInfoBar") as Border).Visibility = Visibility.Visible;
        }

        protected override void HideConnectionInfoBar() =>
            (this.FindName("ConnectionInfoBar") as Border).Visibility = Visibility.Collapsed;

        #region Abstract Method Implementations

        protected override void ClearWikiNavItems()
        {
            var navListView = this.FindName("NavListView") as ListView;
            navListView.Items.Clear();
        }

        protected override void AddWikiNavItem(WikiInstance wiki)
        {
            var navListView = this.FindName("NavListView") as ListView;
            var item = new ListViewItem { DataContext = wiki };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }
            );
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var contentStack = new StackPanel { Orientation = Orientation.Horizontal };
            var icon = new BitmapIcon();
            var binding = new Binding
            {
                Source = wiki,
                Path = new PropertyPath("IconUrl"),
                Mode = BindingMode.OneWay,
                TargetNullValue = new SymbolIcon(Symbol.Globe),
            };
            BindingOperations.SetBinding(icon, BitmapIcon.UriSourceProperty, binding);

            contentStack.Children.Add(icon);
            contentStack.Children.Add(
                new TextBlock
                {
                    Text = wiki.Name,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(12, 0, 0, 0),
                }
            );
            Grid.SetColumn(contentStack, 0);
            grid.Children.Add(contentStack);

            item.Content = grid;
            item.Tapped += (s, e) =>
                NavigateToPage(
                    GetArticleViewerPageType(),
                    new ArticleNavigationParameter { WikiId = wiki.Id, PageTitle = "Main Page" }
                );

            var flyout = new MenuFlyout();
            var homeItem = new MenuFlyoutItem
            {
                Text = "Home Page",
                Icon = new SymbolIcon(Symbol.Home),
            };
            homeItem.Click += (s, e) =>
                NavigateToPage(
                    GetArticleViewerPageType(),
                    new ArticleNavigationParameter { WikiId = wiki.Id, PageTitle = "Main Page" }
                );
            flyout.Items.Add(homeItem);

            var randomItem = new MenuFlyoutItem
            {
                Text = "Random Article",
                Icon = new SymbolIcon(Symbol.Shuffle),
            };
            randomItem.Click += (s, e) =>
                NavigateToPage(
                    GetArticleViewerPageType(),
                    new ArticleNavigationParameter
                    {
                        WikiId = wiki.Id,
                        PageTitle = "Special:Random",
                    }
                );
            flyout.Items.Add(randomItem);

            item.ContextFlyout = flyout;
            item.RightTapped += (s, e) =>
                flyout.ShowAt(s as UIElement, e.GetPosition(s as UIElement));

            navListView.Items.Add(item);
        }

        protected override void AddStandardNavItems()
        {
            var navListView = this.FindName("NavListView") as ListView;

            navListView.Items.Add(
                new ListViewItem
                {
                    Content = new TextBlock(),
                    Height = 1,
                    MinHeight = 1,
                    Padding = new Thickness(0),
                    Margin = new Thickness(12, 8, 12, 8),
                    Background = (Windows.UI.Xaml.Media.Brush)
                        Resources["SystemControlForegroundBaseLowBrush"],
                }
            );

            var favItem = new ListViewItem { Tag = "favourites" };
            var favStack = new StackPanel { Orientation = Orientation.Horizontal };
            favStack.Children.Add(
                new SymbolIcon(Symbol.Favorite) { Margin = new Thickness(0, 0, 12, 0) }
            );
            favStack.Children.Add(
                new TextBlock { Text = "Favourites", VerticalAlignment = VerticalAlignment.Center }
            );
            favItem.Content = favStack;
            navListView.Items.Add(favItem);

            var accountsItem = new ListViewItem { Tag = "accounts" };
            var accStack = new StackPanel { Orientation = Orientation.Horizontal };
            accStack.Children.Add(
                new SymbolIcon(Symbol.Contact) { Margin = new Thickness(0, 0, 12, 0) }
            );
            accStack.Children.Add(
                new TextBlock { Text = "Accounts", VerticalAlignment = VerticalAlignment.Center }
            );
            accountsItem.Content = accStack;
            navListView.Items.Add(accountsItem);
        }

        #endregion

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

        private void HamburgerButton_Click(object sender, RoutedEventArgs e)
        {
            var navSplitView = this.FindName("NavSplitView") as SplitView;
            if (navSplitView != null)
                navSplitView.IsPaneOpen = !navSplitView.IsPaneOpen;
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
        }

        private void NavListView_ItemClick(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is FrameworkElement item && item.Tag is string tag)
            {
                if (tag == "favourites")
                {
                    NavigateToPage(GetFavouritesPageType(), null);
                }
                else if (tag == "accounts")
                {
                    ShowUserAccountFlyout(item);
                }
            }
        }
    }
}
