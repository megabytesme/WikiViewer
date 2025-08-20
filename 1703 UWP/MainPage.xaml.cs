using Newtonsoft.Json.Linq;
using Shared_Code;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.ApplicationModel.Core;
using Windows.UI;
using Windows.UI.Core;
using Windows.UI.ViewManagement;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Documents;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;

namespace _1703_UWP
{
    public sealed partial class MainPage : Page
    {
        private CancellationTokenSource _suggestionCts;
        public static IApiWorker ApiWorker { get; set; }
        private bool _isPreflightCheckComplete = false;
        private string _verificationUrl;

        public MainPage()
        {
            this.InitializeComponent();
            App.UIHost = this.WorkerWebViewHost;
            SetupTitleBar();
            AuthService.AuthenticationStateChanged += AuthService_AuthenticationStateChanged;
            this.Unloaded += (s, e) => { AuthService.AuthenticationStateChanged -= AuthService_AuthenticationStateChanged; };
            this.Loaded += MainPage_Loaded;
        }

        private void HamburgerButton_Click(object sender, RoutedEventArgs e)
        {
            NavSplitView.IsPaneOpen = !NavSplitView.IsPaneOpen;
        }

        private async void MainPage_Loaded(object sender, RoutedEventArgs e)
        {
            if (ApiWorker == null)
            {
                if (AppSettings.ConnectionBackend == ConnectionMethod.HttpClientProxy)
                {
                    ApiWorker = new HttpClientApiWorker();
                }
                else
                {
                    ApiWorker = new WebViewApiWorker();
                }
                await ApiWorker.InitializeAsync();
            }

            SearchBox.PlaceholderText = $"Search {AppSettings.Host}...";
            if (ContentFrame.Content == null)
            {
                var homeButton = (this.NavSplitView.Pane as FrameworkElement).FindName("home") as RadioButton;
                if (homeButton != null) homeButton.IsChecked = true;
            }

            await CheckAndShowFirstRunDisclaimerAsync();
            await PerformPreflightCheckAsync();
            await TryAutoLoginAsync();
        }

        private async Task PerformPreflightCheckAsync()
        {
            ConnectionInfoBar.Visibility = Visibility.Collapsed;
            try
            {
                Debug.WriteLine("[MainPage] Performing non-blocking pre-flight check...");
                await ArticleProcessingService.PageExistsAsync(AppSettings.MainPageName, ApiWorker);
                Debug.WriteLine("[MainPage] Pre-flight check successful. Connection is clear.");
            }
            catch (NeedsUserVerificationException ex)
            {
                Debug.WriteLine("[MainPage] Pre-flight check failed. Cloudflare verification required.");
                _verificationUrl = ex.Url;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MainPage] Pre-flight check failed with a general error: {ex.Message}");
                if (!AppSettings.HasShownDisclaimer)
                {
                    var dialog = new ContentDialog
                    {
                        Title = "Connection Required for First Launch",
                        Content = "This app needs to connect to the internet for its first use to set things up. Please check your connection and restart the app.",
                        CloseButtonText = "Close App"
                    };
                    await dialog.ShowAsync();
                    Application.Current.Exit();
                }
                else
                {
                    InfoBarTitle.Text = "Offline Mode";
                    InfoBarMessage.Text = $"Could not connect to {AppSettings.Host}. Only cached articles and favourites are available.";
                    InfoBarButton.Visibility = Visibility.Collapsed;
                    ConnectionInfoBar.Visibility = Visibility.Visible;
                }
            }
        }

        private void InfoBarButton_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(_verificationUrl))
            {
                ContentFrame.Navigate(typeof(ArticleViewerPage), _verificationUrl);
                ConnectionInfoBar.Visibility = Visibility.Collapsed;
            }
        }

        private async Task<bool> TryAutoLoginAsync()
        {
            Debug.WriteLine("[MainPage] Attempting to auto-login...");
            var savedCreds = CredentialService.LoadCredentials();
            if (savedCreds != null)
            {
                Debug.WriteLine($"[MainPage] Found saved credentials for user: {savedCreds.Username}.");
                LoginNavItem.Content = "Signing in...";
                try
                {
                    await AuthService.PerformLoginAsync(savedCreds.Username, savedCreds.Password);
                    Debug.WriteLine("[MainPage] Auto-login successful.");
                    return true;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[MainPage] Auto-login failed: {ex.Message}");
                    CredentialService.ClearCredentials();
                    AuthService_AuthenticationStateChanged(null, null);
                    return false;
                }
            }
            else
            {
                Debug.WriteLine("[MainPage] No saved credentials found.");
            }
            return false;
        }

        private async Task CheckAndShowFirstRunDisclaimerAsync()
        {
            if (!AppSettings.HasShownDisclaimer)
            {
                var dialog = new ContentDialog
                {
                    Title = "Welcome & Disclaimer",
                    Content = new ScrollViewer()
                    {
                        Content = new TextBlock()
                        {
                            Inlines =
                            {
                                new Run() { Text = "This is an unofficial, third-party client for browsing MediaWiki sites. This app was created by " },
                                new Hyperlink() { NavigateUri = new Uri("https://github.com/megabytesme"), Inlines = { new Run() { Text = "MegaBytesMe" } } },
                                new Run() { Text = " and is not affiliated with, endorsed, or sponsored by the operators of any specific wiki." },
                                new LineBreak(), new LineBreak(),
                                new Run() { Text = "All article data, content, and trademarks are the property of their respective owners and contributors." },
                                new LineBreak(), new LineBreak(),
                                new Run() { Text = "This disclaimer is available to view again in the settings." }
                            },
                            TextWrapping = TextWrapping.Wrap,
                        },
                    },
                    CloseButtonText = "I Understand",
                    DefaultButton = ContentDialogButton.Close,
                };
                await dialog.ShowAsync();
                AppSettings.HasShownDisclaimer = true;
            }
        }

        private void AuthService_AuthenticationStateChanged(object sender, EventArgs e)
        {
            _ = Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                if (AuthService.IsLoggedIn)
                {
                    LoginNavItem.Content = AuthService.Username;
                    LoginNavItem.Tag = "userpage";
                }
                else
                {
                    LoginNavItem.Content = "Login";
                    LoginNavItem.Tag = "login";
                }
            });
        }

        private void OnNavigationRequested(Type sourcePageType, object parameter)
        {
            _ = Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () => ContentFrame.Navigate(sourcePageType, parameter));
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
            Window.Current.SetTitleBar(AppTitleBar);
            var titleBar = ApplicationView.GetForCurrentView().TitleBar;
            titleBar.ButtonBackgroundColor = Colors.Transparent;
            titleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
            CoreApplication.GetCurrentView().TitleBar.LayoutMetricsChanged += (s, e) => UpdateTitleBarLayout();
        }

        private void UpdateTitleBarLayout()
        {
            LeftPaddingColumn.Width = new GridLength(CoreApplication.GetCurrentView().TitleBar.SystemOverlayLeftInset);
            RightPaddingColumn.Width = new GridLength(CoreApplication.GetCurrentView().TitleBar.SystemOverlayRightInset);
        }

        private void NavRadioButton_Checked(object sender, RoutedEventArgs e)
        {
            Type targetPage = null;
            string pageParameter = null;
            if (sender is RadioButton rb && rb.Tag is string tag)
            {
                switch (tag)
                {
                    case "home":
                        targetPage = typeof(ArticleViewerPage);
                        pageParameter = AppSettings.MainPageName;
                        break;
                    case "random":
                        targetPage = typeof(ArticleViewerPage);
                        pageParameter = "random";
                        break;
                    case "favourites":
                        targetPage = typeof(FavouritesPage);
                        break;
                    case "login":
                        targetPage = typeof(LoginPage);
                        break;
                    case "settings":
                        targetPage = typeof(SettingsPage);
                        break;
                    case "userpage":
                        if (AuthService.IsLoggedIn)
                        {
                            targetPage = typeof(ArticleViewerPage);
                            pageParameter = $"User:{AuthService.Username}";
                        }
                        break;
                }
            }
            if (targetPage != null)
            {
                if (ContentFrame.SourcePageType != targetPage || (ContentFrame.GetNavigationState() as string) != pageParameter)
                {
                    ContentFrame.Navigate(targetPage, pageParameter);
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

        private async void SearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
        {
            if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput) return;
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
                string url = $"{AppSettings.ApiEndpoint}?action=opensearch&format=json&limit=10&search={Uri.EscapeDataString(query)}";
                string json = await ApiWorker.GetJsonFromApiAsync(url);
                if (token.IsCancellationRequested) return;
                if (string.IsNullOrEmpty(json))
                {
                    sender.ItemsSource = null;
                    return;
                }
                JArray root = JArray.Parse(json);
                if (root.Count > 1 && root[1] is JArray suggestionsArray)
                {
                    var suggestions = suggestionsArray.ToObject<List<string>>();
                    sender.ItemsSource = suggestions;
                }
                else
                {
                    sender.ItemsSource = null;
                }
            }
            catch (TaskCanceledException) { }
            catch (NeedsUserVerificationException) { sender.ItemsSource = null; }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Suggestion fetch failed: {ex.Message}");
                sender.ItemsSource = null;
            }
        }

        private void ContentFrame_Navigated(object sender, NavigationEventArgs e)
        {
            bool canGoBackInArticlePage = (ContentFrame.Content as ArticleViewerPage)?.CanGoBackInPage ?? false;
            bool canGoBack = ContentFrame.CanGoBack || canGoBackInArticlePage;
            SystemNavigationManager.GetForCurrentView().AppViewBackButtonVisibility = canGoBack ? AppViewBackButtonVisibility.Visible : AppViewBackButtonVisibility.Collapsed;
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