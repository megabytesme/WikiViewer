using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
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
        private CancellationTokenSource _suggestionCts;
        public WebView2 PublicApiWebView => ApiFetchWebView;

        public MainPage()
        {
            this.InitializeComponent();
            ApplyBackdropOrAcrylic();
            SetupTitleBar();
            _ = InitializeApiWebViewAsync();
            AuthService.AuthenticationStateChanged += AuthService_AuthenticationStateChanged;
            this.Unloaded += (s, e) => { AuthService.AuthenticationStateChanged -= AuthService_AuthenticationStateChanged; };
            this.Loaded += MainPage_Loaded;
        }

        private async void MainPage_Loaded(object sender, RoutedEventArgs e)
        {
            while (!WebViewApiService.IsInitialized)
            {
                await Task.Delay(100);
            }
            await TryAutoLoginAsync();
        }

        private async Task TryAutoLoginAsync()
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
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[MainPage] Auto-login failed: {ex.Message}");
                    CredentialService.ClearCredentials();
                    AuthService_AuthenticationStateChanged(null, null);
                }
            }
            else
            {
                Debug.WriteLine("[MainPage] No saved credentials found.");
            }
        }

        private void AuthService_AuthenticationStateChanged(object sender, EventArgs e)
        {
            _ = Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
            {
                if (AuthService.IsLoggedIn)
                {
                    LoginNavItem.Content = AuthService.Username;
                    LoginNavItem.Icon = new FontIcon { FontFamily = new FontFamily("Segoe MDL2 Assets"), Glyph = "" };
                    LoginNavItem.Tag = "userpage";
                }
                else
                {
                    LoginNavItem.Content = "Login";
                    LoginNavItem.Icon = new FontIcon { FontFamily = new FontFamily("Segoe MDL2 Assets"), Glyph = "" };
                    LoginNavItem.Tag = "login";
                }
            });
        }

        private async Task InitializeApiWebViewAsync()
        {
            try
            {
                await ApiFetchWebView.EnsureCoreWebView2Async();
                WebViewApiService.Initialize(ApiFetchWebView);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to initialize API WebView: {ex.Message}");
            }
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
                ContentFrame.Navigate(typeof(ArticleViewerPage), "Main Page");
            }
        }

        private void NavView_ItemInvoked(muxc.NavigationView sender, muxc.NavigationViewItemInvokedEventArgs args)
        {
            Type targetPage = null;
            string pageParameter = null;

            if (args.IsSettingsInvoked)
            {
                targetPage = typeof(SettingsPage);
            }
            else if (args.InvokedItemContainer?.Tag is string tag)
            {
                switch (tag)
                {
                    case "home":
                        targetPage = typeof(ArticleViewerPage);
                        pageParameter = "Main Page";
                        break;
                    case "random":
                        targetPage = typeof(ArticleViewerPage);
                        pageParameter = "random";
                        break;
                    case "login":
                        targetPage = typeof(LoginPage);
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
                    ContentFrame.Navigate(targetPage, pageParameter, args.RecommendedNavigationTransitionInfo);
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
                string url = $"https://betawiki.net/api.php?action=opensearch&format=json&limit=10&search={Uri.EscapeDataString(query)}";

                await WebViewApiService.NavigateAsync(url);
                var webView = WebViewApiService.GetWebView();
                string json = null;

                var stopwatch = System.Diagnostics.Stopwatch.StartNew();
                while (stopwatch.Elapsed.TotalSeconds < 10)
                {
                    if (token.IsCancellationRequested) throw new TaskCanceledException();

                    string html = await webView.ExecuteScriptAsync("document.documentElement.outerHTML");
                    string fullHtml = JsonSerializer.Deserialize<string>(html ?? "null");

                    if (!string.IsNullOrEmpty(fullHtml))
                    {
                        if (fullHtml.Contains("Verifying you are human") || fullHtml.Contains("checking your browser"))
                        {
                            await Task.Delay(500, token);
                            continue;
                        }

                        var doc = new HtmlAgilityPack.HtmlDocument();
                        doc.LoadHtml(fullHtml);
                        string text = doc.DocumentNode.SelectSingleNode("//body/pre")?.InnerText ?? doc.DocumentNode.InnerText;

                        if (!string.IsNullOrEmpty(text) && text.Trim().StartsWith("["))
                        {
                            json = text.Trim();
                            break;
                        }
                    }
                    await Task.Delay(500, token);
                }

                if (json == null)
                {
                    throw new Exception("Failed to retrieve valid search suggestions.");
                }

                if (token.IsCancellationRequested) return;

                using (JsonDocument doc = JsonDocument.Parse(json))
                {
                    JsonElement root = doc.RootElement;
                    if (root.GetArrayLength() > 1)
                    {
                        var suggestions = new List<string>();
                        foreach (JsonElement suggestionElement in root[1].EnumerateArray())
                        {
                            suggestions.Add(suggestionElement.GetString());
                        }
                        sender.ItemsSource = suggestions;
                    }
                }
            }
            catch (TaskCanceledException)
            {
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Suggestion fetch failed: {ex.Message}");
                sender.ItemsSource = null;
            }
        }

        private void ContentFrame_Navigated(object sender, NavigationEventArgs e)
        {
            bool canGoBackInArticlePage = (ContentFrame.Content as ArticleViewerPage)?.CanGoBackInPage ?? false;
            NavView.IsBackEnabled = ContentFrame.CanGoBack || canGoBackInArticlePage;
            SystemNavigationManager.GetForCurrentView().AppViewBackButtonVisibility =
                NavView.IsBackEnabled ? AppViewBackButtonVisibility.Visible : AppViewBackButtonVisibility.Collapsed;

            if (e.SourcePageType == typeof(SettingsPage))
            {
                NavView.SelectedItem = NavView.SettingsItem;
            }
            else if (e.SourcePageType == typeof(ArticleViewerPage) && e.Parameter is string pageParameter)
            {
                if (pageParameter.Equals("Main Page", StringComparison.OrdinalIgnoreCase))
                {
                    NavView.SelectedItem = NavView.MenuItems
                        .OfType<muxc.NavigationViewItem>()
                        .FirstOrDefault(item => "home".Equals(item.Tag as string));
                }
                else if (pageParameter.Equals("random", StringComparison.OrdinalIgnoreCase))
                {
                    NavView.SelectedItem = NavView.MenuItems
                        .OfType<muxc.NavigationViewItem>()
                        .FirstOrDefault(item => "random".Equals(item.Tag as string));
                }
                else
                {
                    NavView.SelectedItem = null;
                }
            }
            else
            {
                NavView.SelectedItem = null;
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