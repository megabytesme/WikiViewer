using WikiViewer.Shared.Uwp.Pages;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace _1703_UWP.Pages
{
    public sealed partial class EditPage : EditPageBase
    {
        public EditPage() => this.InitializeComponent();

        protected override TextBox WikitextEditorTextBox => WikitextEditor;
        protected override TextBox SummaryTextBoxTextBox => SummaryTextBox;
        protected override TextBlock PageTitleTextBlock => new TextBlock();
        protected override TextBlock LoadingTextBlock => LoadingText;
        protected override Grid LoadingOverlayGrid => LoadingOverlay;
        protected override Grid SplitterGrid => SplitGrid;
        protected override Button SaveAppBarButton => SaveButton;
        protected override Button PreviewAppBarButton => PreviewButton;

        protected override void ShowPreview(string htmlContent)
        {
            PreviewWebView.NavigateToString(htmlContent);
            PreviewPlaceholder.Visibility = Windows.UI.Xaml.Visibility.Collapsed;
            PreviewWebView.Visibility = Windows.UI.Xaml.Visibility.Visible;
        }

        protected override void HidePreview(string placeholderText)
        {
            PreviewPlaceholder.Text = placeholderText;
            PreviewWebView.Visibility = Windows.UI.Xaml.Visibility.Collapsed;
            PreviewPlaceholder.Visibility = Windows.UI.Xaml.Visibility.Visible;
        }

        // --- Text Formatting ---
        private void InsertBold_Click(object sender, RoutedEventArgs e) =>
            InsertWikitext("'''", "'''", "bold text");

        private void InsertItalic_Click(object sender, RoutedEventArgs e) =>
            InsertWikitext("''", "''", "italic text");

        private void InsertBoldItalic_Click(object sender, RoutedEventArgs e) =>
            InsertWikitext("'''''", "'''''", "bold and italic text");

        private void InsertUnderline_Click(object sender, RoutedEventArgs e) =>
            InsertWikitext("<u>", "</u>", "underlined text");

        private void InsertStrikethrough_Click(object sender, RoutedEventArgs e) =>
            InsertWikitext("<s>", "</s>", "strikethrough text");

        private void InsertSuperscript_Click(object sender, RoutedEventArgs e) =>
            InsertWikitext("<sup>", "</sup>", "superscript");

        private void InsertSubscript_Click(object sender, RoutedEventArgs e) =>
            InsertWikitext("<sub>", "</sub>", "subscript");

        private void InsertSmall_Click(object sender, RoutedEventArgs e) =>
            InsertWikitext("<small>", "</small>", "small text");

        private void InsertBig_Click(object sender, RoutedEventArgs e) =>
            InsertWikitext("<span style=\"font-size:larger;\">", "</span>", "big text");

        private void InsertBlockquote_Click(object sender, RoutedEventArgs e) =>
            InsertWikitext("<blockquote>", "</blockquote>", "quoted text");

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

        // --- Structure (Line-based) ---
        private void InsertHeading2_Click(object sender, RoutedEventArgs e) =>
            InsertWikitext("\n== ", " ==\n", "Heading");

        private void InsertHeading3_Click(object sender, RoutedEventArgs e) =>
            InsertWikitext("\n=== ", " ===\n", "Heading");

        private void InsertHeading4_Click(object sender, RoutedEventArgs e) =>
            InsertWikitext("\n==== ", " ====\n", "Heading");

        private void InsertHorizontalRule_Click(object sender, RoutedEventArgs e) =>
            InsertWikitext("\n----\n");

        private void InsertIndentedListItem_Click(object sender, RoutedEventArgs e) =>
            InsertWikitext("\n: ", "", "Indented text");

        private void InsertBulletedListItem_Click(object sender, RoutedEventArgs e) =>
            InsertWikitext("\n* ", "", "List item");

        private void InsertNumberedListItem_Click(object sender, RoutedEventArgs e) =>
            InsertWikitext("\n# ", "", "List item");
    }
}
