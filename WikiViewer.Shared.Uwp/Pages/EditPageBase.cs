using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using WikiViewer.Core;
using WikiViewer.Core.Enums;
using WikiViewer.Core.Interfaces;
using WikiViewer.Core.Services;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Navigation;

namespace WikiViewer.Shared.Uwp.Pages
{
    public abstract class EditPageBase : Page
    {
        protected string _pageTitle;
        protected string _originalWikitext;
        protected IApiWorker _apiWorker;
        private bool isDragging = false;
        private double initialX;
        private GridLength leftColInitialWidth;

        protected abstract TextBox WikitextEditorTextBox { get; }
        protected abstract TextBox SummaryTextBoxTextBox { get; }
        protected abstract TextBlock PageTitleTextBlock { get; }
        protected abstract TextBlock LoadingTextBlock { get; }
        protected abstract Grid LoadingOverlayGrid { get; }
        protected abstract Grid SplitterGrid { get; }
        protected abstract Button SaveAppBarButton { get; }
        protected abstract Button PreviewAppBarButton { get; }
        protected abstract void ShowPreview(string htmlContent);
        protected abstract void HidePreview(string placeholderText);

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            _pageTitle = e.Parameter as string;
            if (string.IsNullOrEmpty(_pageTitle))
            {
                Frame.GoBack();
                return;
            }

            PageTitleTextBlock.Text = $"Editing: {_pageTitle.Replace('_', ' ')}";

            if (AppSettings.ConnectionBackend == ConnectionMethod.HttpClientProxy)
            {
                _apiWorker = new HttpClientApiWorker();
            }
            else
            {
#if UWP_1703
                _apiWorker = (IApiWorker)
                    Activator.CreateInstance(
                        Type.GetType("_1703_UWP.Services.WebViewApiWorker, 1703 UWP")
                    );
#else
                _apiWorker = (IApiWorker)
                    Activator.CreateInstance(
                        Type.GetType("_1809_UWP.Services.WebView2ApiWorker, 1809 UWP")
                    );
#endif
            }
            _ = LoadContentAsync();
        }

        protected void Splitter_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            isDragging = true;
            initialX = e.GetCurrentPoint(this).Position.X;
            leftColInitialWidth = SplitterGrid.ColumnDefinitions[0].Width;
            (sender as UIElement)?.CapturePointer(e.Pointer);
        }

        protected void Splitter_PointerMoved(object sender, PointerRoutedEventArgs e)
        {
            if (!isDragging)
                return;
            var currentX = e.GetCurrentPoint(this).Position.X;
            var delta = currentX - initialX;
            var newWidth = Math.Max(300, leftColInitialWidth.Value + delta);
            SplitterGrid.ColumnDefinitions[0].Width = new GridLength(newWidth, GridUnitType.Pixel);
        }

        protected void Splitter_PointerReleased(object sender, PointerRoutedEventArgs e)
        {
            isDragging = false;
            (sender as UIElement)?.ReleasePointerCapture(e.Pointer);
        }

        protected void Splitter_PointerEntered(object sender, PointerRoutedEventArgs e) =>
            Window.Current.CoreWindow.PointerCursor = new CoreCursor(
                CoreCursorType.SizeWestEast,
                0
            );

        protected void Splitter_PointerExited(object sender, PointerRoutedEventArgs e) =>
            Window.Current.CoreWindow.PointerCursor = new CoreCursor(CoreCursorType.Arrow, 0);

        private async Task LoadContentAsync()
        {
            try
            {
                await _apiWorker.InitializeAsync();
                string url =
                    $"{AppSettings.ApiEndpoint}?action=query&prop=revisions&titles={Uri.EscapeDataString(_pageTitle)}&rvprop=content&format=json";
                string json = await _apiWorker.GetJsonFromApiAsync(url);
                var root = JObject.Parse(json);
                var page = root["query"]["pages"].First.First;
                var content = page["revisions"][0]["*"].ToString();
                _originalWikitext = content;
                WikitextEditorTextBox.Text = content;
            }
            catch (Exception ex)
            {
                LoadingTextBlock.Text = $"Error: {ex.Message}";
                WikitextEditorTextBox.Text =
                    $"Failed to load page content. Please go back and try again.\n\nError: {ex.Message}";
                WikitextEditorTextBox.IsReadOnly = true;
                SaveAppBarButton.IsEnabled = false;
                PreviewAppBarButton.IsEnabled = false;
            }
            finally
            {
                LoadingOverlayGrid.Visibility = Visibility.Collapsed;
            }
        }

        protected async void PreviewButton_Click(object sender, RoutedEventArgs e)
        {
            PreviewAppBarButton.IsEnabled = false;
            HidePreview("Generating preview...");
            try
            {
                var postData = new Dictionary<string, string>
                {
                    { "action", "parse" },
                    { "format", "json" },
                    { "title", _pageTitle },
                    { "text", WikitextEditorTextBox.Text },
                    { "prop", "text" },
                    { "disablelimitreport", "true" },
                };
                string json = await _apiWorker.PostAndGetJsonFromApiAsync(
                    AppSettings.ApiEndpoint,
                    postData
                );
                var root = JObject.Parse(json);
                string html = root["parse"]["text"]["*"].ToString();
                string fullHtml =
                    $"<html><head><style>{ArticleProcessingService.GetCssForTheme()}</style></head><body>{html}</body></html>";
                ShowPreview(fullHtml);
            }
            catch (Exception ex)
            {
                HidePreview($"Failed to generate preview: {ex.Message}");
            }
            finally
            {
                PreviewAppBarButton.IsEnabled = true;
            }
        }

        protected async void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            LoadingOverlayGrid.Visibility = Visibility.Visible;
            LoadingTextBlock.Text = "Saving...";
            try
            {
                bool success = await AuthService.SavePageAsync(
                    _pageTitle,
                    WikitextEditorTextBox.Text,
                    SummaryTextBoxTextBox.Text
                );
                if (success)
                {
                    _originalWikitext = WikitextEditorTextBox.Text;
                    Frame.GoBack();
                }
            }
            catch (Exception ex)
            {
                LoadingOverlayGrid.Visibility = Visibility.Collapsed;
                var dialog = new ContentDialog
                {
                    Title = "Save Failed",
                    Content =
                        $"The page could not be saved. The server reported the following error:\n\n{ex.Message}",
                    CloseButtonText = "OK",
                };
                await dialog.ShowAsync();
            }
        }

        protected async void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            if (WikitextEditorTextBox.Text != _originalWikitext)
            {
                var dialog = new ContentDialog
                {
                    Title = "Discard Changes?",
                    Content = "You have unsaved changes. Are you sure you want to discard them?",
                    PrimaryButtonText = "Discard",
                    CloseButtonText = "Cancel",
                };
                var result = await dialog.ShowAsync();
                if (result != ContentDialogResult.Primary)
                    return;
            }
            if (Frame.CanGoBack)
                Frame.GoBack();
        }

        protected void Page_Unloaded(object sender, RoutedEventArgs e)
        {
            _apiWorker?.Dispose();
        }
    }
}
