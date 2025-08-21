using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using WikiViewer.Core;
using WikiViewer.Core.Enums;
using WikiViewer.Core.Interfaces;
using WikiViewer.Core.Models;
using WikiViewer.Core.Services;
using WikiViewer.Shared.Uwp.Services;
using Windows.ApplicationModel.Core;
using Windows.UI;
using Windows.UI.Core;
using Windows.UI.ViewManagement;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Documents;
using Windows.UI.Xaml.Navigation;

namespace WikiViewer.Shared.Uwp.Pages
{
    public abstract class MainPageBase : Page
    {
        private CancellationTokenSource _suggestionCts;
        public static IApiWorker ApiWorker { get; set; }
        private string _verificationUrl;

        protected abstract Frame ContentFrame { get; }
        protected abstract AutoSuggestBox SearchBox { get; }
        protected abstract Grid AppTitleBarGrid { get; }
        protected abstract ColumnDefinition LeftPaddingColumn { get; }
        protected abstract ColumnDefinition RightPaddingColumn { get; }
        protected abstract void UpdateUserUI(bool isLoggedIn, string username);
        protected abstract void ShowConnectionInfoBar(
            string title,
            string message,
            bool showActionButton
        );
        protected abstract void HideConnectionInfoBar();
        protected abstract void NavigateToPage(Type page, string parameter);
        protected abstract bool TryGoBack();
        protected abstract Panel GetWorkerHost();
        protected abstract Type GetArticleViewerPageType();
        protected abstract Type GetFavouritesPageType();
        protected abstract Type GetLoginPageType();
        protected abstract Type GetSettingsPageType();

        public MainPageBase()
        {
            this.Loaded += MainPage_Loaded;
            this.Unloaded += MainPage_Unloaded;
        }

        private void MainPage_Loaded(object sender, RoutedEventArgs e)
        {
            Debug.WriteLine(
                $"[Shared.MainPageBase.Loaded] Page Loaded event fired. ContentFrame is {(ContentFrame == null ? "NULL!" : "VALID")}"
            );

            App.UIHost = GetWorkerHost();
            SetupTitleBar();
            AuthService.AuthenticationStateChanged += AuthService_AuthenticationStateChanged;
            _ = InitializeAppAsync();
        }

        private void MainPage_Unloaded(object sender, RoutedEventArgs e)
        {
            AuthService.AuthenticationStateChanged -= AuthService_AuthenticationStateChanged;
        }

        private async Task InitializeAppAsync()
        {
            if (ApiWorker == null)
            {
                ApiWorker = App.ApiWorkerFactory.CreateApiWorker();
                await ApiWorker.InitializeAsync();
            }

            SearchBox.PlaceholderText = $"Search {AppSettings.Host}...";
            if (ContentFrame.Content == null)
            {
                NavigateToPage(GetArticleViewerPageType(), AppSettings.MainPageName);
            }

            await CheckAndShowFirstRunDisclaimerAsync();
            await PerformPreflightCheckAsync();
            await TryAutoLoginAsync();
        }

        private async Task PerformPreflightCheckAsync()
        {
            HideConnectionInfoBar();
            try
            {
                await ArticleProcessingService.PageExistsAsync(AppSettings.MainPageName, ApiWorker);
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
                        $"Could not connect to {AppSettings.Host}. Only cached articles are available.",
                        false
                    );
                }
            }
        }

        protected void InfoBarButton_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(_verificationUrl))
            {
                ContentFrame.Navigate(GetArticleViewerPageType(), _verificationUrl);
                HideConnectionInfoBar();
            }
        }

        private async Task TryAutoLoginAsync()
        {
            var savedCreds = CredentialService.LoadCredentials();
            if (savedCreds != null)
            {
                UpdateUserUI(false, "Signing in...");
                try
                {
                    await AuthService.PerformLoginAsync(savedCreds.Username, savedCreds.Password);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[MainPage] Auto-login failed: {ex.Message}");
                    CredentialService.ClearCredentials();
                    AuthService_AuthenticationStateChanged(null, null);
                }
            }
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
                                        "This is an unofficial, third-party client for browsing MediaWiki sites. This app was created by ",
                                },
                                new Hyperlink()
                                {
                                    NavigateUri = new Uri("https://github.com/megabytesme"),
                                    Inlines = { new Run() { Text = "MegaBytesMe" } },
                                },
                                new Run()
                                {
                                    Text =
                                        " and is not affiliated with, endorsed, or sponsored by the operators of any specific wiki.",
                                },
                                new LineBreak(),
                                new LineBreak(),
                                new Run()
                                {
                                    Text =
                                        "All article data, content, and trademarks are the property of their respective owners and contributors.",
                                },
                                new LineBreak(),
                                new LineBreak(),
                                new Run()
                                {
                                    Text =
                                        "This disclaimer is available to view again in the settings.",
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
                CoreDispatcherPriority.Normal,
                () => UpdateUserUI(AuthService.IsLoggedIn, AuthService.Username)
            );
        }

        private void OnNavigationRequested(Type sourcePageType, object parameter)
        {
            _ = Dispatcher.RunAsync(
                CoreDispatcherPriority.Normal,
                () => ContentFrame.Navigate(sourcePageType, parameter)
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

        protected void SearchBox_QuerySubmitted(
            AutoSuggestBox sender,
            AutoSuggestBoxQuerySubmittedEventArgs args
        )
        {
            Debug.WriteLine(
                $"[Shared.MainPageBase.SearchBox_QuerySubmitted] Search submitted. Query: '{args.QueryText}'. ContentFrame is {(ContentFrame == null ? "NULL!" : "VALID")}"
            );

            if (!string.IsNullOrEmpty(args.QueryText))
                ContentFrame.Navigate(GetArticleViewerPageType(), args.QueryText);
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
                string url =
                    $"{AppSettings.ApiEndpoint}?action=opensearch&format=json&limit=10&search={Uri.EscapeDataString(query)}";
                string json = await ApiWorker.GetJsonFromApiAsync(url);
                if (token.IsCancellationRequested)
                    return;
                if (string.IsNullOrEmpty(json))
                {
                    sender.ItemsSource = null;
                    return;
                }
                JArray root = JArray.Parse(json);
                if (root.Count > 1 && root[1] is JArray suggestionsArray)
                    sender.ItemsSource = suggestionsArray.ToObject<List<string>>();
                else
                    sender.ItemsSource = null;
            }
            catch (TaskCanceledException) { }
            catch (NeedsUserVerificationException)
            {
                sender.ItemsSource = null;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Suggestion fetch failed: {ex.Message}");
                sender.ItemsSource = null;
            }
        }

        private void System_BackRequested(object sender, BackRequestedEventArgs e)
        {
            if (!e.Handled)
                e.Handled = TryGoBack();
        }
    }
}
