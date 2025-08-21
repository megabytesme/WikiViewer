using System;
using System.Threading.Tasks;
using WikiViewer.Core;
using WikiViewer.Core.Enums;
using WikiViewer.Core.Interfaces;
using WikiViewer.Core.Models;
using WikiViewer.Core.Services;
using WikiViewer.Shared.Uwp.Managers;
using Windows.UI;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Documents;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;

namespace WikiViewer.Shared.Uwp.Pages
{
    public abstract class SettingsPageBase : Page
    {
        private const int BaseRamMb = 400;
        private const int EstimatedRamPerTaskMb = 122;
        private const int highPerformanceMultiplier = 2;
        private bool _isConnectionToggleEvent = false;

        protected abstract StackPanel LoggedInStatePanel { get; }
        protected abstract StackPanel LoggedOutStatePanel { get; }
        protected abstract TextBlock UsernameTextBlock { get; }
        protected abstract TextBlock LoggedOutStateTextBlock { get; }
        protected abstract HyperlinkButton SignInHyperlink { get; }
        protected abstract TextBox WikiUrlTextBox { get; }
        protected abstract TextBox ScriptPathTextBox { get; }
        protected abstract TextBox ArticlePathTextBox { get; }
        protected abstract TextBlock DetectionStatusTextBlock { get; }
        protected abstract ToggleSwitch ConnectionMethodToggleSwitch { get; }
        protected abstract ToggleSwitch CachingToggleSwitch { get; }
        protected abstract Slider ConcurrentDownloadsSliderControl { get; }
        protected abstract TextBlock ConcurrentDownloadsValueTextBlock { get; }
        protected abstract TextBlock RamEstimateTextBlock { get; }
        protected abstract TextBlock CacheSizeTextBlock { get; }
        protected abstract Button ClearCacheButton { get; }
        protected abstract Grid LoadingOverlayGrid { get; }
        protected abstract TextBlock LoadingOverlayTextBlock { get; }
        protected abstract Grid VerificationPanelGrid { get; }
        protected abstract void ShowVerificationPanel(string url);
        protected abstract void ResetAppRootFrame();
        protected abstract Type GetLoginPageType();

        public SettingsPageBase()
        {
            this.Loaded += Page_Loaded;
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            AuthService.AuthenticationStateChanged += AuthService_AuthenticationStateChanged;
        }

        private async void Page_Loaded(object sender, RoutedEventArgs e)
        {
            UpdateUserUI();
            LoadWikiSettings();
            LoadConnectionSettings();
            CachingToggleSwitch.IsOn = AppSettings.IsCachingEnabled;
            SetupConcurrencySlider();
            await UpdateCacheSizeDisplayAsync();
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);
            AuthService.AuthenticationStateChanged -= AuthService_AuthenticationStateChanged;
        }

        private void LoadWikiSettings()
        {
            WikiUrlTextBox.Text = AppSettings.BaseUrl;
            ScriptPathTextBox.Text = AppSettings.ScriptPath;
            ArticlePathTextBox.Text = AppSettings.ArticlePath;
        }

        private void LoadConnectionSettings()
        {
            _isConnectionToggleEvent = true;
            ConnectionMethodToggleSwitch.IsOn =
                AppSettings.ConnectionBackend == ConnectionMethod.HttpClientProxy;
            _isConnectionToggleEvent = false;
        }

        protected async void ConnectionMethodToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (_isConnectionToggleEvent)
                return;
            AppSettings.ConnectionBackend = ConnectionMethodToggleSwitch.IsOn
                ? ConnectionMethod.HttpClientProxy
                : ConnectionMethod.WebView;
            var dialog = new ContentDialog
            {
                Title = "Reload Required",
                Content = "Changing the connection backend requires an app reload to take effect.",
                PrimaryButtonText = "Reload Now",
            };
            await dialog.ShowAsync();
            ResetAppRootFrame();
        }

        protected async void DetectButton_Click(object sender, RoutedEventArgs e)
        {
            string urlToDetect = WikiUrlTextBox.Text.Trim();
            if (string.IsNullOrEmpty(urlToDetect))
            {
                DetectionStatusTextBlock.Text = "Please enter a Base URL first.";
                DetectionStatusTextBlock.Foreground = new SolidColorBrush(Colors.Red);
                DetectionStatusTextBlock.Visibility = Visibility.Visible;
                return;
            }

            LoadingOverlayGrid.Visibility = Visibility.Visible;
            LoadingOverlayTextBlock.Text = "Detecting paths...";
            DetectionStatusTextBlock.Visibility = Visibility.Collapsed;
            IApiWorker tempWorker = null;
            try
            {
                tempWorker = CreateWebViewApiWorker();
                await tempWorker.InitializeAsync(urlToDetect);
                var detectedPaths = await WikiPathDetectorService.DetectPathsAsync(
                    urlToDetect,
                    tempWorker
                );

                if (detectedPaths.WasDetectedSuccessfully)
                {
                    ScriptPathTextBox.Text = detectedPaths.ScriptPath;
                    ArticlePathTextBox.Text = detectedPaths.ArticlePath;
                    DetectionStatusTextBlock.Text =
                        "Detection successful! Review the paths and click Apply.";
                    DetectionStatusTextBlock.Foreground = new SolidColorBrush(Colors.Green);
                }
                else
                {
                    DetectionStatusTextBlock.Text =
                        "Could not automatically detect paths. Please enter them manually.";
                    DetectionStatusTextBlock.Foreground = new SolidColorBrush(Colors.Red);
                }
                DetectionStatusTextBlock.Visibility = Visibility.Visible;
            }
            catch (NeedsUserVerificationException ex)
            {
                LoadingOverlayGrid.Visibility = Visibility.Collapsed;

#if UWP_1809
                    ShowVerificationPanel(ex.Url);
#else
                DetectionStatusTextBlock.Text =
                    "Detection failed because the site is protected by Cloudflare. " +
                    "Please switch to the 'Proxy' connection backend in the settings below and try again.";
                DetectionStatusTextBlock.Foreground = new SolidColorBrush(Colors.Orange);
                DetectionStatusTextBlock.Visibility = Visibility.Visible;
#endif
            }
            catch (Exception ex)
            {
                DetectionStatusTextBlock.Text = $"An error occurred during detection: {ex.Message}";
                DetectionStatusTextBlock.Foreground = new SolidColorBrush(Colors.Red);
                DetectionStatusTextBlock.Visibility = Visibility.Visible;
            }
            finally
            {
                LoadingOverlayGrid.Visibility = Visibility.Collapsed;
                tempWorker?.Dispose();
            }
        }

        protected async void ApplyUrlButton_Click(object sender, RoutedEventArgs e)
        {
            string newUrl = WikiUrlTextBox.Text.Trim();
            if (!string.IsNullOrEmpty(newUrl) && !newUrl.EndsWith("/"))
                newUrl += "/";
            string newScriptPath = ScriptPathTextBox.Text.Trim();
            string newArticlePath = ArticlePathTextBox.Text.Trim();
            if (
                !Uri.TryCreate(newUrl, UriKind.Absolute, out var uriResult)
                || (uriResult.Scheme != "http" && uriResult.Scheme != "https")
            )
            {
                await new ContentDialog
                {
                    Title = "Invalid URL",
                    Content =
                        "Please ensure the Base URL is a valid absolute URL (e.g., 'https://en.wikipedia.org/').",
                    CloseButtonText = "OK",
                }.ShowAsync();
                return;
            }
            bool hasChanged =
                !newUrl.Equals(AppSettings.BaseUrl, StringComparison.OrdinalIgnoreCase)
                || !newScriptPath.Equals(AppSettings.ScriptPath, StringComparison.OrdinalIgnoreCase)
                || !newArticlePath.Equals(
                    AppSettings.ArticlePath,
                    StringComparison.OrdinalIgnoreCase
                );
            if (!hasChanged)
                return;
            var dialog = new ContentDialog
            {
                Title = "Apply New Wiki Settings?",
                Content =
                    "This will clear all local cache, favourites, and log you out. The app will restart after applying the changes.",
                PrimaryButtonText = "Apply and Clear Data",
                CloseButtonText = "Cancel",
            };
            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                LoadingOverlayGrid.Visibility = Visibility.Visible;
                LoadingOverlayTextBlock.Text = "Applying settings...";
                AuthService.Logout();
                await FavouritesService.ClearAllLocalFavouritesAsync();
                await ArticleCacheManager.ClearCacheAsync();
                AppSettings.BaseUrl = newUrl;
                AppSettings.ScriptPath = newScriptPath;
                AppSettings.ArticlePath = newArticlePath;
                ResetAppRootFrame();
            }
        }

        private void SetupConcurrencySlider()
        {
            ConcurrentDownloadsSliderControl.ValueChanged -= ConcurrentDownloadsSlider_ValueChanged;
            int processorCount = Environment.ProcessorCount;
            int highPerfStep = processorCount + 1;
            int unlimitedStep = processorCount + 2;
            ConcurrentDownloadsSliderControl.Maximum = unlimitedStep;
            int currentSetting = AppSettings.MaxConcurrentDownloads;
            if (currentSetting == int.MaxValue)
                ConcurrentDownloadsSliderControl.Value = unlimitedStep;
            else if (currentSetting == processorCount * highPerformanceMultiplier)
                ConcurrentDownloadsSliderControl.Value = highPerfStep;
            else
                ConcurrentDownloadsSliderControl.Value = Math.Min(currentSetting, processorCount);
            UpdateSliderDisplayTextAndEstimate();
            ConcurrentDownloadsSliderControl.ValueChanged += ConcurrentDownloadsSlider_ValueChanged;
        }

        protected void ConcurrentDownloadsSlider_ValueChanged(
            object sender,
            RangeBaseValueChangedEventArgs e
        )
        {
            if (e.NewValue == e.OldValue)
                return;
            UpdateSliderDisplayTextAndEstimate();
        }

        private void UpdateSliderDisplayTextAndEstimate()
        {
            int sliderValue = (int)ConcurrentDownloadsSliderControl.Value;
            int processorCount = Environment.ProcessorCount;
            int highPerfStep = processorCount + 1;
            int unlimitedStep = processorCount + 2;
            int actualTaskCount;
            string displayText;
            if (sliderValue == unlimitedStep)
            {
                displayText = "Unlimited";
                actualTaskCount = 256;
                AppSettings.MaxConcurrentDownloads = int.MaxValue;
            }
            else if (sliderValue == highPerfStep)
            {
                actualTaskCount = processorCount * highPerformanceMultiplier;
                displayText = $"High ({actualTaskCount})";
                AppSettings.MaxConcurrentDownloads = actualTaskCount;
            }
            else
            {
                actualTaskCount = sliderValue;
                displayText = actualTaskCount.ToString();
                AppSettings.MaxConcurrentDownloads = actualTaskCount;
            }
            ConcurrentDownloadsValueTextBlock.Text = displayText;
            long estimatedPeakRamMb = BaseRamMb + ((long)actualTaskCount * EstimatedRamPerTaskMb);
            string formattedRam =
                estimatedPeakRamMb >= 1024
                    ? $"{estimatedPeakRamMb / 1024.0:F1} GB"
                    : $"{estimatedPeakRamMb} MB";
            string estimateText = $"Estimated peak background RAM usage: ~{formattedRam}";
            if (sliderValue == unlimitedStep)
                estimateText += " (or more)";
            RamEstimateTextBlock.Text = estimateText;
        }

        private void AuthService_AuthenticationStateChanged(object sender, EventArgs e) =>
            _ = Dispatcher.RunAsync(CoreDispatcherPriority.Normal, UpdateUserUI);

        private void UpdateUserUI()
        {
            if (AuthService.IsLoggedIn)
            {
                LoggedInStatePanel.Visibility = Visibility.Visible;
                LoggedOutStatePanel.Visibility = Visibility.Collapsed;
                UsernameTextBlock.Text = AuthService.Username;
            }
            else
            {
                LoggedInStatePanel.Visibility = Visibility.Collapsed;
                LoggedOutStatePanel.Visibility = Visibility.Visible;
                LoggedOutStateTextBlock.Text =
                    $"You are not signed in. Sign in to make edits and synchronise favourites on {AppSettings.Host}.";
                SignInHyperlink.Content = $"Sign in to {AppSettings.Host}";
            }
        }

        protected void SignOutButton_Click(object sender, RoutedEventArgs e) =>
            AuthService.Logout();

        protected void SignInButton_Click(object sender, RoutedEventArgs e) =>
            this.Frame.Navigate(GetLoginPageType());

        protected void CachingToggle_Toggled(object sender, RoutedEventArgs e) =>
            AppSettings.IsCachingEnabled = CachingToggleSwitch.IsOn;

        protected async void ClearCacheButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new ContentDialog
            {
                Title = "Clear Cache?",
                Content =
                    "This will remove all saved articles and images. This action cannot be undone.",
                PrimaryButtonText = "Clear",
                CloseButtonText = "Cancel",
            };
            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            {
                await ArticleCacheManager.ClearCacheAsync();
                await UpdateCacheSizeDisplayAsync();
            }
        }

        private async Task UpdateCacheSizeDisplayAsync()
        {
            ClearCacheButton.IsEnabled = false;
            CacheSizeTextBlock.Text = "Calculating...";
            ulong cacheSizeBytes = await ArticleCacheManager.GetCacheSizeAsync();
            string formattedSize;
            if (cacheSizeBytes > 1024 * 1024 * 1024)
                formattedSize = $"{cacheSizeBytes / (1024.0 * 1024.0 * 1024.0):F2} GB";
            else if (cacheSizeBytes > 1024 * 1024)
                formattedSize = $"{cacheSizeBytes / (1024.0 * 1024.0):F2} MB";
            else if (cacheSizeBytes > 1024)
                formattedSize = $"{cacheSizeBytes / 1024.0:F2} KB";
            else
                formattedSize = $"{cacheSizeBytes} bytes";
            CacheSizeTextBlock.Text = formattedSize;
            ClearCacheButton.IsEnabled = cacheSizeBytes > 0;
        }

        protected async void AboutButton_Click(object sender, RoutedEventArgs e) =>
            await new ContentDialog
            {
                Title = "About Wiki Viewer",
                Content = new ScrollViewer()
                {
                    Content = new TextBlock()
                    {
                        Inlines =
                        {
                            new Run() { Text = "Wiki Viewer" },
                            new LineBreak(),
                            new Run() { Text = $"Version {GetAppVersion()} ({GetAppName()})" },
                            new LineBreak(),
                            new Run() { Text = "Copyright   2025 MegaBytesMe" },
                            new LineBreak(),
                            new LineBreak(),
                            new Run() { Text = "Source code available on " },
                            new Hyperlink()
                            {
                                NavigateUri = new Uri("https://github.com/megabytesme/WikiViewer"),
                                Inlines = { new Run() { Text = "GitHub" } },
                            },
                            new LineBreak(),
                            new Run() { Text = "Anything wrong? Let me know: " },
                            new Hyperlink()
                            {
                                NavigateUri = new Uri(
                                    "https://github.com/megabytesme/WikiViewer/issues"
                                ),
                                Inlines = { new Run() { Text = "Support" } },
                            },
                            new LineBreak(),
                            new Run() { Text = "Privacy Policy: " },
                            new Hyperlink()
                            {
                                NavigateUri = new Uri(
                                    "https://github.com/megabytesme/WikiViewer/blob/master/PRIVACYPOLICY.md"
                                ),
                                Inlines = { new Run() { Text = "Privacy Policy" } },
                            },
                            new LineBreak(),
                            new LineBreak(),
                            new Run() { Text = "Like what you see? View my " },
                            new Hyperlink()
                            {
                                NavigateUri = new Uri("https://github.com/megabytesme"),
                                Inlines = { new Run() { Text = "GitHub" } },
                            },
                            new Run() { Text = " and maybe my " },
                            new Hyperlink()
                            {
                                NavigateUri = new Uri(
                                    "https://apps.microsoft.com/search?query=megabytesme"
                                ),
                                Inlines = { new Run() { Text = "Other Apps," } },
                            },
                            new Run()
                            {
                                Text = " or consider buying me a coffee (supporting me) on ",
                            },
                            new Hyperlink()
                            {
                                NavigateUri = new Uri("https://ko-fi.com/megabytesme"),
                                Inlines = { new Run() { Text = "Ko-fi! :-)" } },
                            },
                            new LineBreak(),
                            new LineBreak(),
                            new Run()
                            {
                                Text =
                                    "WikiViewer is a client for browsing MediaWiki-based wikis without your web browser, online and offline (after caching).",
                            },
                        },
                        TextWrapping = TextWrapping.Wrap,
                    },
                },
                CloseButtonText = "OK",
            }.ShowAsync();

        protected async void DisclaimerButton_Click(object sender, RoutedEventArgs e) =>
            await new ContentDialog
            {
                Title = "Disclaimer",
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
                                    " and is not affiliated with, endorsed, or sponsored by the operators of any wiki.",
                            },
                            new LineBreak(),
                            new LineBreak(),
                            new Run()
                            {
                                Text =
                                    $"All article data, content, and trademarks presented from {AppSettings.Host} are the property of that site and its respective contributors. This app simply provides a native viewing experience for publicly available content.",
                            },
                        },
                        TextWrapping = TextWrapping.Wrap,
                    },
                },
                CloseButtonText = "OK",
            }.ShowAsync();

        private IApiWorker CreateWebViewApiWorker()
        {
            return App.ApiWorkerFactory.CreateApiWorker();
        }

        private string GetAppName()
        {
#if UWP_1703
            return "1703_UWP";
#else
            return "1809_UWP";
#endif
        }

        private string GetAppVersion()
        {
#if UWP_1703
            return "1.0.1.0";
#else
            return "2.0.1.0";
#endif
        }
    }
}
