using System;
using WikiViewer.Core.Enums;
using WikiViewer.Core.Models;
using WikiViewer.Core.Services;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;

namespace WikiViewer.Shared.Uwp.Pages
{
    public sealed partial class WikiDetailPage : Page
    {
        private WikiInstance _currentWiki;

        public WikiDetailPage()
        {
            this.InitializeComponent();
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            if (e.Parameter is Guid wikiId)
            {
                _currentWiki = WikiManager.GetWikiById(wikiId);
                if (_currentWiki != null)
                {
                    PopulateDetails();
                }
            }

            if (_currentWiki == null)
            {
                Frame.GoBack();
            }
        }

        private void PopulateDetails()
        {
            PageTitle.Text = $"Editing '{_currentWiki.Name}'";
            WikiNameTextBox.Text = _currentWiki.Name;
            WikiUrlTextBox.Text = _currentWiki.BaseUrl;
            ScriptPathTextBox.Text = _currentWiki.ScriptPath;
            ArticlePathTextBox.Text = _currentWiki.ArticlePath;
            ConnectionMethodToggleSwitch.IsOn =
                _currentWiki.PreferredConnectionMethod == ConnectionMethod.HttpClientProxy;
        }

        private async void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentWiki == null)
                return;

            _currentWiki.Name = WikiNameTextBox.Text;
            _currentWiki.BaseUrl = WikiUrlTextBox.Text;
            _currentWiki.ScriptPath = ScriptPathTextBox.Text;
            _currentWiki.ArticlePath = ArticlePathTextBox.Text;
            _currentWiki.PreferredConnectionMethod = ConnectionMethodToggleSwitch.IsOn
                ? ConnectionMethod.HttpClientProxy
                : ConnectionMethod.WebView;

            await WikiManager.SaveAsync();

            var dialog = new ContentDialog
            {
                Title = "Reload Required",
                Content =
                    "You have modified a wiki's settings. The app needs to reload to apply these changes.",
                PrimaryButtonText = "Reload Now",
            };
            await dialog.ShowAsync();
            App.ResetRootFrame();
        }

        private async void DeleteButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentWiki == null || WikiManager.GetWikis().Count <= 1)
            {
                var cantDeleteDialog = new ContentDialog
                {
                    Title = "Cannot Delete",
                    Content = "You cannot delete the last remaining wiki.",
                    CloseButtonText = "OK",
                };
                await cantDeleteDialog.ShowAsync();
                return;
            }

            var dialog = new ContentDialog
            {
                Title = $"Delete '{_currentWiki.Name}'?",
                Content =
                    "This will permanently delete the wiki, all associated accounts, and its favourites from the app. This action cannot be undone.",
                PrimaryButtonText = "Delete",
                CloseButtonText = "Cancel",
            };
            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            {
                await WikiManager.RemoveWikiAsync(_currentWiki.Id);
                Frame.GoBack();
            }
        }

        private async void DetectPathsButton_Click(object sender, RoutedEventArgs e)
        {
            string urlToDetect = WikiUrlTextBox.Text.Trim();
            if (string.IsNullOrEmpty(urlToDetect))
            {
                DetectionStatusTextBlock.Text = "Please enter a Base URL first.";
                DetectionStatusTextBlock.Foreground = new SolidColorBrush(Colors.Red);
                DetectionStatusTextBlock.Visibility = Visibility.Visible;
                return;
            }

            LoadingOverlay.Visibility = Visibility.Visible;
            LoadingText.Text = "Detecting paths...";
            DetectionStatusTextBlock.Visibility = Visibility.Collapsed;

            var tempWiki = new WikiInstance
            {
                BaseUrl = urlToDetect,
                PreferredConnectionMethod = ConnectionMethod.WebView,
            };

            using (var tempWorker = App.ApiWorkerFactory.CreateApiWorker(tempWiki))
            {
                try
                {
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
                            "Detection successful! Review the paths and click Save.";
                        DetectionStatusTextBlock.Foreground = new SolidColorBrush(Colors.Green);
                    }
                    else
                    {
                        DetectionStatusTextBlock.Text =
                            "Could not automatically detect paths. Please enter them manually.";
                        DetectionStatusTextBlock.Foreground = new SolidColorBrush(Colors.Red);
                    }
                }
                catch (NeedsUserVerificationException ex)
                {
                    ShowVerificationPanel(ex.Url);
                }
                catch (Exception ex)
                {
                    DetectionStatusTextBlock.Text =
                        $"An error occurred during detection: {ex.Message}";
                    DetectionStatusTextBlock.Foreground = new SolidColorBrush(Colors.Red);
                }
                finally
                {
                    DetectionStatusTextBlock.Visibility = Visibility.Visible;
                    LoadingOverlay.Visibility = Visibility.Collapsed;
                }
            }
        }

        partial void ShowVerificationPanel(string url);
    }
}
