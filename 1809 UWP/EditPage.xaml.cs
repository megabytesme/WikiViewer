using Newtonsoft.Json.Linq;
using Shared_Code;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Navigation;

namespace _1809_UWP
{
    public sealed partial class EditPage : Page
    {
        private string _pageTitle;
        private string _originalWikitext;
        private IApiWorker _apiWorker;

        public EditPage()
        {
            this.InitializeComponent();
            Environment.SetEnvironmentVariable("WEBVIEW2_DEFAULT_BACKGROUND_COLOR", "00FFFFFF");
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            _pageTitle = e.Parameter as string;
            if (string.IsNullOrEmpty(_pageTitle))
            {
                Frame.GoBack();
                return;
            }

            PageTitle.Text = $"Editing: {_pageTitle.Replace('_', ' ')}";

            if (AppSettings.ConnectionBackend == ConnectionMethod.HttpClientProxy)
                _apiWorker = new HttpClientApiWorker();
            else
                _apiWorker = new WebView2ApiWorker();

            _ = LoadContentAsync();
        }

        bool isDragging = false;
        double initialX;
        GridLength leftColInitialWidth;

        private void Splitter_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            isDragging = true;
            initialX = e.GetCurrentPoint(this).Position.X;
            leftColInitialWidth = SplitGrid.ColumnDefinitions[0].Width;
            (sender as UIElement)?.CapturePointer(e.Pointer);
        }

        private void Splitter_PointerMoved(object sender, PointerRoutedEventArgs e)
        {
            if (!isDragging) return;

            var currentX = e.GetCurrentPoint(this).Position.X;
            var delta = currentX - initialX;

            var newWidth = Math.Max(300, leftColInitialWidth.Value + delta);
            SplitGrid.ColumnDefinitions[0].Width = new GridLength(newWidth, GridUnitType.Pixel);
        }

        private void Splitter_PointerReleased(object sender, PointerRoutedEventArgs e)
        {
            isDragging = false;
            (sender as UIElement)?.ReleasePointerCapture(e.Pointer);
        }

        private void Splitter_PointerEntered(object sender, PointerRoutedEventArgs e)
        {
            Window.Current.CoreWindow.PointerCursor =
                new CoreCursor(CoreCursorType.SizeWestEast, 0);
        }

        private void Splitter_PointerExited(object sender, PointerRoutedEventArgs e)
        {
            Window.Current.CoreWindow.PointerCursor =
                new CoreCursor(CoreCursorType.Arrow, 0);
        }

        private async Task LoadContentAsync()
        {
            try
            {
                await _apiWorker.InitializeAsync();
                string url = $"{AppSettings.ApiEndpoint}?action=query&prop=revisions&titles={Uri.EscapeDataString(_pageTitle)}&rvprop=content&format=json";
                string json = await _apiWorker.GetJsonFromApiAsync(url);

                var root = JObject.Parse(json);
                var page = root["query"]["pages"].First.First;
                var content = page["revisions"][0]["*"].ToString();

                _originalWikitext = content;
                WikitextEditor.Text = content;
            }
            catch (Exception ex)
            {
                LoadingText.Text = $"Error: {ex.Message}";
                WikitextEditor.Text = $"Failed to load page content. Please go back and try again.\n\nError: {ex.Message}";
                WikitextEditor.IsReadOnly = true;
                SaveButton.IsEnabled = false;
                PreviewButton.IsEnabled = false;
            }
            finally
            {
                LoadingOverlay.Visibility = Visibility.Collapsed;
            }
        }

        private async void PreviewButton_Click(object sender, RoutedEventArgs e)
        {
            PreviewButton.IsEnabled = false;
            PreviewPlaceholder.Text = "Generating preview...";
            PreviewWebView.Visibility = Visibility.Collapsed;

            try
            {
                var postData = new Dictionary<string, string>
                {
                    { "action", "parse" },
                    { "format", "json" },
                    { "title", _pageTitle },
                    { "text", WikitextEditor.Text },
                    { "prop", "text" },
                    { "disablelimitreport", "true" }
                };

                string json = await _apiWorker.PostAndGetJsonFromApiAsync(AppSettings.ApiEndpoint, postData);
                var root = JObject.Parse(json);
                string html = root["parse"]["text"]["*"].ToString();

                string fullHtml = $"<html><head><style>{ArticleProcessingService.GetCssForTheme()}</style></head><body>{html}</body></html>";
                await PreviewWebView.EnsureCoreWebView2Async();
                PreviewWebView.NavigateToString(fullHtml);

                PreviewPlaceholder.Visibility = Visibility.Collapsed;
                PreviewWebView.Visibility = Visibility.Visible;
            }
            catch (Exception ex)
            {
                PreviewPlaceholder.Text = $"Failed to generate preview: {ex.Message}";
                PreviewPlaceholder.Visibility = Visibility.Visible;
            }
            finally
            {
                PreviewButton.IsEnabled = true;
            }
        }

        private async void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            LoadingOverlay.Visibility = Visibility.Visible;
            LoadingText.Text = "Saving...";

            try
            {
                bool success = await AuthService.SavePageAsync(_pageTitle, WikitextEditor.Text, SummaryTextBox.Text);
                if (success)
                {
                    _originalWikitext = WikitextEditor.Text;
                    Frame.GoBack();
                }
            }
            catch (Exception ex)
            {
                LoadingOverlay.Visibility = Visibility.Collapsed;
                var dialog = new ContentDialog
                {
                    Title = "Save Failed",
                    Content = $"The page could not be saved. The server reported the following error:\n\n{ex.Message}",
                    CloseButtonText = "OK"
                };
                await dialog.ShowAsync();
            }
        }

        private async void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            if (WikitextEditor.Text != _originalWikitext)
            {
                var dialog = new ContentDialog
                {
                    Title = "Discard Changes?",
                    Content = "You have unsaved changes. Are you sure you want to discard them?",
                    PrimaryButtonText = "Discard",
                    CloseButtonText = "Cancel"
                };

                var result = await dialog.ShowAsync();
                if (result != ContentDialogResult.Primary)
                {
                    return;
                }
            }

            if (Frame.CanGoBack)
            {
                Frame.GoBack();
            }
        }

        private void Page_Unloaded(object sender, RoutedEventArgs e)
        {
            _apiWorker?.Dispose();
        }
    }
}