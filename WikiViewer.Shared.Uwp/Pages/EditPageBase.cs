using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using WikiViewer.Core.Interfaces;
using WikiViewer.Core.Models;
using WikiViewer.Core.Services;
using WikiViewer.Shared.Uwp.Services;
using Windows.UI;
using Windows.UI.Core;
using Windows.UI.Text;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Navigation;

namespace WikiViewer.Shared.Uwp.Pages
{
    public abstract class EditPageBase : Page
    {
        private WikiInstance _pageWikiContext;
        protected string _pageTitle;
        protected string _originalWikitext;
        private bool isDragging = false;
        private double initialX;
        private GridLength leftColInitialWidth;

        private readonly Dictionary<char, char> _bracketPairs = new Dictionary<char, char>
        {
            { '{', '}' },
            { '[', ']' },
            { '(', ')' },
        };
        private ITextRange _lastLeftBracket,
            _lastRightBracket;
        private Color? _lastOriginalBackgroundColor;

        protected abstract RichEditBox WikitextEditorTextBox { get; }
        protected abstract TextBlock PageTitleTextBlock { get; }
        protected abstract TextBlock LoadingTextBlock { get; }
        protected abstract Grid LoadingOverlayGrid { get; }
        protected abstract Grid SplitterGrid { get; }
        protected abstract Button SaveAppBarButton { get; }
        protected abstract Button PreviewAppBarButton { get; }
        protected abstract Task ShowPreview(string htmlContent);
        protected abstract void HidePreview(string placeholderText);

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

            ArticleNavigationParameter navParam = null;
            if (e.Parameter is string jsonParam)
            {
                try
                {
                    navParam =
                        Newtonsoft.Json.JsonConvert.DeserializeObject<ArticleNavigationParameter>(
                            jsonParam
                        );
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"[EditPageBase] Failed to deserialize navigation parameter: {ex.Message}"
                    );
                }
            }
            else if (e.Parameter is ArticleNavigationParameter directParam)
            {
                navParam = directParam;
            }

            if (navParam != null)
            {
                _pageWikiContext = WikiManager.GetWikiById(navParam.WikiId);
                _pageTitle = navParam.PageTitle;
            }

            if (_pageWikiContext == null || string.IsNullOrEmpty(_pageTitle))
            {
                Frame.GoBack();
                return;
            }

            PageTitleTextBlock.Text =
                $"Editing: {_pageTitle.Replace('_', ' ')} on {_pageWikiContext.Name}";
            this.FindParent<MainPageBase>()
                ?.SetPageTitle(
                    $"Editing: {_pageTitle.Replace('_', ' ')} on {_pageWikiContext.Name}"
                );
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
                var worker = SessionManager.GetAnonymousWorkerForWiki(_pageWikiContext);
                await worker.InitializeAsync(_pageWikiContext.BaseUrl);
                string url =
                    $"{_pageWikiContext.ApiEndpoint}?action=query&prop=revisions&titles={Uri.EscapeDataString(_pageTitle)}&rvprop=content&format=json";
                string json = await worker.GetJsonFromApiAsync(url);
                var root = JObject.Parse(json);
                var page = root["query"]["pages"].First.First;
                var content = page["revisions"][0]["*"].ToString();
                _originalWikitext = content;
                WikitextEditorTextBox.Document.SetText(TextSetOptions.None, _originalWikitext);
            }
            catch (Exception ex)
            {
                LoadingTextBlock.Text = $"Error: {ex.Message}";
                WikitextEditorTextBox.Document.SetText(
                    TextSetOptions.None,
                    $"Failed to load page content. Please go back and try again.\n\nError: {ex.Message}"
                );
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
                var account = AccountManager
                    .GetAccountsForWiki(_pageWikiContext.Id)
                    .FirstOrDefault(a => a.IsLoggedIn);
                IApiWorker worker;
                if (account != null && account.IsLoggedIn)
                {
                    worker = account.AuthenticatedApiWorker;
                }
                else
                {
                    worker = SessionManager.GetAnonymousWorkerForWiki(_pageWikiContext);
                }
                if (worker == null)
                {
                    throw new InvalidOperationException(
                        "Could not obtain an API worker for the preview."
                    );
                }
                WikitextEditorTextBox.Document.GetText(TextGetOptions.None, out var wikitext);
                string fullHtml = await WikitextParsingService.ParseWikitextToPreviewHtmlAsync(
                    wikitext,
                    _pageTitle,
                    _pageWikiContext,
                    worker
                );
                await ShowPreview(fullHtml);
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
            var dialog = new WikiViewer.Shared.Uwp.Controls.SaveDialog();
            var result = await dialog.ShowAsync();

            if (result != ContentDialogResult.Primary)
            {
                return;
            }

            LoadingOverlayGrid.Visibility = Visibility.Visible;
            LoadingTextBlock.Text = "Saving...";
            try
            {
                var account = AccountManager.GetAccountsForWiki(_pageWikiContext.Id).FirstOrDefault(a => a.IsLoggedIn);
                if (account == null) throw new InvalidOperationException("User must be logged in to save a page.");

                var authService = new AuthenticationService(account, _pageWikiContext, App.ApiWorkerFactory);
                WikitextEditorTextBox.Document.GetText(TextGetOptions.None, out var wikitext);

                bool success = await authService.SavePageAsync(
                    _pageTitle,
                    wikitext,
                    dialog.Summary,
                    dialog.IsMinorEdit
                );

                if (success)
                {
                    _originalWikitext = wikitext;
                    Frame.GoBack();
                }
            }
            catch (Exception ex)
            {
                LoadingOverlayGrid.Visibility = Visibility.Collapsed;
                var errorDialog = new ContentDialog
                {
                    Title = "Save Failed",
                    Content = $"The page could not be saved. The server reported the following error:\n\n{ex.Message}",
                    PrimaryButtonText = "OK",
                };
                await errorDialog.ShowAsync();
            }
        }

        protected async void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            WikitextEditorTextBox.Document.GetText(TextGetOptions.None, out var currentText);
            if (currentText != _originalWikitext)
            {
                var dialog = new ContentDialog
                {
                    Title = "Discard Changes?",
                    Content = "You have unsaved changes. Are you sure you want to discard them?",
                    PrimaryButtonText = "Discard",
                    SecondaryButtonText = "Cancel"
                };
                var result = await dialog.ShowAsync();
                if (result != ContentDialogResult.Primary)
                    return;
            }
            if (Frame.CanGoBack)
                Frame.GoBack();
        }

        protected void InsertWikitext(
            string prefix,
            string suffix = "",
            string defaultText = "",
            bool selectDefaultText = true
        )
        {
            var document = WikitextEditorTextBox.Document;
            var selection = document.Selection;
            if (selection == null)
                return;
            if (selection.Length > 0)
            {
                selection.Text = $"{prefix}{selection.Text}{suffix}";
            }
            else
            {
                var originalStartPosition = selection.StartPosition;
                selection.Text = $"{prefix}{defaultText}{suffix}";
                if (selectDefaultText && !string.IsNullOrEmpty(defaultText))
                {
                    selection.StartPosition = originalStartPosition + prefix.Length;
                    selection.EndPosition = selection.StartPosition + defaultText.Length;
                }
                else
                {
                    selection.StartPosition = originalStartPosition + prefix.Length;
                    selection.EndPosition = selection.StartPosition;
                }
            }
            WikitextEditorTextBox.Focus(FocusState.Programmatic);
        }

        protected void AttachEditorFunctionality()
        {
            if (WikitextEditorTextBox != null)
            {
                WikitextEditorTextBox.SelectionChanged += WikitextEditor_SelectionChanged;
            }
        }

        private void WikitextEditor_SelectionChanged(object sender, RoutedEventArgs e)
        {
            if (WikitextEditorTextBox.Document.Selection.Length > 0)
            {
                ClearBracketHighlight();
                return;
            }
            UpdateBracketHighlighting();
        }

        private void UpdateBracketHighlighting()
        {
            ClearBracketHighlight();
            var selection = WikitextEditorTextBox.Document.Selection;
            int caretPosition = selection.StartPosition;
            WikitextEditorTextBox.Document.GetText(TextGetOptions.None, out var text);

            if (caretPosition >= text.Length && caretPosition > 0)
            {
                caretPosition--;
            }
            if (string.IsNullOrEmpty(text))
                return;

            if (caretPosition > 0)
            {
                char charBefore = text[caretPosition - 1];
                if (_bracketPairs.ContainsValue(charBefore))
                {
                    char openingChar = _bracketPairs
                        .FirstOrDefault(kvp => kvp.Value == charBefore)
                        .Key;
                    int matchPos = FindMatchingBracket(
                        text,
                        caretPosition - 1,
                        openingChar,
                        charBefore,
                        searchBackwards: true
                    );
                    if (matchPos != -1)
                    {
                        HighlightPair(matchPos, caretPosition - 1);
                        return;
                    }
                }
            }

            if (caretPosition < text.Length)
            {
                char charAfter = text[caretPosition];
                if (_bracketPairs.ContainsKey(charAfter))
                {
                    char closingChar = _bracketPairs[charAfter];
                    int matchPos = FindMatchingBracket(
                        text,
                        caretPosition,
                        charAfter,
                        closingChar,
                        searchBackwards: false
                    );
                    if (matchPos != -1)
                    {
                        HighlightPair(caretPosition, matchPos);
                        return;
                    }
                }
            }
        }

        private int FindMatchingBracket(
            string text,
            int startPos,
            char openChar,
            char closeChar,
            bool searchBackwards
        )
        {
            int balance = searchBackwards ? -1 : 1;
            int step = searchBackwards ? -1 : 1;
            int currentPos = startPos + step;
            const int scanLimit = 10000;
            int scanCount = 0;

            while (currentPos >= 0 && currentPos < text.Length && scanCount < scanLimit)
            {
                char currentChar = text[currentPos];
                if (currentChar == openChar)
                    balance += searchBackwards ? -1 : 1;
                else if (currentChar == closeChar)
                    balance += searchBackwards ? 1 : -1;
                if (balance == 0)
                    return currentPos;
                currentPos += step;
                scanCount++;
            }
            return -1;
        }

        private void HighlightPair(int pos1, int pos2)
        {
            var document = WikitextEditorTextBox.Document;
            _lastLeftBracket = document.GetRange(Math.Min(pos1, pos2), Math.Min(pos1, pos2) + 1);
            _lastRightBracket = document.GetRange(Math.Max(pos1, pos2), Math.Max(pos1, pos2) + 1);
            _lastOriginalBackgroundColor = _lastLeftBracket.CharacterFormat.BackgroundColor;
            var highlightColor = (Color)Application.Current.Resources["SystemAccentColorLight2"];
            _lastLeftBracket.CharacterFormat.BackgroundColor = highlightColor;
            _lastRightBracket.CharacterFormat.BackgroundColor = highlightColor;
        }

        private void ClearBracketHighlight()
        {
            if (
                _lastLeftBracket != null
                && _lastRightBracket != null
                && _lastOriginalBackgroundColor.HasValue
            )
            {
                _lastLeftBracket.CharacterFormat.BackgroundColor =
                    _lastOriginalBackgroundColor.Value;
                _lastRightBracket.CharacterFormat.BackgroundColor =
                    _lastOriginalBackgroundColor.Value;
            }
            _lastLeftBracket = null;
            _lastRightBracket = null;
            _lastOriginalBackgroundColor = null;
        }
    }
}
