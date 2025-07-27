using System;
using System.Linq;
using Windows.ApplicationModel.Core;
using Windows.UI;
using Windows.UI.Core;
using Windows.UI.ViewManagement;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;
using muxc = Microsoft.UI.Xaml.Controls;

namespace _1809_UWP
{
    public sealed partial class MainPage : Page
    {
        public MainPage()
        {
            this.InitializeComponent();
            ApplyBackdropOrAcrylic();
            SetupTitleBar();
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            SystemNavigationManager.GetForCurrentView().BackRequested += System_BackRequested;
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);
            SystemNavigationManager.GetForCurrentView().BackRequested -= System_BackRequested;
        }

        private void ApplyBackdropOrAcrylic()
        {
            if (Windows.Foundation.Metadata.ApiInformation.IsApiContractPresent("Windows.Foundation.UniversalApiContract", 12))
            {
                muxc.BackdropMaterial.SetApplyToRootOrPageBackground(this, true);
            }
            else
            {
                this.Background = new AcrylicBrush
                {
                    BackgroundSource = AcrylicBackgroundSource.HostBackdrop,
                    TintColor = Colors.Transparent,
                    TintOpacity = 0.6,
                    FallbackColor = Color.FromArgb(255, 40, 40, 40)
                };
            }
        }

        private void SetupTitleBar()
        {
            CoreApplication.GetCurrentView().TitleBar.ExtendViewIntoTitleBar = true;

            Window.Current.SetTitleBar(AppTitleBar);

            var titleBar = ApplicationView.GetForCurrentView().TitleBar;
            titleBar.ButtonBackgroundColor = Colors.Transparent;
            titleBar.ButtonInactiveBackgroundColor = Colors.Transparent;

            CoreApplication.GetCurrentView().TitleBar.LayoutMetricsChanged += (s, e) => UpdateTitleBarLayout();
        }

        private void UpdateTitleBarLayout()
        {
            if (CoreApplication.GetCurrentView().TitleBar.ExtendViewIntoTitleBar)
            {
                LeftPaddingColumn.Width = new GridLength(CoreApplication.GetCurrentView().TitleBar.SystemOverlayLeftInset);
                RightPaddingColumn.Width = new GridLength(CoreApplication.GetCurrentView().TitleBar.SystemOverlayRightInset);
            }
            else
            {
                LeftPaddingColumn.Width = new GridLength(0);
                RightPaddingColumn.Width = new GridLength(0);
            }
        }

        private void NavView_Loaded(object sender, RoutedEventArgs e)
        {
            if (ContentFrame.Content == null)
            {
                NavView.SelectedItem = NavView.MenuItems.OfType<muxc.NavigationViewItem>().FirstOrDefault();
                ContentFrame.Navigate(typeof(HomePage));
            }
        }

        private void NavView_ItemInvoked(muxc.NavigationView sender, muxc.NavigationViewItemInvokedEventArgs args)
        {
            if (args.IsSettingsInvoked) return;

            if (args.InvokedItemContainer?.Tag is string tag)
            {
                if (tag == "home")
                {
                    if (ContentFrame.SourcePageType != typeof(HomePage))
                    {
                        ContentFrame.Navigate(typeof(HomePage), null, args.RecommendedNavigationTransitionInfo);
                    }
                }
                else if (tag == "random")
                {
                    ContentFrame.Navigate(typeof(ArticleViewerPage), "random", args.RecommendedNavigationTransitionInfo);
                }
            }
        }

        private void SearchBox_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
        {
            if (!string.IsNullOrEmpty(args.QueryText))
            {
                ContentFrame.Navigate(typeof(ArticleViewerPage), args.QueryText);
            }
        }

        private void ContentFrame_Navigated(object sender, NavigationEventArgs e)
        {
            bool canGoBackInArticlePage = (ContentFrame.Content as ArticleViewerPage)?.CanGoBackInPage ?? false;

            NavView.IsBackEnabled = ContentFrame.CanGoBack || canGoBackInArticlePage;

            SystemNavigationManager.GetForCurrentView().AppViewBackButtonVisibility =
                NavView.IsBackEnabled ? AppViewBackButtonVisibility.Visible : AppViewBackButtonVisibility.Collapsed;

            if (ContentFrame.SourcePageType == typeof(HomePage))
            {
                NavView.SelectedItem = NavView.MenuItems[0];
            }
        }

        private void NavView_BackRequested(muxc.NavigationView sender, muxc.NavigationViewBackRequestedEventArgs args)
        {
            TryGoBack();
        }

        private void System_BackRequested(object sender, BackRequestedEventArgs e)
        {
            if (!e.Handled)
            {
                e.Handled = TryGoBack();
            }
        }

        private bool TryGoBack()
        {
            if (ContentFrame.Content is ArticleViewerPage articlePage && articlePage.GoBackInPage())
            {
                NavView.IsBackEnabled = ContentFrame.CanGoBack || articlePage.CanGoBackInPage;

                return true;
            }

            if (ContentFrame.CanGoBack)
            {
                ContentFrame.GoBack();
                return true;
            }

            return false;
        }
    }
}