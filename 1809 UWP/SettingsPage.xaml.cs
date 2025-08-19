using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
using Shared_Code;
using System;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.UI;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Documents;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;

namespace _1809_UWP
{
    public sealed partial class SettingsPage : Page
    {
        private const int BaseRamMb = 400;
        private const int EstimatedRamPerTaskMb = 122;
        private const int highPerformanceMultiplier = 2;

        public SettingsPage()
        {
            this.InitializeComponent();
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            AuthService.AuthenticationStateChanged += AuthService_AuthenticationStateChanged;

            UpdateUserUI();
            LoadWikiSettings();
            CachingToggle.IsOn = AppSettings.IsCachingEnabled;
            SetupConcurrencySlider();

            await UpdateCacheSizeDisplayAsync();
        }

        private void LoadWikiSettings()
        {
            WikiUrlTextBox.Text = AppSettings.BaseUrl;
            ScriptPathTextBox.Text = AppSettings.ScriptPath;
            ArticlePathTextBox.Text = AppSettings.ArticlePath;
        }

        private async void DetectButton_Click(object sender, RoutedEventArgs e)
        {
            string urlToDetect = WikiUrlTextBox.Text.Trim();
            if (string.IsNullOrEmpty(urlToDetect))
            {
                DetectionStatusText.Text = "Please enter a Base URL first.";
                DetectionStatusText.Foreground = new SolidColorBrush(Colors.Red);
                DetectionStatusText.Visibility = Visibility.Visible;
                return;
            }

            LoadingOverlay.Visibility = Visibility.Visible;
            LoadingOverlayText.Text = "Detecting paths...";
            DetectionStatusText.Visibility = Visibility.Collapsed;

            WebView2 tempWorker = null;
            try
            {
                tempWorker = new WebView2();
                App.UIHost.Children.Add(tempWorker);
                await tempWorker.EnsureCoreWebView2Async();

                var detectedPaths = await WikiPathDetectorService.DetectPathsAsync(urlToDetect, tempWorker);

                if (detectedPaths.WasDetectedSuccessfully)
                {
                    ScriptPathTextBox.Text = detectedPaths.ScriptPath;
                    ArticlePathTextBox.Text = detectedPaths.ArticlePath;
                    DetectionStatusText.Text = "Detection successful! Review the paths and click Apply.";
                    DetectionStatusText.Foreground = new SolidColorBrush(Colors.Green);
                }
                else
                {
                    DetectionStatusText.Text = "Could not automatically detect paths. Please enter them manually.";
                    DetectionStatusText.Foreground = new SolidColorBrush(Colors.Red);
                }
                DetectionStatusText.Visibility = Visibility.Visible;
            }
            catch (NeedsUserVerificationException ex)
            {
                LoadingOverlay.Visibility = Visibility.Collapsed;
                ShowVerificationPanelAndRetryDetection(ex.Url);
            }
            catch (Exception ex)
            {
                DetectionStatusText.Text = $"An error occurred during detection: {ex.Message}";
                DetectionStatusText.Foreground = new SolidColorBrush(Colors.Red);
                DetectionStatusText.Visibility = Visibility.Visible;
            }
            finally
            {
                LoadingOverlay.Visibility = Visibility.Collapsed;

                if (tempWorker != null)
                {
                    App.UIHost.Children.Remove(tempWorker);
                    tempWorker.Close();
                }
            }
        }

        private async void ShowVerificationPanelAndRetryDetection(string url)
        {
            VerificationPanel.Visibility = Visibility.Visible;
            await VerificationWebView.EnsureCoreWebView2Async();

            TypedEventHandler<CoreWebView2, CoreWebView2NavigationCompletedEventArgs> successHandler = null;
            successHandler = async (sender, args) =>
            {
                if (args.IsSuccess && !sender.Source.Contains("challenges.cloudflare.com"))
                {
                    VerificationWebView.CoreWebView2.NavigationCompleted -= successHandler;

                    await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                    {
                        VerificationPanel.Visibility = Visibility.Collapsed;
                        DetectButton_Click(null, null);
                    });
                }
            };

            VerificationWebView.CoreWebView2.NavigationCompleted += successHandler;
            VerificationWebView.CoreWebView2.Navigate(url);
        }

        private async void ApplyUrlButton_Click(object sender, RoutedEventArgs e)
        {
            string newUrl = WikiUrlTextBox.Text.Trim();
            if (!string.IsNullOrEmpty(newUrl) && !newUrl.EndsWith("/")) newUrl += "/";

            string newScriptPath = ScriptPathTextBox.Text.Trim();
            string newArticlePath = ArticlePathTextBox.Text.Trim();

            if (!Uri.TryCreate(newUrl, UriKind.Absolute, out var uriResult) ||
                (uriResult.Scheme != Uri.UriSchemeHttp && uriResult.Scheme != Uri.UriSchemeHttps))
            {
                var invalidDialog = new ContentDialog
                {
                    Title = "Invalid URL",
                    Content = "Please ensure the Base URL is a valid absolute URL (e.g., 'https://en.wikipedia.org/').",
                    CloseButtonText = "OK"
                };
                await invalidDialog.ShowAsync();
                return;
            }

            bool hasChanged = !newUrl.Equals(AppSettings.BaseUrl, StringComparison.OrdinalIgnoreCase) ||
                              !newScriptPath.Equals(AppSettings.ScriptPath, StringComparison.OrdinalIgnoreCase) ||
                              !newArticlePath.Equals(AppSettings.ArticlePath, StringComparison.OrdinalIgnoreCase);

            if (!hasChanged)
            {
                return;
            }

            var dialog = new ContentDialog
            {
                Title = "Apply New Wiki Settings?",
                Content = "This will clear all local cache, favourites, and log you out. This action cannot be undone. The app will restart its main view after applying the changes.",
                PrimaryButtonText = "Apply and Clear Data",
                CloseButtonText = "Cancel"
            };

            var result = await dialog.ShowAsync();

            if (result == ContentDialogResult.Primary)
            {
                LoadingOverlay.Visibility = Visibility.Visible;
                LoadingOverlayText.Text = "Applying settings...";

                AuthService.Logout();
                await FavouritesService.ClearAllLocalFavouritesAsync();
                await ArticleCacheManager.ClearCacheAsync();

                AppSettings.BaseUrl = newUrl;
                AppSettings.ScriptPath = newScriptPath;
                AppSettings.ArticlePath = newArticlePath;

                App.ResetRootFrame();
            }
        }

        private void SetupConcurrencySlider()
        {
            ConcurrentDownloadsSlider.ValueChanged -= ConcurrentDownloadsSlider_ValueChanged;

            int processorCount = Environment.ProcessorCount;
            int highPerfStep = processorCount + 1;
            int unlimitedStep = processorCount + 2;
            ConcurrentDownloadsSlider.Maximum = unlimitedStep;

            int currentSetting = AppSettings.MaxConcurrentDownloads;

            if (currentSetting == int.MaxValue)
            {
                ConcurrentDownloadsSlider.Value = unlimitedStep;
            }
            else if (currentSetting == processorCount * highPerformanceMultiplier)
            {
                ConcurrentDownloadsSlider.Value = highPerfStep;
            }
            else
            {
                ConcurrentDownloadsSlider.Value = Math.Min(currentSetting, processorCount);
            }

            UpdateSliderDisplayTextAndEstimate();

            ConcurrentDownloadsSlider.ValueChanged += ConcurrentDownloadsSlider_ValueChanged;
        }

        private void ConcurrentDownloadsSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            if (e.NewValue == e.OldValue) return;

            UpdateSliderDisplayTextAndEstimate();
        }

        private void UpdateSliderDisplayTextAndEstimate()
        {
            if (ConcurrentDownloadsValueText == null || RamEstimateText == null) return;

            int sliderValue = (int)ConcurrentDownloadsSlider.Value;
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

            ConcurrentDownloadsValueText.Text = displayText;

            long estimatedPeakRamMb = BaseRamMb + ((long)actualTaskCount * EstimatedRamPerTaskMb);
            string formattedRam;
            if (estimatedPeakRamMb >= 1024)
            {
                double totalRamGb = estimatedPeakRamMb / 1024.0;
                formattedRam = $"{totalRamGb:F1} GB";
            }
            else
            {
                formattedRam = $"{estimatedPeakRamMb} MB";
            }

            string estimateText = $"Estimated peak background RAM usage: ~{formattedRam}";
            if (sliderValue == unlimitedStep)
            {
                estimateText += " (or more)";
            }
            RamEstimateText.Text = estimateText;
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);
            AuthService.AuthenticationStateChanged -= AuthService_AuthenticationStateChanged;
        }

        private void AuthService_AuthenticationStateChanged(object sender, EventArgs e)
        {
            _ = Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, UpdateUserUI);
        }

        private void UpdateUserUI()
        {
            if (AuthService.IsLoggedIn)
            {
                LoggedInState.Visibility = Visibility.Visible;
                LoggedOutState.Visibility = Visibility.Collapsed;
                UsernameText.Text = AuthService.Username;
                ProfilePicture.DisplayName = AuthService.Username;
            }
            else
            {
                LoggedInState.Visibility = Visibility.Collapsed;
                LoggedOutState.Visibility = Visibility.Visible;
                LoggedOutStateTextBlock.Text = $"You are not signed in. Sign in to make edits and synchronise favourites on {AppSettings.Host}.";
                SignInLink.Content = $"Sign in to {AppSettings.Host}";
            }
        }

        private void SignOutButton_Click(object sender, RoutedEventArgs e)
        {
            AuthService.Logout();
        }

        private void SignInButton_Click(object sender, RoutedEventArgs e)
        {
            this.Frame.Navigate(typeof(LoginPage));
        }

        private void CachingToggle_Toggled(object sender, RoutedEventArgs e)
        {
            AppSettings.IsCachingEnabled = CachingToggle.IsOn;
        }

        private async void ClearCacheButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new ContentDialog
            {
                Title = "Clear Cache?",
                Content = "This will remove all saved articles and images. This action cannot be undone.",
                PrimaryButtonText = "Clear",
                CloseButtonText = "Cancel"
            };

            var result = await dialog.ShowAsync();

            if (result == ContentDialogResult.Primary)
            {
                await ArticleCacheManager.ClearCacheAsync();
                await UpdateCacheSizeDisplayAsync();
            }
        }

        private async Task UpdateCacheSizeDisplayAsync()
        {
            ClearCacheButton.IsEnabled = false;
            CacheSizeText.Text = "Calculating...";

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

            CacheSizeText.Text = formattedSize;
            ClearCacheButton.IsEnabled = cacheSizeBytes > 0;
        }

        private async void AboutButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new ContentDialog
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
                            new Run() { Text = "Version 2.0.1.0 (1809_UWP)" },
                            new LineBreak(),
                            new Run() { Text = "Copyright © 2025 MegaBytesMe" },
                            new LineBreak(), new LineBreak(),
                            new Run() { Text = "Source code available on " },
                            new Hyperlink() { NavigateUri = new Uri("https://github.com/megabytesme/WikiViewer"), Inlines = { new Run() { Text = "GitHub" } }, },
                            new LineBreak(),
                            new Run() { Text = "Anything wrong? Let me know: " },
                            new Hyperlink() { NavigateUri = new Uri("https://github.com/megabytesme/WikiViewer/issues"), Inlines = { new Run() { Text = "Support" } }, },
                            new LineBreak(),
                            new Run() { Text = "Privacy Policy: " },
                            new Hyperlink() { NavigateUri = new Uri("https://github.com/megabytesme/WikiViewer/blob/master/PRIVACYPOLICY.md"), Inlines = { new Run() { Text = "Privacy Policy" } }, },
                            new LineBreak(), new LineBreak(),
                            new Run() { Text = "Like what you see? View my " },
                            new Hyperlink() { NavigateUri = new Uri("https://github.com/megabytesme"), Inlines = { new Run() { Text = "GitHub" } }, },
                            new Run() { Text = " and maybe my " },
                            new Hyperlink() { NavigateUri = new Uri("https://apps.microsoft.com/search?query=megabytesme"), Inlines = { new Run() { Text = "Other Apps," } }, },
                            new Run() { Text = " or consider buying me a coffee (supporting me) on " },
                            new Hyperlink() { NavigateUri = new Uri("https://ko-fi.com/megabytesme"), Inlines = { new Run() { Text = "Ko-fi! :-)" } }, },
                            new LineBreak(), new LineBreak(),
                            new Run() { Text = "WikiViewer is a client for browsing MediaWiki-based wikis without your web browser, online and offline (after caching).", },
                        },
                        TextWrapping = TextWrapping.Wrap,
                    },
                },
                CloseButtonText = "OK",
            };
            await dialog.ShowAsync();
        }

        private async void DisclaimerButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new ContentDialog
            {
                Title = "Disclaimer",
                Content = new ScrollViewer()
                {
                    Content = new TextBlock()
                    {
                        Inlines =
                        {
                            new Run() { Text = "This is an unofficial, third-party client for browsing MediaWiki sites. This app was created by " },
                            new Hyperlink() { NavigateUri = new Uri("https://github.com/megabytesme"), Inlines = { new Run() { Text = "MegaBytesMe" } }, },
                            new Run() { Text = " and is not affiliated with, endorsed, or sponsored by the operators of any wiki." },
                            new LineBreak(), new LineBreak(),
                            new Run() { Text = $"All article data, content, and trademarks presented from {AppSettings.Host} are the property of that site and its respective contributors. This app simply provides a native viewing experience for publicly available content." },
                        },
                        TextWrapping = TextWrapping.Wrap,
                    },
                },
                CloseButtonText = "OK",
            };
            await dialog.ShowAsync();
        }
    }
}