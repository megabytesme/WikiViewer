using System;
using System.Collections.ObjectModel;
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
using WikiViewer.Shared.Uwp.Services;
using WikiViewer.Shared.Uwp.ViewModels;
using Windows.UI.Core;
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
        public bool IsVerificationFlow { get; set; } = false;
    }

    public abstract class MainPageBase : Page
    {
        private CancellationTokenSource _suggestionCts;
        private string _verificationUrl;
        private WikiInstance _wikiNeedingVerification;
        public Func<Task> _postVerificationAction = null;

        protected abstract Frame ContentFrame { get; }
        protected abstract AutoSuggestBox SearchBox { get; }
        protected abstract Grid AppTitleBarGrid { get; }
        protected abstract ColumnDefinition LeftPaddingColumn { get; }
        protected abstract ColumnDefinition RightPaddingColumn { get; }
        protected abstract void ShowConnectionInfoBar(
            string title,
            string message,
            bool showActionButton,
            bool isClosable
        );
        protected abstract void HideConnectionInfoBar();
        protected abstract Task ShowDialogAsync(string title, string message);
        protected abstract bool TryGoBack();
        protected abstract Panel GetWorkerHost();
        protected abstract Type GetArticleViewerPageType();
        protected abstract Type GetFavouritesPageType();
        protected abstract Type GetLoginPageType();
        protected abstract Type GetSettingsPageType();

        protected abstract void ClearWikiNavItems();
        protected abstract void AddWikiNavItem(WikiInstance wiki);
        protected abstract void AddStandardNavItems();

        public void SetPageTitle(string title)
        {
            SetPageTitle_Platform(title);
        }

        protected abstract void SetPageTitle_Platform(string title);

        private readonly ObservableCollection<SearchSuggestionViewModel> _suggestions =
            new ObservableCollection<SearchSuggestionViewModel>();

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
            App.SignalUIReady();
            AuthenticationService.AuthenticationStateChanged +=
                AuthService_AuthenticationStateChanged;
            WikiManager.WikisChanged += OnWikisChanged;

            SessionManager.AutoLoginFailed += OnAutoLoginFailed;

            _ = InitializeAppAsync();
        }

        private void MainPage_Unloaded(object sender, RoutedEventArgs e)
        {
            AuthenticationService.AuthenticationStateChanged -=
                AuthService_AuthenticationStateChanged;
            WikiManager.WikisChanged -= OnWikisChanged;

            SessionManager.AutoLoginFailed -= OnAutoLoginFailed;
        }

        private async void OnAutoLoginFailed(object sender, AutoLoginFailedEventArgs e)
        {
            await Dispatcher.RunAsync(
                CoreDispatcherPriority.Normal,
                async () =>
                {
                    if (e.Exception is NeedsUserVerificationException)
                    {
                        _postVerificationAction = () =>
                        {
                            Debug.WriteLine(
                                $"[MainPageBase] Retrying auto-login for '{e.Wiki.Name}' post-verification."
                            );
                            return SessionManager.PerformSingleLoginAsync(e.Wiki);
                        };

#if UWP_1809
                        ShowVerificationRequiredBar(e.Wiki);
#else
                        await ShowDialogAsync(
                            "Verification Required",
                            $"Access to '{e.Wiki.Name}' is restricted by a security check. For the best experience, please edit this wiki in Settings and switch its 'Connection Backend' to 'Proxy'."
                        );
#endif
                    }
                    else
                    {
                        ShowConnectionInfoBar(
                            "Login Failed",
                            $"Could not automatically sign in to '{e.Wiki.Name}'. Please check your connection or credentials.",
                            false,
                            true
                        );
                    }
                }
            );
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
            await SessionManager.InitializeAsync();
            _ = SessionManager.PerformAutoLoginAsync();

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
                    true,
                    false
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
                        PrimaryButtonText = "Close App",
                    };
                    await dialog.ShowAsync();
                    Application.Current.Exit();
                }
                else
                {
                    ShowConnectionInfoBar(
                        "Offline Mode",
                        $"Could not connect to {firstWiki.Host}. Only cached articles are available.",
                        false,
                        true
                    );
                }
            }
        }

        protected void InfoBarButton_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(_verificationUrl) && _wikiNeedingVerification != null)
            {
                NavigateToPage(
                    GetArticleViewerPageType(),
                    new ArticleNavigationParameter
                    {
                        WikiId = _wikiNeedingVerification.Id,
                        PageTitle = _verificationUrl,
                        IsVerificationFlow = true,
                    }
                );

                HideConnectionInfoBar();
                _verificationUrl = null;
                _wikiNeedingVerification = null;
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
            var mainPage = this.FindParent<MainPageBase>();
            if (mainPage != null)
            {
                mainPage.SetPageTitle(string.Empty);
            }
            SystemNavigationManager.GetForCurrentView().BackRequested -= System_BackRequested;
            App.RequestNavigation -= OnNavigationRequested;
        }

        protected async void SearchBox_TextChanged(
            AutoSuggestBox sender,
            AutoSuggestBoxTextChangedEventArgs args
        )
        {
            if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput)
                return;

            if (sender.ItemsSource == null)
            {
                sender.ItemsSource = _suggestions;
            }
            _suggestions.Clear();

            string query = sender.Text;
            if (string.IsNullOrWhiteSpace(query) || query.Length < 2)
            {
                return;
            }

            _suggestionCts?.Cancel();
            _suggestionCts = new CancellationTokenSource();
            var token = _suggestionCts.Token;

            try
            {
                var allWikis = WikiManager.GetWikis();

                var searchTasks = allWikis.Select(async wiki =>
                {
                    try
                    {
                        if (token.IsCancellationRequested)
                            return;

                        var worker = SessionManager.GetAnonymousWorkerForWiki(wiki);
                        string url =
                            $"{wiki.ApiEndpoint}?action=opensearch&format=json&limit=5&search={Uri.EscapeDataString(query)}";
                        string json = await worker.GetJsonFromApiAsync(url);

                        if (string.IsNullOrEmpty(json) || token.IsCancellationRequested)
                            return;

                        JArray root = JArray.Parse(json);
                        if (root.Count > 1 && root[1] is JArray suggestionsArray)
                        {
                            var titles =
                                suggestionsArray.ToObject<System.Collections.Generic.List<string>>();

                            await Dispatcher.RunAsync(
                                CoreDispatcherPriority.Normal,
                                () =>
                                {
                                    if (token.IsCancellationRequested)
                                        return;
                                    foreach (var title in titles)
                                    {
                                        _suggestions.Add(
                                            new SearchSuggestionViewModel
                                            {
                                                Title = title,
                                                WikiName = wiki.Name,
                                                IconUrl = wiki.IconUrl,
                                                WikiId = wiki.Id,
                                            }
                                        );
                                    }
                                }
                            );
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Suggestion fetch for {wiki.Name} failed: {ex.Message}");
                    }
                });

                await Task.WhenAll(searchTasks);
            }
            catch (TaskCanceledException) { }
            catch (Exception ex)
            {
                Debug.WriteLine($"Overall suggestion fetch failed: {ex.Message}");
            }
        }

        protected void SearchBox_QuerySubmitted(
            AutoSuggestBox sender,
            AutoSuggestBoxQuerySubmittedEventArgs args
        )
        {
            ArticleNavigationParameter navParam = null;

            if (args.ChosenSuggestion is SearchSuggestionViewModel selectedSuggestion)
            {
                navParam = new ArticleNavigationParameter
                {
                    WikiId = selectedSuggestion.WikiId,
                    PageTitle = selectedSuggestion.Title,
                };
            }
            else if (!string.IsNullOrWhiteSpace(args.QueryText))
            {
                var firstWiki = WikiManager.GetWikis().FirstOrDefault();
                if (firstWiki != null)
                {
                    navParam = new ArticleNavigationParameter
                    {
                        WikiId = firstWiki.Id,
                        PageTitle = args.QueryText,
                    };
                }
            }

            if (navParam != null)
            {
                NavigateToPage(GetArticleViewerPageType(), navParam);
            }
        }

        private void System_BackRequested(object sender, BackRequestedEventArgs e)
        {
            if (!e.Handled)
                e.Handled = TryGoBack();
        }

        public void ShowVerificationRequiredBar(WikiInstance wiki)
        {
            if (wiki == null)
                return;
            _wikiNeedingVerification = wiki;
            _verificationUrl = wiki.BaseUrl;

            ShowConnectionInfoBar(
                "Verification Required",
                $"Access to '{wiki.Name}' is blocked by a security check. Please click 'Action' to continue.",
                true,
                false
            );
        }
    }
}
