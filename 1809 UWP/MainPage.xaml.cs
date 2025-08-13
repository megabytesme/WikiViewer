using Microsoft.UI.Xaml.Controls;
using Newtonsoft.Json.Linq;
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
using muxc = Microsoft.UI.Xaml.Controls;

namespace _1809_UWP
{
    public sealed partial class MainPage : Page
    {
        private CancellationTokenSource _suggestionCts;
        public static WebView2 ApiWorker { get; private set; }
        private bool _isPreflightCheckComplete = false;
        private string _verificationUrl;

        public MainPage()
        {
            this.InitializeComponent();
            App.UIHost = this.WorkerWebViewHost;
            ApplyBackdropOrAcrylic();
            SetupTitleBar();
            AuthService.AuthenticationStateChanged += AuthService_AuthenticationStateChanged;
            this.Unloaded += (s, e) => { AuthService.AuthenticationStateChanged -= AuthService_AuthenticationStateChanged; };
            this.Loaded += MainPage_Loaded;
        }

        private async void MainPage_Loaded(object sender, RoutedEventArgs e)
        {
            if (ApiWorker == null)
            {
                ApiWorker = new WebView2();
                WorkerWebViewHost.Children.Add(ApiWorker);
                await ApiWorker.EnsureCoreWebView2Async();
            }

            if (ContentFrame.Content == null)
            {
                NavView.SelectedItem = NavView.MenuItems.OfType<muxc.NavigationViewItem>().FirstOrDefault();
                ContentFrame.Navigate(typeof(ArticleViewerPage), "Main Page");
            }

            await CheckAndShowFirstRunDisclaimerAsync();
            await PerformPreflightCheckAsync();
            await TryAutoLoginAsync();
        }

        private async Task PerformPreflightCheckAsync()
        {
            ConnectionInfoBar.IsOpen = false;

            try
            {
                Debug.WriteLine("[MainPage] Performing non-blocking pre-flight check...");
                await ArticleProcessingService.PageExistsAsync("Main Page", ApiWorker);
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
                        Content = "WikiViewer needs to connect to the internet for its first use to set things up. Please check your connection and restart the app.",
                        CloseButtonText = "Close App"
                    };
                    await dialog.ShowAsync();
                    Application.Current.Exit();
                }
                else
                {
                    ConnectionInfoBar.Title = "Offline Mode";
                    ConnectionInfoBar.Message = "Could not connect to BetaWiki. Only cached articles and favourites are available.";
                    InfoBarButton.Visibility = Visibility.Collapsed;
                    ConnectionInfoBar.Severity = muxc.InfoBarSeverity.Error;
                    ConnectionInfoBar.IsOpen = true;
                }
            }
        }

        private void InfoBarButton_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(_verificationUrl))
            {
                ContentFrame.Navigate(typeof(ArticleViewerPage), _verificationUrl);
                ConnectionInfoBar.IsOpen = false;
            }
        }

        private async Task<bool> TryAutoLoginAsync()
        {
            Debug.WriteLine("[MainPage] Attempting to auto-login...");
            var savedCreds = CredentialService.LoadCredentials();
            if (savedCreds != null)
            {
                Debug.WriteLine(
                    $"[MainPage] Found saved credentials for user: {savedCreds.Username}."
                );
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
                                new Run()
                                {
                                    Text =
                                        "This is an unofficial, third-party client for browsing BetaWiki. This app was created by ",
                                },
                                new Hyperlink()
                                {
                                    NavigateUri = new Uri("https://github.com/megabytesme"),
                                    Inlines = { new Run() { Text = "MegaBytesMe" } },
                                },
                                new Run()
                                {
                                    Text =
                                        " and is not affiliated with, endorsed, or sponsored by the official BetaWiki team.",
                                },
                                new LineBreak(),
                                new LineBreak(),
                                new Run()
                                {
                                    Text =
                                        "All article data, content, and trademarks are the property of BetaWiki and its respective contributors.",
                                },
                                new LineBreak(),
                                new LineBreak(),
                                new Run()
                                {
                                    Text =
                                        "This disclaimer is available to view again in the settings.",
                                },
                                new LineBreak(),
                                new LineBreak(),
                                new Run() { Text = "You can view the official BetaWiki here: " },
                                new Hyperlink()
                                {
                                    NavigateUri = new Uri("https://betawiki.net/"),
                                    Inlines = { new Run() { Text = "BetaWiki" } },
                                },
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
            _ = Dispatcher.RunAsync(
                Windows.UI.Core.CoreDispatcherPriority.Normal,
                () =>
                {
                    if (AuthService.IsLoggedIn)
                    {
                        LoginNavItem.Content = AuthService.Username;
                        LoginNavItem.Icon = new FontIcon
                        {
                            FontFamily = new FontFamily("Segoe MDL2 Assets"),
                            Glyph = "",
                        };
                        LoginNavItem.Tag = "userpage";
                    }
                    else
                    {
                        LoginNavItem.Content = "Login";
                        LoginNavItem.Icon = new FontIcon
                        {
                            FontFamily = new FontFamily("Segoe MDL2 Assets"),
                            Glyph = "",
                        };
                        LoginNavItem.Tag = "login";
                    }
                }
            );
        }

        private void OnNavigationRequested(Type sourcePageType, object parameter)
        {
            _ = Dispatcher.RunAsync(
                CoreDispatcherPriority.Normal,
                () =>
                {
                    ContentFrame.Navigate(sourcePageType, parameter);
                }
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

        private void ApplyBackdropOrAcrylic()
        {
            if (
                Windows.Foundation.Metadata.ApiInformation.IsApiContractPresent(
                    "Windows.Foundation.UniversalApiContract",
                    12
                )
            )
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
                    FallbackColor = Color.FromArgb(255, 40, 40, 40),
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
            CoreApplication.GetCurrentView().TitleBar.LayoutMetricsChanged += (s, e) =>
                UpdateTitleBarLayout();
        }

        private void UpdateTitleBarLayout()
        {
            if (CoreApplication.GetCurrentView().TitleBar.ExtendViewIntoTitleBar)
            {
                LeftPaddingColumn.Width = new GridLength(
                    CoreApplication.GetCurrentView().TitleBar.SystemOverlayLeftInset
                );
                RightPaddingColumn.Width = new GridLength(
                    CoreApplication.GetCurrentView().TitleBar.SystemOverlayRightInset
                );
            }
            else
            {
                LeftPaddingColumn.Width = new GridLength(0);
                RightPaddingColumn.Width = new GridLength(0);
            }
        }

        private void NavView_Loaded(object sender, RoutedEventArgs e)
        {
            if (_isPreflightCheckComplete && ContentFrame.Content == null)
            {
                NavView.SelectedItem = NavView
                    .MenuItems.OfType<muxc.NavigationViewItem>()
                    .FirstOrDefault();
                ContentFrame.Navigate(typeof(ArticleViewerPage), "Main Page");
            }
        }

        private void NavView_ItemInvoked(
            muxc.NavigationView sender,
            muxc.NavigationViewItemInvokedEventArgs args
        )
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
                    case "favourites":
                        targetPage = typeof(FavouritesPage);
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
                if (
                    ContentFrame.SourcePageType != targetPage
                    || (ContentFrame.GetNavigationState() as string) != pageParameter
                )
                {
                    ContentFrame.Navigate(
                        targetPage,
                        pageParameter,
                        args.RecommendedNavigationTransitionInfo
                    );
                }
            }
        }

        private void SearchBox_QuerySubmitted(
            AutoSuggestBox sender,
            AutoSuggestBoxQuerySubmittedEventArgs args
        )
        {
            if (!string.IsNullOrEmpty(args.QueryText))
            {
                ContentFrame.Navigate(typeof(ArticleViewerPage), args.QueryText);
            }
        }

        private async void SearchBox_TextChanged(
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
                string url =
                    $"https://betawiki.net/api.php?action=opensearch&format=json&limit=10&search={Uri.EscapeDataString(query)}";

                string json = await ApiRequestService.GetJsonFromApiAsync(url, ApiWorker);

                if (token.IsCancellationRequested)
                    return;

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
            catch (NeedsUserVerificationException)
            {
                sender.ItemsSource = null;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Suggestion fetch failed: {ex.Message}");
                sender.ItemsSource = null;
            }
        }

        private void ContentFrame_Navigated(object sender, NavigationEventArgs e)
        {
            bool canGoBackInArticlePage =
                (ContentFrame.Content as ArticleViewerPage)?.CanGoBackInPage ?? false;
            NavView.IsBackEnabled = ContentFrame.CanGoBack || canGoBackInArticlePage;
            SystemNavigationManager.GetForCurrentView().AppViewBackButtonVisibility =
                NavView.IsBackEnabled
                    ? AppViewBackButtonVisibility.Visible
                    : AppViewBackButtonVisibility.Collapsed;

            if (e.SourcePageType == typeof(SettingsPage))
            {
                NavView.SelectedItem = NavView.SettingsItem;
                return;
            }

            string targetTag = null;
            if (e.SourcePageType == typeof(FavouritesPage))
            {
                targetTag = "favourites";
            }
            else if (e.SourcePageType == typeof(LoginPage))
            {
                targetTag = "login";
            }
            else if (
                e.SourcePageType == typeof(ArticleViewerPage)
                && e.Parameter is string pageParameter
            )
            {
                if (pageParameter.Equals("Main Page", StringComparison.OrdinalIgnoreCase))
                {
                    targetTag = "home";
                }
                else if (pageParameter.Equals("random", StringComparison.OrdinalIgnoreCase))
                {
                    targetTag = "random";
                }
                else if (
                    pageParameter.Equals(
                        $"User:{AuthService.Username}",
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                {
                    targetTag = "userpage";
                }
            }

            NavView.SelectedItem = NavView
                .MenuItems.OfType<muxc.NavigationViewItem>()
                .FirstOrDefault(item => string.Equals(item.Tag as string, targetTag));
        }

        private void NavView_BackRequested(
            muxc.NavigationView sender,
            muxc.NavigationViewBackRequestedEventArgs args
        )
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
