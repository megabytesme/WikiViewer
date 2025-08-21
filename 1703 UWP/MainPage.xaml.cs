using System;
using System.Diagnostics;
using WikiViewer.Shared.Uwp.Pages;
using WikiViewer.Shared.Uwp.Services;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace _1703_UWP.Pages
{
    public sealed partial class MainPage : MainPageBase
    {
        private readonly Frame _contentFrame;

        public MainPage()
        {
            this.InitializeComponent();
            _contentFrame = this.FindName("PageContentFrame") as Frame;
        }

        protected override Frame ContentFrame
        {
            get { return _contentFrame; }
        }

        protected override AutoSuggestBox SearchBox => this.NavSearchBox;
        protected override Grid AppTitleBarGrid => this.AppTitleBar;
        protected override ColumnDefinition LeftPaddingColumn => this.TitleBarLeftPaddingColumn;
        protected override ColumnDefinition RightPaddingColumn => this.TitleBarRightPaddingColumn;

        protected override Panel GetWorkerHost() => this.WorkerWebViewHost;

        protected override Type GetArticleViewerPageType() => typeof(ArticleViewerPage);

        protected override Type GetFavouritesPageType() => typeof(FavouritesPage);

        protected override Type GetLoginPageType() => typeof(_1703_UWP.LoginPage);

        protected override Type GetSettingsPageType() => typeof(SettingsPage);

        protected override void UpdateUserUI(bool isLoggedIn, string username)
        {
            var navItem = this.FindName("LoginNavItem") as RadioButton;
            if (navItem == null)
                return;
            if (isLoggedIn)
            {
                navItem.Content = username ?? "User";
                navItem.Tag = "userpage";
            }
            else
            {
                navItem.Content = username ?? "Login";
                navItem.Tag = "login";
            }
        }

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

        protected override void NavigateToPage(Type page, string parameter)
        {
            var navSplitView = this.FindName("NavSplitView") as SplitView;
            string tag = "";
            if (page == GetArticleViewerPageType())
            {
                if (parameter == WikiViewer.Core.AppSettings.MainPageName)
                    tag = "home";
                else if (parameter == "random")
                    tag = "random";
            }
            else if (page == GetFavouritesPageType())
                tag = "favourites";
            else if (page == GetLoginPageType())
                tag = "login";
            else if (page == GetSettingsPageType())
                tag = "settings";

            if (
                page != null
                && (
                    ContentFrame.SourcePageType != page
                    || (ContentFrame.GetNavigationState() as string) != parameter
                )
            )
                ContentFrame.Navigate(page, parameter);

            foreach (
                var item in (navSplitView.Pane as FrameworkElement).FindChildren<RadioButton>()
            )
            {
                if (item.Tag as string == tag)
                {
                    item.IsChecked = true;
                    break;
                }
            }
        }

        protected override bool TryGoBack()
        {
            if (ContentFrame.Content is ArticleViewerPage avp && avp.CanGoBackInPage)
                return avp.GoBackInPage();
            if (ContentFrame.CanGoBack)
            {
                ContentFrame.GoBack();
                return true;
            }
            return false;
        }

        private void HamburgerButton_Click(object sender, RoutedEventArgs e) =>
            (this.FindName("NavSplitView") as SplitView).IsPaneOpen = !(
                this.FindName("NavSplitView") as SplitView
            ).IsPaneOpen;

        private void NavRadioButton_Checked(object sender, RoutedEventArgs e)
        {
            if (sender is RadioButton rb && rb.Tag is string tag)
            {
                Debug.WriteLine(
                    $"[1703.MainPage.NavRadioButton_Checked] Clicked on tag: '{tag}'. ContentFrame is currently {(ContentFrame == null ? "NULL!" : "VALID")}"
                );

                Type targetPage = null;
                string pageParameter = null;
                switch (tag)
                {
                    case "home":
                        targetPage = GetArticleViewerPageType();
                        pageParameter = WikiViewer.Core.AppSettings.MainPageName;
                        break;
                    case "random":
                        targetPage = GetArticleViewerPageType();
                        pageParameter = "random";
                        break;
                    case "favourites":
                        targetPage = GetFavouritesPageType();
                        break;
                    case "login":
                        targetPage = GetLoginPageType();
                        break;
                    case "settings":
                        targetPage = GetSettingsPageType();
                        break;
                    case "userpage":
                        if (WikiViewer.Core.Services.AuthService.IsLoggedIn)
                        {
                            targetPage = GetArticleViewerPageType();
                            pageParameter = $"User:{WikiViewer.Core.Services.AuthService.Username}";
                        }
                        break;
                }
                if (targetPage != null)
                    NavigateToPage(targetPage, pageParameter);
            }
        }

        private void ContentFrame_Navigated(
            object sender,
            Windows.UI.Xaml.Navigation.NavigationEventArgs e
        )
        {
            bool canGoBackInPage =
                (ContentFrame.Content as ArticleViewerPage)?.CanGoBackInPage == true;
            bool canGoBack = ContentFrame.CanGoBack || canGoBackInPage;
            Windows
                .UI.Core.SystemNavigationManager.GetForCurrentView()
                .AppViewBackButtonVisibility = canGoBack
                ? Windows.UI.Core.AppViewBackButtonVisibility.Visible
                : Windows.UI.Core.AppViewBackButtonVisibility.Collapsed;
        }
    }
}
