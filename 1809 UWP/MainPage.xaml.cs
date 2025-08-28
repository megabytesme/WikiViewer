using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using WikiViewer.Core.Models;
using WikiViewer.Core.Services;
using WikiViewer.Shared.Uwp.Pages;
using Windows.ApplicationModel.Core;
using Windows.UI;
using Windows.UI.ViewManagement;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Data;
using NavigationView = Microsoft.UI.Xaml.Controls.NavigationView;
using NavigationViewBackRequestedEventArgs = Microsoft.UI.Xaml.Controls.NavigationViewBackRequestedEventArgs;
using NavigationViewItemInvokedEventArgs = Microsoft.UI.Xaml.Controls.NavigationViewItemInvokedEventArgs;
using NavigationViewItemSeparator = Microsoft.UI.Xaml.Controls.NavigationViewItemSeparator;

namespace _1809_UWP.Pages
{
    public sealed partial class MainPage : MainPageBase
    {
        private static readonly Random _random = new Random();

        public MainPage()
        {
            this.InitializeComponent();
            this.Loaded += MainPage_Loaded;
        }

        private void MainPage_Loaded(object sender, RoutedEventArgs e)
        {
            SetupTitleBar();
        }

        private readonly Queue<DialogInfo> _infoBarQueue = new Queue<DialogInfo>();
        private readonly SemaphoreSlim _infoBarSemaphore = new SemaphoreSlim(1, 1);

        private struct DialogInfo
        {
            public string Title;
            public string Message;
            public bool ShowActionButton;
            public bool IsClosable;
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

        protected override void SetPageTitle_Platform(string title)
        {
            return;
        }

        public void UpdateBackButtonState()
        {
            bool canGoBackInFrame = ContentFrame.CanGoBack;
            bool canGoBackInPage =
                (ContentFrame.Content as ArticleViewerPage)?.CanGoBackInPage == true;
            NavView.IsBackEnabled = canGoBackInFrame || canGoBackInPage;
        }

        public override void UpdatePlatformBackButton()
        {
            UpdateBackButtonState();
        }

        protected override async Task ShowConnectionInfoBarAsync(
            string title,
            string message,
            bool showActionButton,
            bool isClosable
        )
        {
            _infoBarQueue.Enqueue(
                new DialogInfo
                {
                    Title = title,
                    Message = message,
                    ShowActionButton = showActionButton,
                    IsClosable = isClosable,
                }
            );
            await ProcessInfoBarQueueAsync();
        }

        private async Task ProcessInfoBarQueueAsync()
        {
            await _infoBarSemaphore.WaitAsync();
            try
            {
                if (!_infoBarQueue.Any() || ConnectionInfoBar.IsOpen)
                {
                    return;
                }

                var info = _infoBarQueue.Dequeue();
                ConnectionInfoBar.Title = info.Title;
                ConnectionInfoBar.Message = info.Message;
                InfoBarButton.Visibility = info.ShowActionButton
                    ? Visibility.Visible
                    : Visibility.Collapsed;
                ConnectionInfoBar.IsClosable = info.IsClosable;
                ConnectionInfoBar.IsOpen = true;
            }
            finally
            {
                _infoBarSemaphore.Release();
            }
        }

        protected override void HideConnectionInfoBar() => ConnectionInfoBar.IsOpen = false;

        protected override async Task ShowDialogAsync(string title, string message)
        {
            await ShowConnectionInfoBarAsync(title, message, false, true);
        }

        private async void ConnectionInfoBar_CloseButtonClick(
            Microsoft.UI.Xaml.Controls.InfoBar sender,
            object args
        )
        {
            await ProcessInfoBarQueueAsync();
        }

        protected override bool TryGoBack()
        {
            if (ContentFrame.Content is ArticleViewerPage avp && avp.CanGoBackInPage)
            {
                return avp.GoBackInPage();
            }
            if (ContentFrame.CanGoBack)
            {
                ContentFrame.GoBack();
                return true;
            }
            return false;
        }

        protected override void ClearWikiNavItems()
        {
            NavView.MenuItems.Clear();
        }

        protected override void AddWikiNavItem(WikiInstance wiki)
        {
            var navItem = new Microsoft.UI.Xaml.Controls.NavigationViewItem
            {
                Content = wiki.Name,
                Tag = $"home_{wiki.Id}",
            };

            var icon = new BitmapIcon
            {
                ShowAsMonochrome = false,
                Width = 20,
                Height = 20,
            };
            var iconUri = !string.IsNullOrEmpty(wiki.IconUrl)
                ? new Uri(wiki.IconUrl)
                : new Uri("ms-appx:///Assets/Square150x150Logo.png");
            var binding = new Binding
            {
                Source = wiki,
                Path = new PropertyPath("IconUrl"),
                Mode = BindingMode.OneWay,
            };
            BindingOperations.SetBinding(icon, BitmapIcon.UriSourceProperty, binding);
            navItem.Icon = icon;

            navItem.MenuItems.Add(
                new Microsoft.UI.Xaml.Controls.NavigationViewItem
                {
                    Content = "Home Page",
                    Tag = $"home_{wiki.Id}",
                    Icon = new SymbolIcon(Symbol.Home),
                }
            );
            navItem.MenuItems.Add(
                new Microsoft.UI.Xaml.Controls.NavigationViewItem
                {
                    Content = "Random Article",
                    Tag = $"random_{wiki.Id}",
                    Icon = new SymbolIcon(Symbol.Shuffle),
                }
            );

            NavView.MenuItems.Add(navItem);
        }

        protected override void AddStandardNavItems()
        {
            NavView.MenuItems.Add(new NavigationViewItemSeparator());
            NavView.MenuItems.Add(
                new Microsoft.UI.Xaml.Controls.NavigationViewItem
                {
                    Content = "Surprise Me",
                    Tag = "surprise_me",
                    Icon = new SymbolIcon(Symbol.Shuffle),
                }
            );
            NavView.MenuItems.Add(
                new Microsoft.UI.Xaml.Controls.NavigationViewItem
                {
                    Content = "Favourites",
                    Tag = "favourites",
                    Icon = new SymbolIcon(Symbol.Favorite),
                }
            );
            NavView.MenuItems.Add(
                new Microsoft.UI.Xaml.Controls.NavigationViewItem
                {
                    Content = "Accounts",
                    Tag = "accounts",
                    Icon = new SymbolIcon(Symbol.Contact),
                }
            );
        }

        private void SetupTitleBar()
        {
            CoreApplication.GetCurrentView().TitleBar.ExtendViewIntoTitleBar = true;
            Window.Current.SetTitleBar(AppTitleBarGrid);
            var titleBar = ApplicationView.GetForCurrentView().TitleBar;
            titleBar.ButtonBackgroundColor = Colors.Transparent;
            titleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
            CoreApplication.GetCurrentView().TitleBar.LayoutMetricsChanged += (s, e) =>
                UpdateTitleBarLayout();
        }

        private void UpdateTitleBarLayout()
        {
            LeftPaddingColumn.Width = new GridLength(
                CoreApplication.GetCurrentView().TitleBar.SystemOverlayLeftInset
            );
            RightPaddingColumn.Width = new GridLength(
                CoreApplication.GetCurrentView().TitleBar.SystemOverlayRightInset
            );
        }

        private void NavView_Loaded(object sender, RoutedEventArgs e)
        {
            if (ContentFrame.Content == null)
            {
                var firstWiki = WikiManager.GetWikis().FirstOrDefault();
                if (firstWiki != null)
                {
                    NavigateToPage(
                        GetArticleViewerPageType(),
                        new ArticleNavigationParameter
                        {
                            WikiId = firstWiki.Id,
                            PageTitle = "Main Page",
                        }
                    );
                }
            }
        }

        private void NavView_ItemInvoked(
            NavigationView sender,
            NavigationViewItemInvokedEventArgs args
        )
        {
            if (args.IsSettingsInvoked)
            {
                NavigateToPage(GetSettingsPageType(), null);
                return;
            }

            if (
                args.InvokedItemContainer is Microsoft.UI.Xaml.Controls.NavigationViewItem item
                && item.Tag is string tag
            )
            {
                if (tag.StartsWith("home_") || tag.StartsWith("random_"))
                {
                    var parts = tag.Split('_');
                    var action = parts[0];
                    if (Guid.TryParse(parts[1], out Guid wikiId))
                    {
                        var pageTitle = action == "random" ? "Special:Random" : "Main Page";
                        NavigateToPage(
                            GetArticleViewerPageType(),
                            new ArticleNavigationParameter
                            {
                                WikiId = wikiId,
                                PageTitle = pageTitle,
                            }
                        );
                    }
                }
                else if (tag == "surprise_me")
                {
                    var allWikis = WikiManager.GetWikis().ToList();
                    if (allWikis.Any())
                    {
                        var randomWiki = allWikis[_random.Next(allWikis.Count)];

                        NavigateToPage(
                            GetArticleViewerPageType(),
                            new ArticleNavigationParameter
                            {
                                WikiId = randomWiki.Id,
                                PageTitle = "Special:Random",
                            }
                        );
                    }
                }
                else if (tag == "favourites")
                {
                    NavigateToPage(GetFavouritesPageType(), null);
                }
                else if (tag == "accounts")
                {
                    ShowUserAccountFlyout(item);
                }
            }
        }

        private void NavView_BackRequested(
            NavigationView sender,
            NavigationViewBackRequestedEventArgs args
        ) => TryGoBack();

        private void ContentFrame_Navigated(
            object sender,
            Windows.UI.Xaml.Navigation.NavigationEventArgs e
        )
        {
            if (ContentFrame.Content is ArticleViewerPage avp)
            {
                avp.UpdateSystemBackButton();
            }
            else
            {
                UpdateBackButtonState();
                var navManager = Windows.UI.Core.SystemNavigationManager.GetForCurrentView();
                navManager.AppViewBackButtonVisibility = ContentFrame.CanGoBack
                    ? Windows.UI.Core.AppViewBackButtonVisibility.Visible
                    : Windows.UI.Core.AppViewBackButtonVisibility.Collapsed;
            }

            if (e.SourcePageType == GetSettingsPageType())
            {
                NavView.SelectedItem = NavView.SettingsItem;
            }
            else if (e.SourcePageType == GetFavouritesPageType())
            {
                NavView.SelectedItem = NavView
                    .MenuItems.OfType<Microsoft.UI.Xaml.Controls.NavigationViewItem>()
                    .FirstOrDefault(i => (i.Tag as string) == "favourites");
            }
            else
            {
                if (e.Parameter is string jsonParam && !string.IsNullOrEmpty(jsonParam))
                {
                    try
                    {
                        var navParam = JsonConvert.DeserializeObject<ArticleNavigationParameter>(
                            jsonParam
                        );
                        if (navParam != null)
                        {
                            string homeTag = $"home_{navParam.WikiId}";
                            NavView.SelectedItem = NavView
                                .MenuItems.OfType<Microsoft.UI.Xaml.Controls.NavigationViewItem>()
                                .FirstOrDefault(i => (i.Tag as string) == homeTag);
                        }
                    }
                    catch
                    {
                        NavView.SelectedItem = null;
                    }
                }
                else
                {
                    NavView.SelectedItem = null;
                }
            }
        }

        private void SearchBox_QuerySubmitted(
            AutoSuggestBox sender,
            AutoSuggestBoxQuerySubmittedEventArgs args
        ) => base.SearchBox_QuerySubmitted(sender, args);
    }
}
