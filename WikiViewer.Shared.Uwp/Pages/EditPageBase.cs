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

        private readonly List<(string Open, string Close)> _wikitextPairs = new List<(
            string,
            string
        )>
        {
            ("======", "======"),
            ("=====", "====="),
            ("====", "===="),
            ("===", "==="),
            ("==", "=="),
            ("'''''", "'''''"),
            ("'''", "'''"),
            ("''", "''"),
            ("{{", "}}"),
            ("[[", "]]"),
            ("<!--", "-->"),
            ("<ins>", "</ins>"),
            ("<del>", "</del>"),
            ("<sup>", "</sup>"),
            ("<sub>", "</sub>"),
            ("<small>", "</small>"),
            ("<span style=\"font-size:larger;\">", "</span>"),
            ("<ref>", "</ref>"),
            ("<cite>", "</cite>"),
            ("<nowiki>", "</nowiki>"),
            ("<code>", "</code>"),
            ("<pre>", "</pre>"),
            ("<blockquote>", "</blockquote>"),
            ("<u>", "</u>"),
            ("<s>", "</s>"),
            ("{", "}"),
            ("[", "]"),
            ("(", ")"),
        };

        private ITextRange _lastLeftBracket,
            _lastRightBracket;
        private readonly List<ITextRange> _lastUnmatchedRanges = new List<ITextRange>();
        private DispatcherTimer _highlightTimer;

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

        private class TokenMatch
        {
            public int Position;
            public int Length;
            public bool IsOpening;
            public bool IsMatched;
            public int MatchPosition;
            public (string Open, string Close) Pair;
        }

        protected void AttachEditorFunctionality()
        {
            if (WikitextEditorTextBox == null)
                return;

            if (_highlightTimer == null)
            {
                _highlightTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
                _highlightTimer.Tick += (sender, args) =>
                {
                    _highlightTimer.Stop();
                    UpdateBracketHighlighting();
                };
            }

            WikitextEditorTextBox.SelectionChanged += (s, e) =>
            {
                _highlightTimer.Stop();
                _highlightTimer.Start();
            };

            WikitextEditorTextBox.TextChanged += (s, e) =>
            {
                _highlightTimer.Stop();
                _highlightTimer.Start();
            };
        }

        private void UpdateBracketHighlighting()
        {
            ClearBracketHighlight();

            var defaultBackgroundColor = Colors.Transparent;

            WikitextEditorTextBox.Document.GetText(TextGetOptions.None, out var text);

            foreach (var range in _lastUnmatchedRanges)
            {
                if (range.StartPosition < text.Length)
                {
                    range.CharacterFormat.BackgroundColor = defaultBackgroundColor;
                }
            }
            _lastUnmatchedRanges.Clear();

            if (string.IsNullOrEmpty(text))
                return;

            var allTokens = ScanDocumentForAllTokens(text);

            foreach (var t in allTokens.Where(t => !t.IsMatched))
            {
                var range = PaintToken(t.Position, t.Length, Colors.Red);
                if (range != null)
                    _lastUnmatchedRanges.Add(range);
            }

            var caretPos = WikitextEditorTextBox.Document.Selection.StartPosition;
            if (caretPos > text.Length)
                caretPos = text.Length;

            var tokenUnderCaret = allTokens.FirstOrDefault(t =>
                (caretPos > t.Position && caretPos <= t.Position + t.Length)
                || (caretPos == t.Position)
            );

            if (tokenUnderCaret != null && tokenUnderCaret.IsMatched)
            {
                var accentColor = (Color)Application.Current.Resources["SystemAccentColorLight2"];

                _lastLeftBracket = PaintToken(
                    tokenUnderCaret.Position,
                    tokenUnderCaret.Length,
                    accentColor
                );

                var matchLength = tokenUnderCaret.IsOpening
                    ? tokenUnderCaret.Pair.Close.Length
                    : tokenUnderCaret.Pair.Open.Length;
                _lastRightBracket = PaintToken(
                    tokenUnderCaret.MatchPosition,
                    matchLength,
                    accentColor
                );
            }
        }

        private List<TokenMatch> ScanDocumentForAllTokens(string text)
        {
            var allKnownTokens = _wikitextPairs
                .SelectMany(p => new[] { p.Open, p.Close })
                .Distinct()
                .OrderByDescending(t => t.Length)
                .ToList();

            var matches = new List<TokenMatch>();
            var stack = new Stack<TokenMatch>();
            int index = 0;

            while (index < text.Length)
            {
                string matchedToken = null;

                foreach (var tokenStr in allKnownTokens)
                {
                    if (
                        index + tokenStr.Length <= text.Length
                        && text.Substring(index, tokenStr.Length) == tokenStr
                    )
                    {
                        matchedToken = tokenStr;
                        break;
                    }
                }

                if (matchedToken != null)
                {
                    var pair = _wikitextPairs.First(p =>
                        p.Open == matchedToken || p.Close == matchedToken
                    );
                    bool isOpening = matchedToken == pair.Open;

                    if (pair.Open == pair.Close)
                    {
                        isOpening = !(stack.Count > 0 && stack.Peek().Pair.Open == matchedToken);
                    }

                    if (IsTokenAllowedHere(text, index, matchedToken, isOpening))
                    {
                        var token = new TokenMatch
                        {
                            Position = index,
                            Length = matchedToken.Length,
                            IsOpening = isOpening,
                            Pair = pair,
                        };

                        if (isOpening)
                        {
                            stack.Push(token);
                        }
                        else if (stack.Count > 0 && stack.Peek().Pair.Open == pair.Open)
                        {
                            var opener = stack.Pop();
                            opener.IsMatched = true;
                            opener.MatchPosition = index;
                            token.IsMatched = true;
                            token.MatchPosition = opener.Position;
                        }
                        matches.Add(token);
                    }
                    index += matchedToken.Length;
                }
                else
                {
                    index++;
                }
            }
            return matches;
        }

        private bool IsTokenAllowedHere(string text, int index, string token, bool isOpening)
        {
            if (token.StartsWith("=") && isOpening)
            {
                return index == 0 || text[index - 1] == '\n' || text[index - 1] == '\r';
            }

            return true;
        }

        private void ClearBracketHighlight()
        {
            var defaultBackgroundColor = Colors.Transparent;
            if (_lastLeftBracket != null)
                _lastLeftBracket.CharacterFormat.BackgroundColor = defaultBackgroundColor;
            if (_lastRightBracket != null)
                _lastRightBracket.CharacterFormat.BackgroundColor = defaultBackgroundColor;

            _lastLeftBracket = null;
            _lastRightBracket = null;
        }

        private ITextRange PaintToken(int pos, int length, Color color)
        {
            var document = WikitextEditorTextBox.Document;
            string fullText;
            document.GetText(TextGetOptions.None, out fullText);
            if (pos + length > fullText.Length)
                return null;
            var range = document.GetRange(pos, pos + length);
            range.CharacterFormat.BackgroundColor = color;
            return range;
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
                UpdateBracketHighlighting();
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
                IApiWorker worker =
                    account?.AuthenticatedApiWorker
                    ?? SessionManager.GetAnonymousWorkerForWiki(_pageWikiContext);

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
            var dialogResult = await dialog.ShowAsync();
            if (dialogResult != ContentDialogResult.Primary)
                return;

            LoadingOverlayGrid.Visibility = Visibility.Visible;
            LoadingTextBlock.Text = "Saving...";
            try
            {
                WikitextEditorTextBox.Document.GetText(TextGetOptions.None, out var wikitext);
                bool saveSuccess = false;
                var account = AccountManager
                    .GetAccountsForWiki(_pageWikiContext.Id)
                    .FirstOrDefault(a => a.IsLoggedIn);

                if (account != null)
                {
                    var authService = new AuthenticationService(
                        account,
                        _pageWikiContext,
                        App.ApiWorkerFactory
                    );
                    saveSuccess = await authService.SavePageAsync(
                        _pageTitle,
                        wikitext,
                        dialog.Summary,
                        dialog.IsMinorEdit
                    );
                }
                else
                {
                    var anonWorker = SessionManager.GetAnonymousWorkerForWiki(_pageWikiContext);
                    await anonWorker.InitializeAsync(_pageWikiContext.BaseUrl);
                    var tokenJson = await anonWorker.GetJsonFromApiAsync(
                        $"{_pageWikiContext.ApiEndpoint}?action=query&meta=tokens&type=csrf&format=json"
                    );
                    var token = JObject
                        .Parse(tokenJson)
                        ?["query"]?["tokens"]?["csrftoken"]?.ToString();
                    var postData = new Dictionary<string, string>
                    {
                        { "action", "edit" },
                        { "format", "json" },
                        { "title", _pageTitle },
                        { "text", wikitext },
                        { "summary", dialog.Summary },
                        { "minor", dialog.IsMinorEdit ? "true" : "false" },
                        { "token", token },
                    };
                    var resultJson = await anonWorker.PostAndGetJsonFromApiAsync(
                        _pageWikiContext.ApiEndpoint,
                        postData
                    );
                    var parsed = JObject.Parse(resultJson);
                    saveSuccess = parsed?["edit"]?["result"]?.ToString() == "Success";
                }

                if (saveSuccess)
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
                    Content =
                        $"The page could not be saved. The server reported the following error:\n\n{ex.Message}",
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
                    SecondaryButtonText = "Cancel",
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
                }
            }
            WikitextEditorTextBox.Focus(FocusState.Programmatic);
        }
    }
}
