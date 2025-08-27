using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using WikiViewer.Core;
using WikiViewer.Core.Enums;
using WikiViewer.Core.Models;
using WikiViewer.Core.Services;
using WikiViewer.Shared.Uwp.Converters;
using WikiViewer.Shared.Uwp.Services;
using Windows.Storage;
using Windows.Storage.Pickers;
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
        protected abstract ComboBox ConnectionMethodComboBoxControl { get; }
        protected abstract TextBlock DetectionStatusTextBlockControl { get; }
        protected abstract Grid LoadingOverlayControl { get; }
        protected abstract TextBlock LoadingTextControl { get; }
        protected abstract Image IconPreviewImageControl { get; }
        protected abstract Button SetIconButtonControl { get; }
        protected abstract Button RemoveIconButtonControl { get; }

        private readonly StringToBitmapImageConverter _iconConverter =
            new StringToBitmapImageConverter();

        protected abstract void ShowVerificationPanel(string url);

        public WikiDetailPageBase()
        {
            this.Loaded += OnLoaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            if (_currentWiki != null)
            {
                PopulateDetails();

                var mainPage = this.FindParent<MainPageBase>();
                if (mainPage != null)
                {
                    mainPage.SetPageTitle(
                        _isNewWiki ? "Add New Wiki" : $"Editing '{_currentWiki.Name}'"
                    );
                }

                var methodMatch = ConnectionMethodComboBoxControl
                    .Items.OfType<ComboBoxItem>()
                    .FirstOrDefault(i => Equals(i.Tag, _currentWiki.PreferredConnectionMethod));

                if (methodMatch != null)
                {
                    ConnectionMethodComboBoxControl.SelectedItem = methodMatch;
                }

                UpdateIconState();
            }
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

            if (e.Parameter is Guid wikiId)
            {
                _currentWiki = WikiManager.GetWikiById(wikiId);
            }
            else if (e.Parameter is bool isNew && isNew)
            {
                _isNewWiki = true;
                _currentWiki = new WikiInstance
                {
                    Name = "New Wiki",
                    BaseUrl = "https://www.mediawiki.org/",
                    PreferredConnectionMethod = AppSettings.DefaultConnectionMethod,
                };
            }

            if (_currentWiki == null)
            {
                if (Frame.CanGoBack)
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

            ConnectionMethodComboBoxControl.Items.Clear();
            ConnectionMethodComboBoxControl.Items.Add(
                new ComboBoxItem { Content = "Auto (Recommended)", Tag = ConnectionMethod.Auto }
            );
            ConnectionMethodComboBoxControl.Items.Add(
                new ComboBoxItem
                {
                    Content = "HttpClient (Recommended - Fastest)",
                    Tag = ConnectionMethod.HttpClient,
                }
            );
            ConnectionMethodComboBoxControl.Items.Add(
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
            ConnectionMethodComboBoxControl.Items.Add(
                new ComboBoxItem
                {
                    Content = "Proxy (Last Resort)",
                    Tag = ConnectionMethod.HttpClientProxy,
                }
            );
            ConnectionMethodComboBoxControl.SelectedValuePath = "Tag";
        }

        protected async void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            string baseUrl = WikiUrlTextBoxControl.Text.Trim();
            if (
                string.IsNullOrEmpty(baseUrl)
                || !Uri.IsWellFormedUriString(baseUrl, UriKind.Absolute)
            )
            {
                DetectionStatusTextBlockControl.Text =
                    "Please enter a valid, full URL (e.g., https://en.wikipedia.org/).";
                DetectionStatusTextBlockControl.Visibility = Visibility.Visible;
                return;
            }
            if (_currentWiki == null)
                return;

            _currentWiki.Name = WikiNameTextBoxControl.Text;
            _currentWiki.BaseUrl = WikiUrlTextBoxControl.Text;
            _currentWiki.ScriptPath = ScriptPathTextBoxControl.Text;
            _currentWiki.ArticlePath = ArticlePathTextBoxControl.Text;
            _currentWiki.PreferredConnectionMethod = (ConnectionMethod)
                ConnectionMethodComboBoxControl.SelectedValue;

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
                    PrimaryButtonText = "OK",
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
                SecondaryButtonText = "Cancel",
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
            DetectionStatusTextBlockControl.Visibility = Visibility.Collapsed;

            var selectedMethod = (ConnectionMethod)ConnectionMethodComboBoxControl.SelectedValue;

            if (selectedMethod == ConnectionMethod.Auto)
            {
                LoadingTextControl.Text = "Detecting best connection method and paths...";
                var testResult = await ConnectionTesterService.FindWorkingMethodAndPathsAsync(
                    urlToDetect,
                    App.ApiWorkerFactory
                );

                if (testResult.IsSuccess)
                {
                    ScriptPathTextBoxControl.Text = testResult.Paths.ScriptPath;
                    ArticlePathTextBoxControl.Text = testResult.Paths.ArticlePath;
                    DetectionStatusTextBlockControl.Text =
                        $"Success! Found a working connection using '{testResult.Method}'.";
                    DetectionStatusTextBlockControl.Foreground = new SolidColorBrush(
                        Windows.UI.Colors.Green
                    );

                    await Dispatcher.RunAsync(
                        Windows.UI.Core.CoreDispatcherPriority.Normal,
                        () =>
                        {
                            ConnectionMethodComboBoxControl.SelectedValue = testResult.Method;

                            var methodMatch = ConnectionMethodComboBoxControl
                                .Items.OfType<ComboBoxItem>()
                                .FirstOrDefault(i => Equals(i.Tag, testResult.Method));

                            if (methodMatch != null)
                            {
                                ConnectionMethodComboBoxControl.SelectedItem = methodMatch;
                            }
                        }
                    );
                }
                else
                {
                    DetectionStatusTextBlockControl.Text =
                        "Auto-detection failed. No working connection method found. The site may be offline or incompatible.";
                    DetectionStatusTextBlockControl.Foreground = new SolidColorBrush(
                        Windows.UI.Colors.Red
                    );
                }
            }
            else
            {
                LoadingTextControl.Text = $"Testing with '{selectedMethod}' method...";
                var tempWiki = new WikiInstance
                {
                    BaseUrl = urlToDetect,
                    PreferredConnectionMethod = selectedMethod,
                };
                using (var worker = App.ApiWorkerFactory.CreateApiWorker(tempWiki))
                {
                    try
                    {
                        await worker.InitializeAsync(urlToDetect);
                        var paths = await WikiPathDetectorService.DetectPathsAsync(
                            urlToDetect,
                            worker
                        );
                        if (paths.WasDetectedSuccessfully)
                        {
                            ScriptPathTextBoxControl.Text = paths.ScriptPath;
                            ArticlePathTextBoxControl.Text = paths.ArticlePath;
                            DetectionStatusTextBlockControl.Text =
                                $"Success! Connection with '{selectedMethod}' method works.";
                            DetectionStatusTextBlockControl.Foreground = new SolidColorBrush(
                                Windows.UI.Colors.Green
                            );
                        }
                        else
                        {
                            DetectionStatusTextBlockControl.Text =
                                $"Failed to detect paths using the '{selectedMethod}' method.";
                            DetectionStatusTextBlockControl.Foreground = new SolidColorBrush(
                                Windows.UI.Colors.Red
                            );
                        }
                    }
                    catch (Exception ex)
                    {
                        DetectionStatusTextBlockControl.Text =
                            $"Failed to connect using '{selectedMethod}': {ex.Message}";
                        DetectionStatusTextBlockControl.Foreground = new SolidColorBrush(
                            Windows.UI.Colors.Red
                        );
                    }
                }
            }

            DetectionStatusTextBlockControl.Visibility = Visibility.Visible;
            LoadingOverlayControl.Visibility = Visibility.Collapsed;
        }

        protected async void SetIconButton_Click(object sender, RoutedEventArgs e)
        {
            var picker = new FileOpenPicker
            {
                ViewMode = PickerViewMode.Thumbnail,
                SuggestedStartLocation = PickerLocationId.PicturesLibrary,
            };
            picker.FileTypeFilter.Add(".png");
            picker.FileTypeFilter.Add(".jpg");
            picker.FileTypeFilter.Add(".jpeg");
            picker.FileTypeFilter.Add(".gif");
            picker.FileTypeFilter.Add(".ico");

            StorageFile file = await picker.PickSingleFileAsync();
            if (file != null)
            {
                var iconsFolder = await ApplicationData.Current.LocalFolder.CreateFolderAsync(
                    "favicons",
                    CreationCollisionOption.OpenIfExists
                );

                string newFileName = _currentWiki.Id.ToString() + Path.GetExtension(file.Name);
                await file.CopyAsync(iconsFolder, newFileName, NameCollisionOption.ReplaceExisting);

                _currentWiki.IconUrl = $"ms-appdata:///local/favicons/{newFileName}";
                _currentWiki.IsIconUserSet = true;

                UpdateIconState();
            }
        }

        protected async void RemoveIconButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentWiki == null)
                return;

            if (_currentWiki.IsIconUserSet && !string.IsNullOrEmpty(_currentWiki.IconUrl))
            {
                try
                {
                    var file = await StorageFile.GetFileFromApplicationUriAsync(
                        new Uri(_currentWiki.IconUrl)
                    );
                    await file.DeleteAsync();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine(
                        $"[CustomIcon] Could not delete custom icon file: {ex.Message}"
                    );
                }
            }

            _currentWiki.IconUrl = null;
            _currentWiki.IsIconUserSet = false;
            UpdateIconState();

            LoadingOverlayControl.Visibility = Visibility.Visible;
            LoadingTextControl.Text = "Re-fetching original favicon...";

            using (var worker = App.ApiWorkerFactory.CreateApiWorker(_currentWiki))
            {
                await FaviconService.FetchAndCacheFaviconUrlAsync(
                    _currentWiki,
                    worker,
                    forceRefresh: true
                );
            }

            UpdateIconState();
            LoadingOverlayControl.Visibility = Visibility.Collapsed;
        }

        private void UpdateIconState()
        {
            string iconUrl = _currentWiki?.IconUrl;
            if (string.IsNullOrEmpty(iconUrl))
            {
                iconUrl = "ms-appx:///Assets/Square150x150Logo.png";
            }

            IconPreviewImageControl.Source = (Windows.UI.Xaml.Media.ImageSource)
                _iconConverter.Convert(iconUrl, null, null, null);

            RemoveIconButtonControl.Visibility =
                _currentWiki != null && _currentWiki.IsIconUserSet
                    ? Visibility.Visible
                    : Visibility.Collapsed;
        }
    }
}
