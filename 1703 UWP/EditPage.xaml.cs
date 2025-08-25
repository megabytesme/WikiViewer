using System;
using System.Threading.Tasks;
using WikiViewer.Core;
using WikiViewer.Shared.Uwp.Controls;
using WikiViewer.Shared.Uwp.Pages;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Input;

namespace _1703_UWP.Pages
{
    public sealed partial class EditPage : EditPageBase
    {
        public EditPage()
        {
            this.InitializeComponent();
            AttachEditorFunctionality();
            LoadEditorThemePreference();
            WikitextEditor.IsRightTapEnabled = true;
            WikitextEditor.ContextMenuOpening += (s, e) => e.Handled = true;
            WikitextEditor.AddHandler(
                UIElement.RightTappedEvent,
                new RightTappedEventHandler(WikitextEditor_ContextMenu),
                handledEventsToo: true
            );

            WikitextEditor.AddHandler(
                UIElement.HoldingEvent,
                new HoldingEventHandler(WikitextEditor_ContextMenu),
                handledEventsToo: true
            );
        }

        protected override RichEditBox WikitextEditorTextBox => WikitextEditor;
        protected override TextBlock PageTitleTextBlock => new TextBlock();
        protected override TextBlock LoadingTextBlock => LoadingText;
        protected override Grid LoadingOverlayGrid => LoadingOverlay;
        protected override Grid SplitterGrid => SplitGrid;
        protected override Button SaveAppBarButton => SaveButton;
        protected override Button PreviewAppBarButton => PreviewButton;

        protected override async Task ShowPreview(string htmlContent)
        {
            PreviewWebView.NavigateToString(htmlContent);
            PreviewPlaceholder.Visibility = Visibility.Collapsed;
            PreviewWebView.Visibility = Visibility.Visible;
            await Task.CompletedTask;
        }

        protected override void HidePreview(string placeholderText)
        {
            PreviewPlaceholder.Text = placeholderText;
            PreviewWebView.Visibility = Visibility.Collapsed;
            PreviewPlaceholder.Visibility = Visibility.Visible;
        }

        protected void LightModeToggle_Click(object sender, RoutedEventArgs e)
        {
            bool isLight = (sender as ToggleMenuFlyoutItem)?.IsChecked ?? false;

            LightModeToggleMenuBarItem.IsChecked = isLight;
            LightModeToggleMenuItem.IsChecked = isLight;

            UpdateEditorTheme(isLight);
        }

        private void UpdateEditorTheme(bool isLight)
        {
            if (WikitextEditorTextBox == null)
                return;

            if (isLight)
            {
                WikitextEditorTextBox.RequestedTheme = ElementTheme.Light;
            }
            else
            {
                WikitextEditorTextBox.RequestedTheme = ElementTheme.Dark;
            }

            AppSettings.EditorTheme = isLight ? ElementTheme.Light : ElementTheme.Default;
        }

        private void LoadEditorThemePreference()
        {
            var savedTheme = AppSettings.EditorTheme;
            bool isLight = (savedTheme == ElementTheme.Light);

            if (LightModeToggleMenuBarItem != null)
                LightModeToggleMenuBarItem.IsChecked = isLight;
            if (LightModeToggleMenuItem != null)
                LightModeToggleMenuItem.IsChecked = isLight;

            UpdateEditorTheme(isLight);
        }

        // --- Text Formatting ---
        private void InsertBold_Click(object sender, RoutedEventArgs e) =>
            InsertWikitext("'''", "'''", "bold text");

        private void InsertItalic_Click(object sender, RoutedEventArgs e) =>
            InsertWikitext("''", "''", "italic text");

        private void InsertBoldItalic_Click(object sender, RoutedEventArgs e) =>
            InsertWikitext("'''''", "'''''", "bold & italic text");

        private void InsertUnderline_Click(object sender, RoutedEventArgs e) =>
            InsertWikitext("<u>", "</u>", "underlined text");

        private void InsertStrikethrough_Click(object sender, RoutedEventArgs e) =>
            InsertWikitext("<s>", "</s>", "strikethrough text");

        private void InsertInserted_Click(object sender, RoutedEventArgs e) =>
            InsertWikitext("<ins>", "</ins>", "inserted text");

        private void InsertDeleted_Click(object sender, RoutedEventArgs e) =>
            InsertWikitext("<del>", "</del>", "deleted text");

        private void InsertSuperscript_Click(object sender, RoutedEventArgs e) =>
            InsertWikitext("<sup>", "</sup>", "superscript");

        private void InsertSubscript_Click(object sender, RoutedEventArgs e) =>
            InsertWikitext("<sub>", "</sub>", "subscript");

        private void InsertSmall_Click(object sender, RoutedEventArgs e) =>
            InsertWikitext("<small>", "</small>", "small text");

        private void InsertBig_Click(object sender, RoutedEventArgs e) =>
            InsertWikitext("<span style=\"font-size:larger;\">", "</span>", "big text");

        // --- Image Formatting ---
        private void InsertImageBasic_Click(object sender, RoutedEventArgs e) =>
            InsertWikitext("[[File:", "]]", "Example.jpg");

        private void InsertImageThumbnail_Click(object sender, RoutedEventArgs e) =>
            InsertWikitext("[[File:", "|thumb|Caption text]]", "Example.jpg");

        private void InsertImageAlignLeft_Click(object sender, RoutedEventArgs e) =>
            InsertWikitext("[[File:", "|thumb|left|Caption text]]", "Example.jpg");

        private void InsertAlignCenter_Click(object sender, RoutedEventArgs e) =>
            InsertWikitext("[[File:", "|thumb|center|Caption text]]", "Example.jpg");

        private void InsertAlignRight_Click(object sender, RoutedEventArgs e) =>
            InsertWikitext("[[File:", "|thumb|right|Caption text]]", "Example.jpg");

        private void InsertImageLinked_Click(object sender, RoutedEventArgs e) =>
            InsertWikitext("[[File:", "|link=Target Page]]", "Example.jpg");

        private void InsertImageAlt_Click(object sender, RoutedEventArgs e) =>
            InsertWikitext("[[File:", "|alt=Alternative text for screen readers]]", "Example.jpg");

        private void InsertMediaLink_Click(object sender, RoutedEventArgs e) =>
            InsertWikitext("[[Media:", "]]", "Example.ogg");

        // --- Links & References ---
        private void InsertInternalLink_Click(object sender, RoutedEventArgs e) =>
            InsertWikitext("[[", "]]", "Page title");

        private void InsertExternalLink_Click(object sender, RoutedEventArgs e) =>
            InsertWikitext("[", "]", "http://www.example.com link text");

        private void InsertRedirect_Click(object sender, RoutedEventArgs e) =>
            InsertWikitext("#REDIRECT [[", "]]", "Target Page");

        private void InsertReference_Click(object sender, RoutedEventArgs e) =>
            InsertWikitext("<ref>", "</ref>", "reference text");

        private void InsertCite_Click(object sender, RoutedEventArgs e) =>
            InsertWikitext("<cite>", "</cite>", "https://www.example.com");

        // --- Signatures ---
        private void InsertSignature3_Click(object sender, RoutedEventArgs e) =>
            InsertWikitext("~~~");

        private void InsertSignature4_Click(object sender, RoutedEventArgs e) =>
            InsertWikitext("~~~~");

        private void InsertSignature5_Click(object sender, RoutedEventArgs e) =>
            InsertWikitext("~~~~~");

        // --- Advanced ---
        private void InsertNoWiki_Click(object sender, RoutedEventArgs e) =>
            InsertWikitext("<nowiki>", "</nowiki>", "[[Not a link]]");

        private void InsertCode_Click(object sender, RoutedEventArgs e) =>
            InsertWikitext("<code>", "</code>", "Source Code");

        private void InsertPreformatted_Click(object sender, RoutedEventArgs e) =>
            InsertWikitext("<pre>", "</pre>", "preformatted text");

        private void InsertPreformattedStyled_Click(object sender, RoutedEventArgs e) =>
            InsertWikitext("<pre style=\"\">", "</pre>", "styled preformatted text");

        private void InsertComment_Click(object sender, RoutedEventArgs e) =>
            InsertWikitext("<!-- ", " -->", "comment");

        // --- Structure (Line-based) ---
        private void InsertHeading2_Click(object sender, RoutedEventArgs e) =>
            InsertWikitext("\n== ", " ==\n", "Heading");

        private void InsertHeading3_Click(object sender, RoutedEventArgs e) =>
            InsertWikitext("\n=== ", " ===\n", "Heading");

        private void InsertHeading4_Click(object sender, RoutedEventArgs e) =>
            InsertWikitext("\n==== ", " ====\n", "Heading");

        private void InsertLineBreak_Click(object sender, RoutedEventArgs e) =>
            InsertWikitext("<br />");

        private void InsertHorizontalRule_Click(object sender, RoutedEventArgs e) =>
            InsertWikitext("\n----\n");

        private void InsertBlockquote_Click(object sender, RoutedEventArgs e) =>
            InsertWikitext("\n<blockquote>", "</blockquote>\n", "quoted text");

        private void InsertIndentedListItem_Click(object sender, RoutedEventArgs e) =>
            InsertWikitext("\n: ", "", "Indented text");

        private void InsertBulletedListItem_Click(object sender, RoutedEventArgs e) =>
            InsertWikitext("\n* ", "", "List item");

        private void InsertNumberedListItem_Click(object sender, RoutedEventArgs e) =>
            InsertWikitext("\n# ", "", "List item");

        private async void InsertSpecialCharacter_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new SymbolPickerDialog();
            var result = await dialog.ShowAsync();
            if (
                result == ContentDialogResult.Primary
                && !string.IsNullOrEmpty(dialog.SelectedEntity)
            )
            {
                InsertWikitext(dialog.SelectedEntity);
            }
        }

        private void WikitextEditor_ContextMenu(object sender, RightTappedRoutedEventArgs e)
        {
            FlyoutBase.ShowAttachedFlyout((FrameworkElement)sender);
        }

        private void WikitextEditor_ContextMenu(object sender, HoldingRoutedEventArgs e)
        {
            FlyoutBase.ShowAttachedFlyout((FrameworkElement)sender);
        }

        private void Cut_Click(object sender, RoutedEventArgs e)
        {
            WikitextEditor.Document.Selection.Cut();
        }

        private void Copy_Click(object sender, RoutedEventArgs e)
        {
            WikitextEditor.Document.Selection.Copy();
        }

        private void Paste_Click(object sender, RoutedEventArgs e)
        {
            WikitextEditor.Document.Selection.Paste(0);
        }
    }
}
