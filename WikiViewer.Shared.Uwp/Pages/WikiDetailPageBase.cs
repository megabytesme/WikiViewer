using System;
using System.Threading.Tasks;
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
    public abstract class WikiDetailPageBase : Page
    {
        private WikiInstance _currentWiki;
        private bool _isNewWiki = false;

        protected abstract TextBlock PageTitleTextBlockControl { get; }
        protected abstract TextBox WikiNameTextBoxControl { get; }
        protected abstract TextBox WikiUrlTextBoxControl { get; }
        protected abstract TextBox ScriptPathTextBoxControl { get; }
        protected abstract TextBox ArticlePathTextBoxControl { get; }
        protected abstract ToggleSwitch ConnectionMethodToggleSwitchControl { get; }
        protected abstract TextBlock DetectionStatusTextBlockControl { get; }
        protected abstract Grid LoadingOverlayControl { get; }
        protected abstract TextBlock LoadingTextControl { get; }

        protected abstract void ShowVerificationPanel(string url);

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
            else if (e.Parameter is bool isNew && isNew)
            {
                _isNewWiki = true;
                _currentWiki = new WikiInstance
                {
                    Name = "New Wiki",
                    BaseUrl = "https://www.mediawiki.org/",
                };
                PopulateDetails();
                PageTitleTextBlockControl.Text = "Add New Wiki";
            }

            if (_currentWiki == null)
            {
                Frame.GoBack();
            }
        }

        private void PopulateDetails()
        {
            PageTitleTextBlockControl.Text = _isNewWiki
                ? "Add New Wiki"
                : $"Editing '{_currentWiki.Name}'";
            WikiNameTextBoxControl.Text = _currentWiki.Name;
            WikiUrlTextBoxControl.Text = _currentWiki.BaseUrl;
            ScriptPathTextBoxControl.Text = _currentWiki.ScriptPath;
            ArticlePathTextBoxControl.Text = _currentWiki.ArticlePath;
            ConnectionMethodToggleSwitchControl.IsOn =
                _currentWiki.PreferredConnectionMethod == ConnectionMethod.HttpClientProxy;
        }

        protected async void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentWiki == null)
                return;

            _currentWiki.Name = WikiNameTextBoxControl.Text;
            _currentWiki.BaseUrl = WikiUrlTextBoxControl.Text;
            _currentWiki.ScriptPath = ScriptPathTextBoxControl.Text;
            _currentWiki.ArticlePath = ArticlePathTextBoxControl.Text;
            _currentWiki.PreferredConnectionMethod = ConnectionMethodToggleSwitchControl.IsOn
                ? ConnectionMethod.HttpClientProxy
                : ConnectionMethod.WebView;

            if (_isNewWiki)
            {
                await WikiManager.AddWikiAsync(_currentWiki);
            }
            else
            {
                await WikiManager.SaveAsync();
            }

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

        protected async void DeleteButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentWiki == null || _isNewWiki)
                return;

            if (WikiManager.GetWikis().Count <= 1)
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

        protected async void DetectPathsButton_Click(object sender, RoutedEventArgs e)
        {
            string urlToDetect = WikiUrlTextBoxControl.Text.Trim();
            if (string.IsNullOrEmpty(urlToDetect))
            {
                DetectionStatusTextBlockControl.Text = "Please enter a Base URL first.";
                DetectionStatusTextBlockControl.Foreground = new SolidColorBrush(Colors.Red);
                DetectionStatusTextBlockControl.Visibility = Visibility.Visible;
                return;
            }

            LoadingOverlayControl.Visibility = Visibility.Visible;
            LoadingTextControl.Text = "Detecting paths...";
            DetectionStatusTextBlockControl.Visibility = Visibility.Collapsed;

            var tempWiki = new WikiInstance
            {
                BaseUrl = urlToDetect,
                PreferredConnectionMethod = ConnectionMethodToggleSwitchControl.IsOn
                    ? ConnectionMethod.HttpClientProxy
                    : ConnectionMethod.WebView,
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
                        ScriptPathTextBoxControl.Text = detectedPaths.ScriptPath;
                        ArticlePathTextBoxControl.Text = detectedPaths.ArticlePath;
                        DetectionStatusTextBlockControl.Text =
                            "Detection successful! Review the paths and click Save.";
                        DetectionStatusTextBlockControl.Foreground = new SolidColorBrush(
                            Colors.Green
                        );
                    }
                    else
                    {
                        DetectionStatusTextBlockControl.Text =
                            "Could not automatically detect paths. Please enter them manually.";
                        DetectionStatusTextBlockControl.Foreground = new SolidColorBrush(
                            Colors.Red
                        );
                    }
                }
                catch (NeedsUserVerificationException ex)
                {
#if UWP_1809
                    ShowVerificationPanel(ex.Url);
#else
                    DetectionStatusTextBlockControl.Text =
                        "Detection failed. This site is protected by a security check that is incompatible with this version of WebView. "
                        + "Please switch the 'Connection Backend' to 'Proxy' below and try again.";
                    DetectionStatusTextBlockControl.Foreground = new SolidColorBrush(Colors.Orange);
#endif
                }
                catch (Exception ex)
                {
                    DetectionStatusTextBlockControl.Text =
                        $"An error occurred during detection: {ex.Message}";
                    DetectionStatusTextBlockControl.Foreground = new SolidColorBrush(Colors.Red);
                }
                finally
                {
                    DetectionStatusTextBlockControl.Visibility = Visibility.Visible;
                    LoadingOverlayControl.Visibility = Visibility.Collapsed;
                }
            }
        }
    }
}
