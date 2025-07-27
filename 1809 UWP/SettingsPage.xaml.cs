using System;
using System.Threading.Tasks;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
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
    }
}