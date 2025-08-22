using System;
using System.Linq;
using WikiViewer.Core.Services;
using WikiViewer.Shared.Uwp.Pages;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
using NavigationViewItem = Microsoft.UI.Xaml.Controls.NavigationViewItem;

namespace _1809_UWP.Pages
{
    public sealed partial class MainPage : MainPageBase
    {
        public MainPage()
        {
            this.InitializeComponent();
            ApplyBackdropOrAcrylic();
        }

        protected override Frame ContentFrame => this.PageContentFrame;
        protected override AutoSuggestBox SearchBox => this.NavSearchBox;
        protected override Grid AppTitleBarGrid => this.AppTitleBar;
        protected override ColumnDefinition LeftPaddingColumn => this.TitleBarLeftPaddingColumn;
        protected override ColumnDefinition RightPaddingColumn => this.TitleBarRightPaddingColumn;

        protected override Panel GetWorkerHost() => this.WorkerWebViewHost;

        protected override Type GetArticleViewerPageType() => typeof(ArticleViewerPage);

        protected override Type GetFavouritesPageType() => typeof(FavouritesPage);

        protected override Type GetLoginPageType() => typeof(_1809_UWP.LoginPage);

        protected override Type GetSettingsPageType() => typeof(SettingsPage);

        protected override void UpdateUserUI(bool isLoggedIn, string username)
        {
            if (isLoggedIn)
            {
                LoginNavItem.Content = username;
                LoginNavItem.Icon = new FontIcon
                {
                    FontFamily = new FontFamily("Segoe MDL2 Assets"),
                    Glyph = "\uE77B",
                };
                LoginNavItem.Tag = "userpage";
            }
            else
            {
                LoginNavItem.Content = username ?? "Login";
                LoginNavItem.Icon = new FontIcon
                {
                    FontFamily = new FontFamily("Segoe MDL2 Assets"),
                    Glyph = "\uE77B",
                };
                LoginNavItem.Tag = "login";
            }
        }

        protected override void ShowConnectionInfoBar(
            string title,
            string message,
            bool showActionButton
        )
        {
            ConnectionInfoBar.Title = title;
            ConnectionInfoBar.Message = message;
            InfoBarButton.Visibility = showActionButton
                ? Windows.UI.Xaml.Visibility.Visible
                : Windows.UI.Xaml.Visibility.Collapsed;
            ConnectionInfoBar.IsOpen = true;
        }

        protected override void HideConnectionInfoBar() => ConnectionInfoBar.IsOpen = false;

        protected override void NavigateToPage(Type page, string parameter)
        {
            if (
                page != null
                && (
                    ContentFrame.SourcePageType != page
                    || (ContentFrame.GetNavigationState() as string) != parameter
                )
            )
            {
                ContentFrame.Navigate(
                    page,
                    parameter,
                    new Windows.UI.Xaml.Media.Animation.EntranceNavigationTransitionInfo()
                );
            }
        }

        protected override bool TryGoBack()
        {
            if (ContentFrame.Content is ArticleViewerPage avp && avp.CanGoBackInPage)
            {
                NavView.IsBackEnabled = ContentFrame.CanGoBack || avp.CanGoBackInPage;
                return avp.GoBackInPage();
            }
            if (ContentFrame.CanGoBack)
            {
                ContentFrame.GoBack();
                return true;
            }
            return false;
        }

        private void NavView_Loaded(object sender, Windows.UI.Xaml.RoutedEventArgs e)
        {
            if (ContentFrame.Content == null)
                NavigateToPage(GetArticleViewerPageType(), "Main Page");
        }

        private void NavView_ItemInvoked(
            Microsoft.UI.Xaml.Controls.NavigationView sender,
            Microsoft.UI.Xaml.Controls.NavigationViewItemInvokedEventArgs args
        )
        {
            Type targetPage = null;
            string pageParameter = null;
            if (args.IsSettingsInvoked)
            {
                targetPage = GetSettingsPageType();
            }
            else if (args.InvokedItemContainer?.Tag is string tag)
            {
                switch (tag)
                {
                    case "home":
                        targetPage = GetArticleViewerPageType();
                        pageParameter = "Main Page";
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
                    case "userpage":
                        if (SessionManager.IsLoggedIn)
                        {
                            targetPage = GetArticleViewerPageType();
                            pageParameter = $"User:{SessionManager.Username}";
                        }
                        break;
                }
            }
            if (targetPage != null)
                NavigateToPage(targetPage, pageParameter);
        }

        private void NavView_BackRequested(
            Microsoft.UI.Xaml.Controls.NavigationView sender,
            Microsoft.UI.Xaml.Controls.NavigationViewBackRequestedEventArgs args
        ) => TryGoBack();

        private void ContentFrame_Navigated(
            object sender,
            Windows.UI.Xaml.Navigation.NavigationEventArgs e
        )
        {
            NavView.IsBackEnabled =
                ContentFrame.CanGoBack
                || (ContentFrame.Content as ArticleViewerPage)?.CanGoBackInPage == true;
            Windows
                .UI.Core.SystemNavigationManager.GetForCurrentView()
                .AppViewBackButtonVisibility = NavView.IsBackEnabled
                ? Windows.UI.Core.AppViewBackButtonVisibility.Visible
                : Windows.UI.Core.AppViewBackButtonVisibility.Collapsed;

            if (e.SourcePageType == GetSettingsPageType())
            {
                NavView.SelectedItem = NavView.SettingsItem;
                return;
            }

            string tag = null;
            if (e.SourcePageType == GetFavouritesPageType())
                tag = "favourites";
            else if (e.SourcePageType == GetLoginPageType())
                tag = "login";
            else if (e.SourcePageType == GetArticleViewerPageType() && e.Parameter is string p)
            {
                if (p.Equals("Main Page", StringComparison.OrdinalIgnoreCase))
                    tag = "home";
                else if (p.Equals("random", StringComparison.OrdinalIgnoreCase))
                    tag = "random";
                else if (
                    p.Equals($"User:{SessionManager.Username}", StringComparison.OrdinalIgnoreCase)
                )
                    tag = "userpage";
            }
            NavView.SelectedItem = NavView
                .MenuItems.OfType<NavigationViewItem>()
                .FirstOrDefault(i => (string)i.Tag == tag);
        }

        private void ApplyBackdropOrAcrylic()
        {
            if (
                Windows.Foundation.Metadata.ApiInformation.IsApiContractPresent(
                    "Windows.Foundation.UniversalApiContract",
                    12
                )
            )
            {
                Microsoft.UI.Xaml.Controls.BackdropMaterial.SetApplyToRootOrPageBackground(
                    this,
                    true
                );
            }
            else
            {
                this.Background = new AcrylicBrush
                {
                    BackgroundSource = AcrylicBackgroundSource.HostBackdrop,
                    TintColor = Windows.UI.Colors.Transparent,
                    TintOpacity = 0.6,
                    FallbackColor = Windows.UI.Color.FromArgb(255, 40, 40, 40),
                };
            }
        }
    }
}
