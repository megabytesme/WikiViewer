using System;
using System.Threading.Tasks;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Documents;
using Windows.UI.Xaml.Navigation;

namespace _1809_UWP
{
    public sealed partial class SettingsPage : Page
    {
        public SettingsPage()
        {
            this.InitializeComponent();
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            CachingToggle.IsOn = AppSettings.IsCachingEnabled;
            await UpdateCacheSizeDisplayAsync();
        }

        private void CachingToggle_Toggled(object sender, RoutedEventArgs e)
        {
            AppSettings.IsCachingEnabled = CachingToggle.IsOn;
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
                ClearCacheButton.IsEnabled = false;
                CacheSizeText.Text = "Clearing...";

                await ArticleCacheManager.ClearCacheAsync();

                await UpdateCacheSizeDisplayAsync();
            }
        }

        private async void AboutButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new ContentDialog
            {
                Title = "About WikiBeta",
                Content = new ScrollViewer()
                {
                    Content = new TextBlock()
                    {
                        Inlines =
                        {
                            new Run() { Text = "WikiBeta" },
                            new LineBreak(),
                            new Run() { Text = "Version 2.0.0.0 (1809_UWP)" },
                            new LineBreak(),
                            new Run() { Text = "Copyright © 2025 MegaBytesMe" },
                            new LineBreak(),
                            new LineBreak(),
                            new Run() { Text = "Source code available on " },
                            new Hyperlink()
                            {
                                NavigateUri = new Uri("https://github.com/megabytesme/WikiBeta"),
                                Inlines = { new Run() { Text = "GitHub" } },
                            },
                            new LineBreak(),
                            new Run() { Text = "Anything wrong? Let us know: " },
                            new Hyperlink()
                            {
                                NavigateUri = new Uri(
                                    "https://github.com/megabytesme/WikiBeta/issues"
                                ),
                                Inlines = { new Run() { Text = "Support" } },
                            },
                            new LineBreak(),
                            new Run() { Text = "Privacy Policy: " },
                            new Hyperlink()
                            {
                                NavigateUri = new Uri(
                                    "https://github.com/megabytesme/WikiBeta/blob/master/PRIVACYPOLICY.md"
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
                                Inlines = { new Run() { Text = "Other Apps" } },
                            },
                            new LineBreak(),
                            new LineBreak(),
                            new Run()
                            {
                                Text =
                                    "WikiBeta is an app which allows you to view the Beta Wiki without your web browser, online and offline (after caching).",
                            },
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