using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using WikiViewer.Core;
using WikiViewer.Core.Interfaces;
using WikiViewer.Core.Models;
using WikiViewer.Core.Services;
using WikiViewer.Uwp.Controls;
using WikiViewer.Uwp.Services;
using Windows.UI;
using Windows.UI.Core;
using Windows.UI.Text;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Navigation;

namespace WikiViewer.Uwp.Pages
{
    public abstract class EditPageBase : Page
    {
        private WikiInstance _pageWikiContext;
        protected string _pageTitle;
        protected string _originalWikitext;
        private readonly EditorPreviewSession _previewSession = new EditorPreviewSession();
        private bool _previewControlsReady;
        private int _previewRenderVersion;
        private bool _editorAttached;
        private readonly DispatcherTimer _previewTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(800) };
        private bool _previewBusy;
        private bool _previewQueued;
        private bool _previewActive;
        private bool _sourceLoaded;
        private string _lastPreviewSource;
        private bool isDragging = false;
        private double initialX;
        private GridLength leftColInitialWidth;
        private bool _isPreviewModeActiveInNarrow = false;
        private const double NarrowStateWidthTrigger = 800;
        private class WikitextPair
        {
            public string Name { get; }
            public System.Text.RegularExpressions.Regex OpenPattern { get; }
            public System.Text.RegularExpressions.Regex ClosePattern { get; }
            public string OpenString { get; }
            public string CloseString { get; }
            public bool IsRawContent { get; }

            public WikitextPair(string open, string close, bool isRaw = false)
            {
                Name = open;
                OpenPattern = new System.Text.RegularExpressions.Regex(
                    System.Text.RegularExpressions.Regex.Escape(open),
                    System.Text.RegularExpressions.RegexOptions.Compiled
                );
                ClosePattern = new System.Text.RegularExpressions.Regex(
                    System.Text.RegularExpressions.Regex.Escape(close),
                    System.Text.RegularExpressions.RegexOptions.Compiled
                );
                OpenString = open;
                CloseString = close;
                IsRawContent = isRaw;
            }

            public WikitextPair(
                string name,
                string openRegex,
                string closeRegex,
                string simpleOpen,
                string simpleClose,
                bool isRaw = false
            )
            {
                Name = name;
                OpenPattern = new System.Text.RegularExpressions.Regex(
                    openRegex,
                    System.Text.RegularExpressions.RegexOptions.Compiled
                );
                ClosePattern = new System.Text.RegularExpressions.Regex(
                    closeRegex,
                    System.Text.RegularExpressions.RegexOptions.Compiled
                );
                OpenString = simpleOpen;
                CloseString = simpleClose;
                IsRawContent = isRaw;
            }
        }

        private readonly List<WikitextPair> _wikitextPairs = new List<WikitextPair>
        {
            // Existing Table Rules
            new WikitextPair("table", @"^\{\|", @"^\|\}", "{|", "|}"),
            new WikitextPair("table_caption", @"^\|\+", "$", "|+", ""),
            new WikitextPair("table_row", @"^\|-", "$", "|-", ""),
            new WikitextPair("table_header", @"^!", "$", "!", ""),
            new WikitextPair("table_cell", @"^\|(?!})", "$", "|", ""),
            // Headings
            new WikitextPair("======", "======"),
            new WikitextPair("=====", "====="),
            new WikitextPair("====", "===="),
            new WikitextPair("===", "==="),
            new WikitextPair("==", "=="),
            // Standard Formatting
            new WikitextPair("'''''", "'''''"),
            new WikitextPair("'''", "'''"),
            new WikitextPair("''", "''"),
            // HTML-like tags
            new WikitextPair("ref", @"<ref\b[^>]*>", @"</ref>", "<ref>", "</ref>", isRaw: true),
            new WikitextPair("<nowiki>", "</nowiki>", isRaw: true),
            new WikitextPair("<code>", "</code>", isRaw: true),
            new WikitextPair("<pre>", "</pre>", isRaw: true),
            new WikitextPair("<ins>", "</ins>"),
            new WikitextPair("<del>", "</del>"),
            new WikitextPair("<sup>", "</sup>"),
            new WikitextPair("<sub>", "</sub>"),
            new WikitextPair("<small>", "</small>"),
            new WikitextPair("<blockquote>", "</blockquote>"),
            new WikitextPair("<u>", "</u>"),
            new WikitextPair("<s>", "</s>"),
            new WikitextPair("<big>", "</big>"),
            // Template and Link Brackets
            new WikitextPair("{{", "}}"),
            new WikitextPair("[[", "]]"),
            // Comments
            new WikitextPair("<!--", "-->"),
            // Single Brackets
            new WikitextPair("{", "}"),
            new WikitextPair("[", "]"),
            new WikitextPair("(", ")"),
            // Specialized Content Tags
            new WikitextPair(
                "syntaxhighlight",
                @"<syntaxhighlight\b[^>]*>",
                @"</syntaxhighlight>",
                "<syntaxhighlight>",
                "</syntaxhighlight>",
                isRaw: true
            ),
            new WikitextPair(
                "source",
                @"<source\b[^>]*>",
                @"</source>",
                "<source>",
                "</source>",
                isRaw: true
            ),
            new WikitextPair(
                "poem",
                @"<poem\b[^>]*>",
                @"</poem>",
                "<poem>",
                "</poem>",
                isRaw: true
            ),
            new WikitextPair(
                "score",
                @"<score\b[^>]*>",
                @"</score>",
                "<score>",
                "</score>",
                isRaw: true
            ),
            new WikitextPair(
                "hiero",
                @"<hiero\b[^>]*>",
                @"</hiero>",
                "<hiero>",
                "</hiero>",
                isRaw: true
            ),
            // Transclusion Control Tags
            new WikitextPair("<noinclude>", "</noinclude>"),
            new WikitextPair("<includeonly>", "</includeonly>"),
            new WikitextPair("<onlyinclude>", "</onlyinclude>"),
            // Other Content Blocks
            new WikitextPair("<gallery>", "</gallery>", isRaw: true),
            new WikitextPair("<math>", "</math>", isRaw: true),
        };

        private class TokenMatch
        {
            public int Position;
            public int Length;
            public bool IsOpening;
            public bool IsMatched;
            public int MatchPosition;
            public WikitextPair Pair { get; set; }
        }

        private const string WIKITEXT_PUNCTUATION = "[]{}'|#*=!<>:-";
        private ITextRange _lastLeftBracket,
            _lastRightBracket;
        private readonly List<ITextRange> _lastUnmatchedRanges = new List<ITextRange>();
        private bool _isUpdatingText = false;
        private DispatcherTimer _highlightDebounceTimer;
        private string _lastHighlightText = null;
        private int _lastHighlightCaretPosition = -1;
        private CancellationTokenSource _highlightCts;

        protected abstract RichEditBox WikitextEditorTextBox { get; }
        protected abstract TextBlock PageTitleTextBlock { get; }
        protected abstract TextBlock LoadingTextBlock { get; }
        protected abstract Grid LoadingOverlayGrid { get; }
        protected abstract Grid SplitterGrid { get; }
        protected abstract Button SaveAppBarButton { get; }
        protected abstract Button PreviewAppBarButton { get; }
        protected abstract Task ShowWebPreviewAsync(string htmlContent, int version);
        protected bool IsPreviewRenderCurrent(int version) => version == _previewRenderVersion;
        protected abstract FrameworkElement WebPreviewHost { get; }
        protected abstract FrameworkElement PreviewPaneHost { get; }
        protected abstract NativeArticleView NativePreviewHost { get; }
        protected abstract ToggleSwitch PreviewModeControl { get; }
        protected abstract TextBlock PreviewPlaceholderControl { get; }
        protected abstract TextBlock PreviewStatusControl { get; }

        public EditPageBase()
        {
            SizeChanged += Page_SizeChanged;
            _previewTimer.Tick += async (s, e) => { _previewTimer.Stop(); await GeneratePreviewAsync(true); };
            Loaded += (s, e) => { if (_previewControlsReady) { SetPreviewLayout(false); QueueNativePreview(); } };
        }

        private void Page_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (!_previewControlsReady || (e.PreviousSize.Width < NarrowStateWidthTrigger) == (e.NewSize.Width < NarrowStateWidthTrigger)) return;
            SetPreviewLayout(false);
        }

        private void SetPreviewLayout(bool showPreview)
        {
            bool narrow = ActualWidth < NarrowStateWidthTrigger;
            _isPreviewModeActiveInNarrow = narrow && showPreview;
            WikitextEditorTextBox.Visibility = _isPreviewModeActiveInNarrow ? Visibility.Collapsed : Visibility.Visible;
            PreviewPaneHost.Visibility = !narrow || showPreview ? Visibility.Visible : Visibility.Collapsed;
            if (PreviewAppBarButton is AppBarButton button)
            {
                button.Label = _isPreviewModeActiveInNarrow ? "Edit" : "Preview";
                button.Icon = new SymbolIcon(_isPreviewModeActiveInNarrow ? Symbol.Edit : Symbol.View);
            }
        }

        private void ResetPreviewPaneVisually()
        {
            if (!PreviewModeControl.IsOn) HidePreview("Click 'Preview' to see a rendering of your changes.");
        }

        protected void InitializePreviewControls()
        {
            _previewControlsReady = true;
            NativePreviewHost.ContentMargin = new Thickness(12, 12, 12, 24);
            NativePreviewHost.UnsupportedContentMessage = "Some content is available in Web preview. Switch the preview renderer to Web.";
            PreviewModeControl.IsOn = AppSettings.UseNativeReaderByDefault;
            SetPreviewLayout(false);
        }

        protected void HidePreview(string text)
        {
            _previewRenderVersion++;
            NativePreviewHost.ClearArticle();
            NativePreviewHost.Visibility = WebPreviewHost.Visibility = Visibility.Collapsed;
            PreviewPlaceholderControl.Text = text;
            PreviewPlaceholderControl.Visibility = Visibility.Visible;
        }

        protected async void PreviewMode_Toggled(object sender, RoutedEventArgs e)
        {
            if (!_previewControlsReady) return;
            _previewTimer.Stop();
            _previewQueued = false;
            _previewSession.InvalidateRequests();
            if (PreviewModeControl.IsOn) QueueNativePreview();
            if (_previewSession.Current != null) await RenderPreviewAsync(_previewSession.Current);
            else HidePreview(PreviewModeControl.IsOn ? "Preparing live preview..." : "Click Preview to generate a Web preview.");
        }

        private void QueueNativePreview()
        {
            if (!_previewControlsReady || !_previewActive || !_sourceLoaded || !PreviewModeControl.IsOn) return;
            _previewQueued = true;
            _previewTimer.Stop();
            _previewTimer.Start();
            PreviewStatusControl.Text = "Live preview will update after typing pauses.";
        }

        private async Task RenderPreviewAsync(ArticlePreview preview)
        {
            int version = ++_previewRenderVersion;
            PreviewPlaceholderControl.Visibility = Visibility.Collapsed;
            PreviewStatusControl.Text = _previewSession.IsStale ? (PreviewModeControl.IsOn ? "Updating live preview..." : "Preview is out of date. Click Preview to update it.") : "Preview of your unsaved text";
            NativePreviewHost.ClearArticle();
            if (PreviewModeControl.IsOn)
            {
                NativePreviewHost.Visibility = Visibility.Visible;
                WebPreviewHost.Visibility = Visibility.Collapsed;
                try
                {
                    await NativePreviewHost.ShowArticleAsync(preview.Content, new Uri(_pageWikiContext.GetWikiPageUrl(_pageTitle)),
                        async uri => { await Windows.System.Launcher.LaunchUriAsync(uri); },
                        (url, token) => MediaCacheService.GetLocalUriAsync(url, _pageWikiContext, cancellationToken: token));
                    return;
                }
                catch (Exception ex)
                {
                    if (version != _previewRenderVersion) return;
                    HidePreview("Native preview could not display this content: " + ex.Message + ". Web preview is available using Preview.");
                    return;
                }
            }
            if (version != _previewRenderVersion) return;
            NativePreviewHost.Visibility = Visibility.Collapsed;
            WebPreviewHost.Visibility = Visibility.Visible;
            try
            {
                if (string.IsNullOrEmpty(preview.WebHtml)) await WikitextParsingService.BuildWebPreviewHtmlAsync(preview, _pageTitle, _pageWikiContext);
                if (version != _previewRenderVersion) return;
                if (!EditorPreviewPolicy.CanDisplayWeb(preview.WebHtml))
                {
                    HidePreview("This page exceeds the Web preview's 2 MB limit. Switch to Native for an unlimited, live preview.");
                    return;
                }
                if (version == _previewRenderVersion) await ShowWebPreviewAsync(preview.WebHtml, version);
            }
            catch (Exception ex) { if (version == _previewRenderVersion) HidePreview("Failed to display Web preview: " + ex.Message); }
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            _previewActive = true;
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
                AttachEditorFunctionality();
                UpdateBracketHighlighting();
                _sourceLoaded = !WikitextEditorTextBox.IsReadOnly;
                QueueNativePreview();
            }
        }

        protected void AttachEditorFunctionality()
        {
            if (WikitextEditorTextBox == null || _editorAttached)
                return;
            _editorAttached = true;
            _highlightDebounceTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(150),
            };
            _highlightDebounceTimer.Tick += HighlightDebounceTimer_Tick;
            WikitextEditorTextBox.TextChanged += Editor_TextChanged;
            WikitextEditorTextBox.SelectionChanged += Editor_SelectionChanged;
            Window.Current.CoreWindow.CharacterReceived += CoreWindow_CharacterReceived;
            Window.Current.CoreWindow.KeyDown += CoreWindow_KeyDown;
        }

        protected override void OnNavigatingFrom(NavigatingCancelEventArgs e)
        {
            base.OnNavigatingFrom(e);
            if (e.Cancel) return;
            _previewActive = false;
            _previewQueued = false;
            _previewTimer.Stop();
            _previewSession.Clear();
            _previewRenderVersion++;
            NativePreviewHost.ClearArticle();
            _highlightDebounceTimer?.Stop();
            _highlightCts?.Cancel();
            WikitextEditorTextBox.TextChanged -= Editor_TextChanged;
            WikitextEditorTextBox.SelectionChanged -= Editor_SelectionChanged;
            Window.Current.CoreWindow.CharacterReceived -= CoreWindow_CharacterReceived;
            Window.Current.CoreWindow.KeyDown -= CoreWindow_KeyDown;
        }

        private void CoreWindow_CharacterReceived(CoreWindow sender, CharacterReceivedEventArgs e)
        {
            if (FocusManager.GetFocusedElement() != WikitextEditorTextBox || _isUpdatingText)
                return;

            var document = WikitextEditorTextBox.Document;
            var selection = document.Selection;
            char typedChar = (char)e.KeyCode;

            if (selection.Length > 0 || selection.StartPosition < 1)
            {
                HandleAutoCompletion();
                _highlightDebounceTimer.Start();
                return;
            }

            if (WIKITEXT_PUNCTUATION.Contains(typedChar)) { }
            else
            {
                var typedCharRange = document.GetRange(
                    selection.StartPosition - 1,
                    selection.StartPosition
                );

                if (typedCharRange.CharacterFormat.BackgroundColor != Colors.Transparent)
                {
                    try
                    {
                        _isUpdatingText = true;
                        document.BeginUndoGroup();

                        var cleanFormat = typedCharRange.CharacterFormat.GetClone();
                        cleanFormat.BackgroundColor = Colors.Transparent;
                        typedCharRange.CharacterFormat = cleanFormat;
                    }
                    finally
                    {
                        document.EndUndoGroup();
                        _isUpdatingText = false;
                    }
                }
            }

            HandleAutoCompletion();
            _highlightDebounceTimer.Start();
        }

        private void CoreWindow_KeyDown(CoreWindow sender, KeyEventArgs e)
        {
            if (FocusManager.GetFocusedElement() != WikitextEditorTextBox)
                return;

            var coreWindow = Window.Current.CoreWindow;
            var ctrlState = coreWindow.GetKeyState(Windows.System.VirtualKey.Control);
            bool isCtrlDown = (ctrlState & CoreVirtualKeyStates.Down) == CoreVirtualKeyStates.Down;

            if (isCtrlDown)
            {
                if (e.VirtualKey == Windows.System.VirtualKey.Z)
                {
                    var document = WikitextEditorTextBox.Document;
                    if (document.CanUndo())
                    {
                        _isUpdatingText = true;
                        e.Handled = true;

                        document.GetText(TextGetOptions.None, out string initialText);
                        while (document.CanUndo())
                        {
                            document.Undo();
                            document.GetText(TextGetOptions.None, out string newText);
                            if (initialText != newText)
                            {
                                break;
                            }
                        }
                    }
                    return;
                }

                if (e.VirtualKey == Windows.System.VirtualKey.Y)
                {
                    var document = WikitextEditorTextBox.Document;
                    if (document.CanRedo())
                    {
                        _isUpdatingText = true;
                        e.Handled = true;

                        document.GetText(TextGetOptions.None, out string initialText);
                        while (document.CanRedo())
                        {
                            document.Redo();
                            document.GetText(TextGetOptions.None, out string newText);
                            if (initialText != newText)
                            {
                                break;
                            }
                        }
                    }
                    return;
                }
            }

            if (e.VirtualKey == Windows.System.VirtualKey.Back && !_isUpdatingText)
            {
                var document = WikitextEditorTextBox.Document;
                var selection = document.Selection;
                if (selection.Length > 0)
                    return;
                var caretPos = selection.StartPosition;
                document.GetText(TextGetOptions.None, out var text);
                if (caretPos == 0 || caretPos >= text.Length)
                    return;
                foreach (var pair in _wikitextPairs)
                {
                    if (
                        caretPos >= pair.OpenString.Length
                        && text.Substring(caretPos - pair.OpenString.Length, pair.OpenString.Length)
                            == pair.OpenString
                        && caretPos + pair.CloseString.Length <= text.Length
                        && text.Substring(caretPos, pair.CloseString.Length) == pair.CloseString
                    )
                    {
                        try
                        {
                            _isUpdatingText = true;
                            document.BeginUndoGroup();
                            var rangeToDelete = document.GetRange(
                                caretPos - pair.OpenString.Length,
                                caretPos + pair.CloseString.Length
                            );
                            rangeToDelete.Text = string.Empty;
                        }
                        finally
                        {
                            document.EndUndoGroup();
                        }
                        e.Handled = true;
                        return;
                    }
                }
            }
        }

        private async void HighlightDebounceTimer_Tick(object sender, object e)
        {
            _highlightDebounceTimer.Stop();
            if (_isUpdatingText)
                return;

            WikitextEditorTextBox.Document.GetText(TextGetOptions.None, out var currentText);
            var currentCaretPosition = WikitextEditorTextBox.Document.Selection.StartPosition;

            if (
                currentText == _lastHighlightText
                && currentCaretPosition == _lastHighlightCaretPosition
            )
            {
                return;
            }

            _highlightCts?.Cancel();
            _highlightCts = new CancellationTokenSource();
            var cancellationToken = _highlightCts.Token;

            try
            {
                var allTokens = await Task.Run(
                    () => ScanDocumentForAllTokens(currentText),
                    cancellationToken
                );

                cancellationToken.ThrowIfCancellationRequested();

                _isUpdatingText = true;
                WikitextEditorTextBox.Document.BeginUndoGroup();

                ClearBracketHighlight();
                var defaultBackgroundColor = Colors.Transparent;

                foreach (var range in _lastUnmatchedRanges)
                {
                    if (range.StartPosition < currentText.Length)
                    {
                        range.CharacterFormat.BackgroundColor = defaultBackgroundColor;
                    }
                }
                _lastUnmatchedRanges.Clear();

                if (!string.IsNullOrEmpty(currentText))
                {
                    foreach (var t in allTokens.Where(t => !t.IsMatched))
                    {
                        var range = PaintToken(t.Position, t.Length, Colors.Red);
                        if (range != null)
                            _lastUnmatchedRanges.Add(range);
                    }

                    var tokenUnderCaret = allTokens.FirstOrDefault(t =>
                        (
                            currentCaretPosition > t.Position
                            && currentCaretPosition <= t.Position + t.Length
                        ) || (currentCaretPosition == t.Position)
                    );

                    if (tokenUnderCaret != null && tokenUnderCaret.IsMatched)
                    {
                        var accentColor = (Color)
                            Application.Current.Resources["SystemAccentColorLight2"];
                        _lastLeftBracket = PaintToken(
                            tokenUnderCaret.Position,
                            tokenUnderCaret.Length,
                            accentColor
                        );
                        var matchingToken = allTokens.FirstOrDefault(t =>
                            t.Position == tokenUnderCaret.MatchPosition
                        );
                        if (matchingToken != null)
                        {
                            _lastRightBracket = PaintToken(
                                matchingToken.Position,
                                matchingToken.Length,
                                accentColor
                            );
                        }
                    }
                }

                _lastHighlightText = currentText;
                _lastHighlightCaretPosition = currentCaretPosition;
            }
            catch (OperationCanceledException)
            {
                Debug.WriteLine("[Highlighting] Scan was cancelled by newer edit.");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Highlighting] Error during background processing: {ex.Message}");
            }
            finally
            {
                WikitextEditorTextBox.Document.EndUndoGroup();
                _isUpdatingText = false;
            }
        }

        private void Editor_TextChanged(object sender, RoutedEventArgs e)
        {
            WikitextEditorTextBox.Document.GetText(TextGetOptions.None, out var previewSource);
            _previewSession.SourceChanged(previewSource);
            if (_previewSession.IsStale) PreviewStatusControl.Text = "Preview is out of date. Click Preview to update it.";
            if (!string.Equals(_lastPreviewSource, previewSource, StringComparison.Ordinal))
            {
                _lastPreviewSource = previewSource;
                QueueNativePreview();
            }
            if (_isUpdatingText)
            {
                _isUpdatingText = false;
                return;
            }
            _highlightDebounceTimer.Start();
        }

        private void Editor_SelectionChanged(object sender, RoutedEventArgs e)
        {
            _highlightDebounceTimer.Start();
        }

        private void HandleAutoCompletion()
        {
            var document = WikitextEditorTextBox.Document;
            var selection = document.Selection;
            var caretPos = selection.StartPosition;
            if (selection.Length > 0 || caretPos == 0)
                return;
            document.GetText(TextGetOptions.None, out var text);

            var openTokens = _wikitextPairs
                .Where(p => p.OpenString != p.CloseString)
                .Select(p => p.OpenString)
                .OrderByDescending(s => s.Length)
                .ToList();

            foreach (var openToken in openTokens)
            {
                if (
                    caretPos >= openToken.Length
                    && text.Substring(caretPos - openToken.Length, openToken.Length) == openToken
                )
                {
                    var pair = _wikitextPairs.First(p => p.OpenString == openToken);
                    string requiredCloser = pair.CloseString;
                    string textAfterCursor =
                        text.Length > caretPos ? text.Substring(caretPos) : string.Empty;

                    if (!textAfterCursor.StartsWith(requiredCloser))
                    {
                        int matchingChars = 0;
                        while (
                            matchingChars < requiredCloser.Length
                            && matchingChars < textAfterCursor.Length
                            && requiredCloser[matchingChars] == textAfterCursor[matchingChars]
                        )
                        {
                            matchingChars++;
                        }
                        string missingCloserPart = requiredCloser.Substring(matchingChars);

                        if (!string.IsNullOrEmpty(missingCloserPart))
                        {
                            try
                            {
                                _isUpdatingText = true;
                                document.BeginUndoGroup();
                                selection.TypeText(missingCloserPart);
                                selection.StartPosition = caretPos;
                                selection.EndPosition = caretPos;
                            }
                            finally
                            {
                                document.EndUndoGroup();
                            }
                        }
                    }
                    return;
                }
            }
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

                var matchingToken = allTokens.FirstOrDefault(t =>
                    t.Position == tokenUnderCaret.MatchPosition
                );
                if (matchingToken != null)
                {
                    _lastRightBracket = PaintToken(
                        matchingToken.Position,
                        matchingToken.Length,
                        accentColor
                    );
                }
            }
        }

        private class GroupInfo
        {
            public WikitextPair Pair { get; set; }
            public bool IsOpeningPattern { get; set; }
        }

        private static System.Text.RegularExpressions.Regex _masterScannerRegex;
        private static Dictionary<string, GroupInfo> _groupNameToInfoMap;
        private string _lastScannedText;
        private List<TokenMatch> _lastScanResult;

        private List<TokenMatch> ScanDocumentForAllTokens(string text)
        {
            if (text == _lastScannedText)
            {
                return _lastScanResult;
            }

            var stopwatch = Stopwatch.StartNew();
            InitializeMasterScanner();

            var matches = new List<TokenMatch>();
            var stack = new Stack<TokenMatch>();

            foreach (
                System.Text.RegularExpressions.Match match in _masterScannerRegex.Matches(text)
            )
            {
                string successfulGroupName = null;
                foreach (string groupName in _groupNameToInfoMap.Keys)
                {
                    if (match.Groups[groupName].Success)
                    {
                        successfulGroupName = groupName;
                        break;
                    }
                }

                if (successfulGroupName == null)
                    continue;

                var groupInfo = _groupNameToInfoMap[successfulGroupName];
                var pair = groupInfo.Pair;
                bool isOpeningToken = groupInfo.IsOpeningPattern;

                if (pair.OpenPattern.ToString() == pair.ClosePattern.ToString())
                {
                    if (stack.Any() && stack.Peek().Pair.Name == pair.Name)
                    {
                        isOpeningToken = false;
                    }
                    else
                    {
                        isOpeningToken = true;
                    }
                }

                if (isOpeningToken && match.Value.TrimEnd().EndsWith("/>"))
                {
                    var token = new TokenMatch
                    {
                        Position = match.Index,
                        Length = match.Length,
                        IsOpening = true,
                        IsMatched = true,
                        MatchPosition = match.Index,
                        Pair = pair,
                    };
                    matches.Add(token);
                }
                else if (isOpeningToken)
                {
                    var token = new TokenMatch
                    {
                        Position = match.Index,
                        Length = match.Length,
                        IsOpening = true,
                        Pair = pair,
                    };
                    stack.Push(token);
                    matches.Add(token);
                }
                else
                {
                    if (stack.Any() && stack.Peek().Pair.Name == pair.Name)
                    {
                        var opener = stack.Pop();
                        var token = new TokenMatch
                        {
                            Position = match.Index,
                            Length = match.Length,
                            IsOpening = false,
                            Pair = pair,
                            IsMatched = true,
                            MatchPosition = opener.Position,
                        };
                        opener.IsMatched = true;
                        opener.MatchPosition = match.Index;
                        matches.Add(token);
                    }
                    else
                    {
                        matches.Add(
                            new TokenMatch
                            {
                                Position = match.Index,
                                Length = match.Length,
                                IsOpening = false,
                                Pair = pair,
                                IsMatched = false,
                            }
                        );
                    }
                }
            }

            foreach (var unclosed in stack)
            {
                unclosed.IsMatched = false;
            }

            stopwatch.Stop();
            Debug.WriteLine(
                $"[Highlighting] Single-threaded scan completed in {stopwatch.ElapsedMilliseconds}ms."
            );

            _lastScannedText = text;
            _lastScanResult = matches;

            return matches;
        }

        private void InitializeMasterScanner()
        {
            if (_masterScannerRegex != null)
                return;

            var patternBuilder = new System.Text.StringBuilder();
            _groupNameToInfoMap = new Dictionary<string, GroupInfo>();
            int groupCounter = 0;

            foreach (var pair in _wikitextPairs)
            {
                string openGroupName = $"p{groupCounter++}";
                patternBuilder.Append($"(?<{openGroupName}>{pair.OpenPattern})|");
                _groupNameToInfoMap[openGroupName] = new GroupInfo
                {
                    Pair = pair,
                    IsOpeningPattern = true,
                };

                if (pair.OpenPattern.ToString() != pair.ClosePattern.ToString())
                {
                    string closeGroupName = $"p{groupCounter++}";
                    patternBuilder.Append($"(?<{closeGroupName}>{pair.ClosePattern})|");
                    _groupNameToInfoMap[closeGroupName] = new GroupInfo
                    {
                        Pair = pair,
                        IsOpeningPattern = false,
                    };
                }
            }
            patternBuilder.Length--;

            _masterScannerRegex = new System.Text.RegularExpressions.Regex(
                patternBuilder.ToString(),
                System.Text.RegularExpressions.RegexOptions.Compiled
            );
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
            document.GetText(TextGetOptions.None, out string fullText);
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

        protected async void PreviewButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isPreviewModeActiveInNarrow)
            {
                ResetPreviewPaneVisually();
                SetPreviewLayout(false);
                return;
            }
            WikitextEditorTextBox.Document.GetText(TextGetOptions.None, out var source);
            _previewSession.SourceChanged(source);
            SetPreviewLayout(true);
            if (PreviewModeControl.IsOn)
            {
                await GeneratePreviewAsync(true);
                return;
            }
            await GeneratePreviewAsync(false);
        }

        private async Task GeneratePreviewAsync(bool native)
        {
            if (!_previewActive || !_sourceLoaded || PreviewModeControl.IsOn != native) return;
            if (_previewBusy) { if (native) _previewQueued = true; return; }
            _previewTimer.Stop();
            _previewQueued = false;
            WikitextEditorTextBox.Document.GetText(TextGetOptions.None, out var source);
            _previewSession.SourceChanged(source);
            if (_previewSession.Current != null && !_previewSession.IsStale)
            {
                if (!native) await RenderPreviewAsync(_previewSession.Current);
                return;
            }
            var request = _previewSession.Begin(source, _pageTitle);
            _previewBusy = true;
            if (!native) PreviewAppBarButton.IsEnabled = false;
            PreviewStatusControl.Text = native ? "Updating live preview..." : "Generating Web preview...";

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
                var preview = await WikitextParsingService.ParseEditorPreviewAsync(
                    request.Source,
                    request.Title,
                    _pageWikiContext,
                    worker,
                    buildWebHtml: !native
                );
                if (_previewActive && PreviewModeControl.IsOn == native && _previewSession.Accept(request, preview)) await RenderPreviewAsync(preview);
                else if (_previewSession.IsLatest(request) && !native) PreviewStatusControl.Text = "Text changed while the preview was generated. Click Preview again.";
            }
            catch (Exception ex)
            {
                if (_previewSession.IsLatest(request))
                {
                    PreviewStatusControl.Text = $"Preview could not update: {ex.Message}";
                }
            }
            finally
            {
                _previewBusy = false;
                if (_previewActive) PreviewAppBarButton.IsEnabled = true;
                if (_previewQueued) QueueNativePreview();
            }
        }

        protected async void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new WikiViewer.Uwp.Controls.SaveDialog();
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
