using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using WikiViewer.Core;
using WikiViewer.Core.Enums;
using WikiViewer.Core.Managers;
using WikiViewer.Core.Models;
using WikiViewer.Core.Services;
using WikiViewer.Shared.Uwp.Services;
using Windows.Storage;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Documents;

namespace WikiViewer.Shared.Uwp.Pages
{
    public abstract class SettingsPageBase : Page
    {
        protected readonly ObservableCollection<WikiInstance> Wikis =
            new ObservableCollection<WikiInstance>();

        protected abstract ComboBox DefaultConnectionMethodComboBoxControl { get; }
        protected abstract ToggleSwitch CachingToggleControl { get; }
        protected abstract ComboBox ConcurrencyComboBoxControl { get; }
        protected abstract TextBlock CacheSizeTextControl { get; }
        protected abstract Button ClearCacheButtonControl { get; }
        protected abstract ListView WikiListViewControl { get; }
        protected abstract TextBlock ConcurrencyDescriptionTextControl { get; }
        protected abstract ToggleSwitch ShowCssRefreshButtonToggleControl { get; }

        public SettingsPageBase()
        {
            this.Loaded += OnLoaded;
            this.Unloaded += OnUnloaded;
        }

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            PopulateConcurrencyComboBox();
            PopulateDefaultConnectionMethodComboBox();

            var savedLevel = AppSettings.DownloadConcurrencyLevel;

            var match = ConcurrencyComboBoxControl
                .Items.OfType<ComboBoxItem>()
                .FirstOrDefault(i => Equals(i.Tag, savedLevel));

            if (match != null)
            {
                ConcurrencyComboBoxControl.SelectedItem = match;
                UpdateConcurrencyDescription(savedLevel);
            }

            var savedMethod = AppSettings.DefaultConnectionMethod;
            var methodMatch = DefaultConnectionMethodComboBoxControl
                .Items.OfType<ComboBoxItem>()
                .FirstOrDefault(i => Equals(i.Tag, savedMethod));
            if (methodMatch != null)
            {
                DefaultConnectionMethodComboBoxControl.SelectedItem = methodMatch;
            }

            DefaultConnectionMethodComboBoxControl.SelectionChanged +=
                DefaultConnectionMethodComboBox_SelectionChanged;

            CachingToggleControl.IsOn = AppSettings.IsCachingEnabled;

            try
            {
                CacheSizeTextControl.Text = "Calculating...";
                await UpdateCacheSizeAsync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to update cache size: {ex.Message}");
                CacheSizeTextControl.Text = "Error";
            }

            LoadWikis();
            WikiListViewControl.ItemsSource = Wikis;
            WikiManager.WikisChanged += OnWikisChanged;

            ShowCssRefreshButtonToggleControl.IsOn = AppSettings.ShowCssRefreshButton;
            ShowCssRefreshButtonToggleControl.Toggled += ShowCssRefreshButtonToggle_Toggled;
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            WikiManager.WikisChanged -= OnWikisChanged;
            ShowCssRefreshButtonToggleControl.Toggled -= ShowCssRefreshButtonToggle_Toggled;
        }

        protected void ShowCssRefreshButtonToggle_Toggled(object sender, RoutedEventArgs e)
        {
            var toggle = sender as ToggleSwitch;
            AppSettings.ShowCssRefreshButton = toggle.IsOn;
        }

        private void PopulateConcurrencyComboBox()
        {
            var mediumCores = Math.Max(2, Environment.ProcessorCount / 2);
            var highCores = Math.Max(2, Environment.ProcessorCount);
            var doubleCores = highCores * 2;

            ConcurrencyComboBoxControl.Items.Clear();
            ConcurrencyComboBoxControl.Items.Add(
                new ComboBoxItem { Content = $"Low (2 downloads)", Tag = ConcurrencyLevel.Low }
            );
            ConcurrencyComboBoxControl.Items.Add(
                new ComboBoxItem
                {
                    Content = $"Medium ({mediumCores} downloads)",
                    Tag = ConcurrencyLevel.Medium,
                }
            );
            ConcurrencyComboBoxControl.Items.Add(
                new ComboBoxItem
                {
                    Content = $"High ({highCores} downloads)",
                    Tag = ConcurrencyLevel.High,
                }
            );
            ConcurrencyComboBoxControl.Items.Add(
                new ComboBoxItem
                {
                    Content = $"Double ({doubleCores} downloads)",
                    Tag = ConcurrencyLevel.Double,
                }
            );
            ConcurrencyComboBoxControl.Items.Add(
                new ComboBoxItem
                {
                    Content = $"Extreme (256 downloads)",
                    Tag = ConcurrencyLevel.Extreme,
                }
            );
            ConcurrencyComboBoxControl.Items.Add(
                new ComboBoxItem { Content = "Unlimited", Tag = ConcurrencyLevel.Unlimited }
            );

            ConcurrencyComboBoxControl.SelectedValuePath = "Tag";
        }

        private void PopulateDefaultConnectionMethodComboBox()
        {
            DefaultConnectionMethodComboBoxControl.Items.Clear();
            DefaultConnectionMethodComboBoxControl.Items.Add(
                new ComboBoxItem { Content = "Auto (Recommended)", Tag = ConnectionMethod.Auto }
            );
            DefaultConnectionMethodComboBoxControl.Items.Add(
                new ComboBoxItem
                {
                    Content = "HttpClient (Recommended - Fastest)",
                    Tag = ConnectionMethod.HttpClient,
                }
            );
            DefaultConnectionMethodComboBoxControl.Items.Add(
                new ComboBoxItem
                {
#if UWP_1809
                    Content = "WebView2 (Backup - Compatibility)",
#else
                    Content = "WebView (Backup - Compatibility)",
#endif
                    Tag = ConnectionMethod.WebView,
                }
            );
            DefaultConnectionMethodComboBoxControl.Items.Add(
                new ComboBoxItem
                {
                    Content = "Proxy (Last Resort)",
                    Tag = ConnectionMethod.HttpClientProxy,
                }
            );
            DefaultConnectionMethodComboBoxControl.SelectedValuePath = "Tag";
        }

        protected void DefaultConnectionMethodComboBox_SelectionChanged(
            object sender,
            SelectionChangedEventArgs e
        )
        {
            if (
                DefaultConnectionMethodComboBoxControl.SelectedItem is ComboBoxItem selectedItem
                && selectedItem.Tag is ConnectionMethod method
            )
            {
                AppSettings.DefaultConnectionMethod = method;
            }
        }

        private void UpdateConcurrencyDescription(ConcurrencyLevel level)
        {
            string description = "";
            switch (level)
            {
                case ConcurrencyLevel.Low:
                    description = "Recommended for slow or metered connections (2 threads).";
                    break;
                case ConcurrencyLevel.Medium:
                    description =
                        "Balanced performance. Recommended for most devices (1/2 CPU Core count).";
                    break;
                case ConcurrencyLevel.High:
                    description =
                        "Uses all available processor cores for faster loading on powerful devices (All CPU core count).";
                    break;
                case ConcurrencyLevel.Double:
                    description =
                        "Aggressive loading. May impact system responsiveness on some devices (Double core count).";
                    break;
                case ConcurrencyLevel.Extreme:
                    description =
                        "Warning: High values can lead to increased memory usage and may cause instability or rate-limiting by the wiki server (256 threads).";
                    break;
                case ConcurrencyLevel.Unlimited:
                    description =
                        "Warning: High values can lead to increased memory usage and may cause instability or rate-limiting by the wiki server (Unlimited).";
                    break;
            }
            ConcurrencyDescriptionTextControl.Text = description;
        }

        private async Task OnWikisChanged()
        {
            _ = Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, LoadWikis);
        }

        private void LoadWikis()
        {
            Wikis.Clear();
            foreach (var wiki in WikiManager.GetWikis().OrderBy(w => w.Name))
            {
                Wikis.Add(wiki);
            }
        }

        private async Task UpdateCacheSizeAsync()
        {
            var sizeInBytes = await ArticleCacheManager.GetCacheSizeAsync();
            var sizeInMb = sizeInBytes / 1024.0 / 1024.0;
            CacheSizeTextControl.Text = $"{sizeInMb:F2} MB used";
        }

        protected void CachingToggle_Toggled(object sender, RoutedEventArgs e)
        {
            var toggle = sender as ToggleSwitch;
            AppSettings.IsCachingEnabled = toggle.IsOn;
        }

        protected void ConcurrencyComboBox_SelectionChanged(
            object sender,
            SelectionChangedEventArgs e
        )
        {
            if (ConcurrencyComboBoxControl.SelectedValue is ConcurrencyLevel level)
            {
                AppSettings.DownloadConcurrencyLevel = level;
                UpdateConcurrencyDescription(level);
            }
        }

        protected async void ClearCacheButton_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            button.IsEnabled = false;
            await ArticleCacheManager.ClearCacheAsync();
            await UpdateCacheSizeAsync();
            button.IsEnabled = true;
        }

        protected async void AboutButton_Click(object sender, RoutedEventArgs e)
        {
            var reviewButton = new Button
            {
                Content = "Rate and Review This App",
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Margin = new Thickness(0, 8, 0, 0)
            };
            reviewButton.Click += ReviewButton_Click;

            var scrollContent = new ScrollViewer()
            {
                Content = new TextBlock()
                {
                    Inlines =
                        {
                            new Run() { Text = "WikiViewer" },
                            new LineBreak(),
                            new Run() { Text = $"Version {GetAppVersion()} ({GetAppName()})" },
                            new LineBreak(),
                            new Run() { Text = "Copyright © 2025 MegaBytesMe" },
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
            };

            var dialogContent = new StackPanel();
            dialogContent.Children.Add(scrollContent);
            dialogContent.Children.Add(reviewButton);

            var dialog = new ContentDialog
            {
                Title = "About WikiViewer",
                Content = dialogContent,
                PrimaryButtonText = "OK"
            };

            var result = await dialog.ShowAsync();
        }

        private void ReviewButton_Click(object sender, RoutedEventArgs e)
        {
            ReviewRequestService.RequestReview();
        }

        protected async void DisclaimerButton_Click(object sender, RoutedEventArgs e)
        {
            var wikis = WikiManager.GetWikis();
            var hostsList = string.Join(", ", wikis.Select(w => w.Host));

            var textBlock = new TextBlock { TextWrapping = TextWrapping.Wrap };

            textBlock.Inlines.Add(
                new Run
                {
                    Text =
                        "This is an unofficial, third-party client for browsing MediaWiki sites. This app was created by ",
                }
            );
            textBlock.Inlines.Add(
                new Hyperlink
                {
                    NavigateUri = new Uri("https://github.com/megabytesme"),
                    Inlines = { new Run { Text = "MegaBytesMe" } },
                }
            );
            textBlock.Inlines.Add(
                new Run
                {
                    Text =
                        " and is not affiliated with, endorsed, or sponsored by the operators of any wiki.",
                }
            );
            textBlock.Inlines.Add(new LineBreak());
            textBlock.Inlines.Add(new LineBreak());
            textBlock.Inlines.Add(
                new Run
                {
                    Text =
                        $"All article data, content, and trademarks presented from the configured wikis ({hostsList}) are the property of those sites and their respective contributors. This app simply provides a native viewing experience for publicly available content.",
                }
            );

            var dialog = new ContentDialog
            {
                Title = "Disclaimer",
                Content = new ScrollViewer { Content = textBlock },
                PrimaryButtonText = "OK",
            };
            await dialog.ShowAsync();
        }

        protected abstract Type GetWikiDetailPageType();

        protected void WikiListView_ItemClick(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is WikiInstance wiki)
            {
                Frame.Navigate(GetWikiDetailPageType(), wiki.Id);
            }
        }

        protected void AddWikiButton_Click(object sender, RoutedEventArgs e)
        {
            Frame.Navigate(GetWikiDetailPageType(), true);
        }

        private string GetAppName()
        {
#if UWP_1507
            return "1507_UWP";
#else
            return "1809_UWP";
#endif
        }

        private string GetAppVersion()
        {
#if UWP_1507
            return "1.0.1.0";
#else
            return "2.0.1.0";
#endif
        }

        protected async void EditThemeButton_Click(object sender, RoutedEventArgs e)
        {
            await ThemeManager.GetThemeCssAsync();
            var file = await ApplicationData.Current.LocalFolder.GetFileAsync(
                ThemeManager.ThemeCssFileName
            );
            if (file != null)
            {
                await Windows.System.Launcher.LaunchFileAsync(file);
            }
        }

        protected async void ResetThemeButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new ContentDialog
            {
                Title = "Reset Theme?",
                Content =
                    "This will restore the default article appearance. Your custom CSS will be overwritten. Are you sure?",
                PrimaryButtonText = "Reset Theme",
                SecondaryButtonText = "Cancel",
            };

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                await ThemeManager.ResetThemeToDefaultAsync();
            }
        }
    }
}
