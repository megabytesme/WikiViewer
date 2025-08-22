using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using WikiViewer.Core;
using WikiViewer.Core.Models;
using WikiViewer.Core.Services;
using WikiViewer.Shared.Uwp.Controls;
using Windows.ApplicationModel.Core;
using Windows.UI;
using Windows.UI.Core;
using Windows.UI.ViewManagement;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media.Animation;
using Windows.UI.Xaml.Navigation;

namespace WikiViewer.Shared.Uwp.Pages
{
    public class ArticleNavigationParameter
    {
        public Guid WikiId { get; set; }
        public string PageTitle { get; set; }
    }

    public abstract class MainPageBase : Page
    {
        private CancellationTokenSource _suggestionCts;
        private string _verificationUrl;

        protected abstract Frame ContentFrame { get; }
        protected abstract AutoSuggestBox SearchBox { get; }
        protected abstract Grid AppTitleBarGrid { get; }
        protected abstract ColumnDefinition LeftPaddingColumn { get; }
        protected abstract ColumnDefinition RightPaddingColumn { get; }
        protected abstract void ShowConnectionInfoBar(
            string title,
            string message,
            bool showActionButton
        );
        protected abstract void HideConnectionInfoBar();
        protected abstract bool TryGoBack();
        protected abstract Panel GetWorkerHost();
        protected abstract Type GetArticleViewerPageType();
        protected abstract Type GetFavouritesPageType();
        protected abstract Type GetLoginPageType();
        protected abstract Type GetSettingsPageType();

        protected abstract void ClearWikiNavItems();
        protected abstract void AddWikiNavItem(WikiInstance wiki);
        protected abstract void AddStandardNavItems();

        protected void NavigateToPage(
            Type page,
            object parameter,
            NavigationTransitionInfo transitionInfo = null
        )
        {
            object finalParameter = parameter;
            if (parameter is ArticleNavigationParameter navParam)
            {
                finalParameter = JsonConvert.SerializeObject(navParam);
            }

            if (ContentFrame.SourcePageType != page)
            {
                ContentFrame.Navigate(
                    page,
                    finalParameter,
                    transitionInfo ?? new EntranceNavigationTransitionInfo()
                );
            }
            else
            {
                ContentFrame.Navigate(
                    page,
                    finalParameter,
                    transitionInfo ?? new EntranceNavigationTransitionInfo()
                );
            }
        }

        public MainPageBase()
        {
            this.Loaded += MainPage_Loaded;
            this.Unloaded += MainPage_Unloaded;
        }

        private void MainPage_Loaded(object sender, RoutedEventArgs e)
        {
            App.UIHost = GetWorkerHost();
            SetupTitleBar();
            AuthenticationService.AuthenticationStateChanged +=
                AuthService_AuthenticationStateChanged;
            WikiManager.WikisChanged += OnWikisChanged;
            _ = InitializeAppAsync();
        }

        private void MainPage_Unloaded(object sender, RoutedEventArgs e)
        {
            AuthenticationService.AuthenticationStateChanged -=
                AuthService_AuthenticationStateChanged;
            WikiManager.WikisChanged -= OnWikisChanged;
        }

        private void OnWikisChanged(object sender, EventArgs e)
        {
            _ = Dispatcher.RunAsync(
                Windows.UI.Core.CoreDispatcherPriority.Normal,
                PopulateWikiNavItems
            );
        }

        private void PopulateWikiNavItems()
        {
            ClearWikiNavItems();

            var wikis = WikiManager.GetWikis();
            foreach (var wiki in wikis)
            {
                _ = Task.Run(async () =>
                {
                    using (var worker = App.ApiWorkerFactory.CreateApiWorker(wiki))
                    {
                        await FaviconService.FetchAndCacheFaviconUrlAsync(wiki, worker);
                    }
                });
                AddWikiNavItem(wiki);
            }

            AddStandardNavItems();
        }

        private async Task InitializeAppAsync()
        {
            PopulateWikiNavItems();
            SearchBox.PlaceholderText = "Search all wikis...";

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
            await PerformPreflightCheckAsync();
        }

        private async Task PerformPreflightCheckAsync()
        {
            HideConnectionInfoBar();
            var firstWiki = WikiManager.GetWikis().FirstOrDefault();
            if (firstWiki == null)
                return;

            try
            {
                var worker = SessionManager.GetAnonymousWorkerForWiki(firstWiki);
                await ArticleProcessingService.PageExistsAsync("Main Page", worker, firstWiki);
            }
            catch (NeedsUserVerificationException ex)
            {
                _verificationUrl = ex.Url;
                ShowConnectionInfoBar(
                    "Verification Required",
                    "A security check is required to continue.",
                    true
                );
            }
            catch (Exception)
            {
                if (!AppSettings.HasShownDisclaimer)
                {
                    var dialog = new ContentDialog
                    {
                        Title = "Connection Required",
                        Content = "An internet connection is required for the first launch.",
                        CloseButtonText = "Close App",
                    };
                    await dialog.ShowAsync();
                    Application.Current.Exit();
                }
                else
                {
                    ShowConnectionInfoBar(
                        "Offline Mode",
                        $"Could not connect to {firstWiki.Host}. Only cached articles are available.",
                        false
                    );
                }
            }
        }

        protected void InfoBarButton_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(_verificationUrl))
            {
                var firstWiki = WikiManager.GetWikis().FirstOrDefault();
                if (firstWiki != null)
                {
                    ContentFrame.Navigate(
                        GetArticleViewerPageType(),
                        new ArticleNavigationParameter
                        {
                            WikiId = firstWiki.Id,
                            PageTitle = _verificationUrl,
                        }
                    );
                }
                HideConnectionInfoBar();
            }
        }

        private void AuthService_AuthenticationStateChanged(
            object sender,
            AuthenticationStateChangedEventArgs e
        ) { }

        protected void ShowUserAccountFlyout(FrameworkElement target)
        {
            var flyout = new Flyout();
            var content = new AccountStatusFlyout();

            content.RequestSignIn += (wikiId) =>
            {
                flyout.Hide();
                NavigateToPage(GetLoginPageType(), wikiId);
            };

            content.RequestSignOut += (accountId) =>
            {
                flyout.Hide();
                var account = AccountManager.GetAccountById(accountId);
                if (account != null && account.IsLoggedIn)
                {
                    var wiki = WikiManager.GetWikiById(account.WikiInstanceId);
                    var authService = new AuthenticationService(
                        account,
                        wiki,
                        App.ApiWorkerFactory
                    );
                    authService.Logout();
                }
            };

            flyout.Content = content;
            flyout.ShowAt(target);
        }

        private void OnNavigationRequested(Type sourcePageType, object parameter)
        {
            _ = Dispatcher.RunAsync(
                CoreDispatcherPriority.Normal,
                () => NavigateToPage(sourcePageType, parameter)
            );
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            SystemNavigationManager.GetForCurrentView().BackRequested += System_BackRequested;
            App.RequestNavigation += OnNavigationRequested;
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);
            SystemNavigationManager.GetForCurrentView().BackRequested -= System_BackRequested;
            App.RequestNavigation -= OnNavigationRequested;
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

        protected async void SearchBox_TextChanged(
            AutoSuggestBox sender,
            AutoSuggestBoxTextChangedEventArgs args
        )
        {
            if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput)
                return;
            string query = sender.Text;
            if (string.IsNullOrWhiteSpace(query) || query.Length < 2)
            {
                sender.ItemsSource = null;
                return;
            }
            _suggestionCts?.Cancel();
            _suggestionCts = new CancellationTokenSource();
            var token = _suggestionCts.Token;
            try
            {
                await Task.Delay(300, token);
                var allWikis = WikiManager.GetWikis();
                var suggestionTasks = allWikis.Select(async wiki =>
                {
                    try
                    {
                        var worker = SessionManager.GetAnonymousWorkerForWiki(wiki);
                        string url =
                            $"{wiki.ApiEndpoint}?action=opensearch&format=json&limit=5&search={Uri.EscapeDataString(query)}";
                        string json = await worker.GetJsonFromApiAsync(url);
                        if (string.IsNullOrEmpty(json))
                            return new System.Collections.Generic.List<string>();
                        JArray root = JArray.Parse(json);
                        return root.Count > 1 && root[1] is JArray suggestionsArray
                            ? suggestionsArray.ToObject<System.Collections.Generic.List<string>>()
                            : new System.Collections.Generic.List<string>();
                    }
                    catch
                    {
                        return new System.Collections.Generic.List<string>();
                    }
                });
                var results = await Task.WhenAll(suggestionTasks);
                if (token.IsCancellationRequested)
                    return;
                sender.ItemsSource = results.SelectMany(r => r).Distinct().ToList();
            }
            catch (TaskCanceledException) { }
            catch (Exception ex)
            {
                Debug.WriteLine($"Suggestion fetch failed: {ex.Message}");
                sender.ItemsSource = null;
            }
        }

        protected void SearchBox_QuerySubmitted(
            AutoSuggestBox sender,
            AutoSuggestBoxQuerySubmittedEventArgs args
        )
        {
            var wikis = WikiManager.GetWikis();
            if (wikis.Any())
            {
                var firstWiki = wikis.First();
                NavigateToPage(
                    GetArticleViewerPageType(),
                    new ArticleNavigationParameter
                    {
                        WikiId = firstWiki.Id,
                        PageTitle = args.QueryText,
                    }
                );
            }
        }

        private void System_BackRequested(object sender, BackRequestedEventArgs e)
        {
            if (!e.Handled)
                e.Handled = TryGoBack();
        }
    }
}
