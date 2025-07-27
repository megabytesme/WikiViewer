using Microsoft.Web.WebView2.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
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
        private TaskCompletionSource<string> _apiResultTcs;

        public MainPage()
        {
            this.InitializeComponent();
            ApplyBackdropOrAcrylic();
            SetupTitleBar();

            _ = InitializeApiWebViewAsync();
        }

        private async Task InitializeApiWebViewAsync()
        {
            try
            {
                await ApiFetchWebView.EnsureCoreWebView2Async();
                ApiFetchWebView.CoreWebView2.NavigationCompleted += ApiFetchWebView_NavigationCompleted;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to initialize API WebView: {ex.Message}");
            }
        }

        private async void ApiFetchWebView_NavigationCompleted(CoreWebView2 sender, CoreWebView2NavigationCompletedEventArgs args)
        {
            if (_apiResultTcs == null || _apiResultTcs.Task.IsCompleted)
            {
                return;
            }

            if (!args.IsSuccess)
            {
                _apiResultTcs.TrySetException(new HttpRequestException($"API navigation failed with status: {args.WebErrorStatus}"));
                return;
            }

            try
            {
                string script = "document.body.innerText;";
                string scriptResult = await sender.ExecuteScriptAsync(script);
                string resultJson = JsonSerializer.Deserialize<string>(scriptResult);

                _apiResultTcs.TrySetResult(resultJson);
            }
            catch (Exception ex)
            {
                _apiResultTcs.TrySetException(ex);
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
            if (args.IsSettingsInvoked) return;

            if (args.InvokedItemContainer?.Tag is string tag)
            {
                if (tag == "home")
                {
                    ContentFrame.Navigate(typeof(ArticleViewerPage), "Main Page", args.RecommendedNavigationTransitionInfo);
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

                _apiResultTcs = new TaskCompletionSource<string>();
                ApiFetchWebView.CoreWebView2.Navigate(url);
                string json = await _apiResultTcs.Task;
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
                System.Diagnostics.Debug.WriteLine($"Suggestion fetch via WebView failed: {ex.Message}");
                sender.ItemsSource = null;
            }
        }

        private void ContentFrame_Navigated(object sender, NavigationEventArgs e)
        {
            bool canGoBackInArticlePage = (ContentFrame.Content as ArticleViewerPage)?.CanGoBackInPage ?? false;
            NavView.IsBackEnabled = ContentFrame.CanGoBack || canGoBackInArticlePage;
            SystemNavigationManager.GetForCurrentView().AppViewBackButtonVisibility =
                NavView.IsBackEnabled ? AppViewBackButtonVisibility.Visible : AppViewBackButtonVisibility.Collapsed;

            if (e.SourcePageType == typeof(ArticleViewerPage) && e.Parameter is string pageParameter)
            {
                if (pageParameter.Equals("Main Page", StringComparison.OrdinalIgnoreCase))
                {
                    NavView.SelectedItem = NavView.MenuItems
                        .OfType<muxc.NavigationViewItem>()
                        .FirstOrDefault(item => "home".Equals(item.Tag as string));
                    return;
                } else
                {
                    NavView.SelectedItem = NavView.MenuItems
                        .OfType<muxc.NavigationViewItem>()
                        .FirstOrDefault(item => "random".Equals(item.Tag as string));
                    return;
                }
            }

            NavView.SelectedItem = null;
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