using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using WikiViewer.Core;
using WikiViewer.Core.Models;
using WikiViewer.Core.Services;
using WikiViewer.Shared.Uwp.Managers;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Documents;

namespace WikiViewer.Shared.Uwp.Pages
{
    public abstract class SettingsPageBase : Page
    {
        protected readonly ObservableCollection<WikiInstance> Wikis =
            new ObservableCollection<WikiInstance>();

        protected abstract ToggleSwitch CachingToggleControl { get; }
        protected abstract Slider ConcurrentDownloadsSliderControl { get; }
        protected abstract TextBlock ConcurrentDownloadsValueTextControl { get; }
        protected abstract TextBlock CacheSizeTextControl { get; }
        protected abstract Button ClearCacheButtonControl { get; }
        protected abstract ListView WikiListViewControl { get; }

        public SettingsPageBase()
        {
            this.Loaded += OnLoaded;
            this.Unloaded += OnUnloaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            CachingToggleControl.IsOn = AppSettings.IsCachingEnabled;
            ConcurrentDownloadsSliderControl.Value = AppSettings.MaxConcurrentDownloads;
            ConcurrentDownloadsValueTextControl.Text =
                AppSettings.MaxConcurrentDownloads.ToString();
            _ = UpdateCacheSizeAsync();

            LoadWikis();
            WikiListViewControl.ItemsSource = Wikis;
            WikiManager.WikisChanged += OnWikisChanged;
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            WikiManager.WikisChanged -= OnWikisChanged;
        }

        private void OnWikisChanged(object sender, EventArgs e)
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

        protected void ConcurrentDownloadsSlider_ValueChanged(
            object sender,
            Windows.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e
        )
        {
            int value = (int)e.NewValue;
            AppSettings.MaxConcurrentDownloads = value;
            if (ConcurrentDownloadsValueTextControl != null)
            {
                ConcurrentDownloadsValueTextControl.Text = value.ToString();
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

        protected void WikiListView_ItemClick(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is WikiInstance wiki)
            {
                Frame.Navigate(typeof(WikiDetailPage), wiki.Id);
            }
        }

        protected void AddWikiButton_Click(object sender, RoutedEventArgs e)
        {
            Frame.Navigate(typeof(WikiDetailPage), true);
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

        protected async void DisclaimerButton_Click(object sender, RoutedEventArgs e)
        {
            var wikis = WikiManager.GetWikis();
            var hostsList = string.Join(", ", wikis.Select(w => w.Host));

            var textBlock = new TextBlock { TextWrapping = TextWrapping.Wrap };

            textBlock.Inlines.Add(new Run { Text = "This is an unofficial, third-party client for browsing MediaWiki sites. This app was created by " });
            textBlock.Inlines.Add(new Hyperlink
            {
                NavigateUri = new Uri("https://github.com/megabytesme"),
                Inlines = { new Run { Text = "MegaBytesMe" } }
            });
            textBlock.Inlines.Add(new Run { Text = " and is not affiliated with, endorsed, or sponsored by the operators of any wiki." });
            textBlock.Inlines.Add(new LineBreak());
            textBlock.Inlines.Add(new LineBreak());
            textBlock.Inlines.Add(new Run { Text = $"All article data, content, and trademarks presented from the configured wikis ({hostsList}) are the property of those sites and their respective contributors. This app simply provides a native viewing experience for publicly available content." });

            var dialog = new ContentDialog
            {
                Title = "Disclaimer",
                Content = new ScrollViewer { Content = textBlock },
                CloseButtonText = "OK",
            };
            await dialog.ShowAsync();
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
