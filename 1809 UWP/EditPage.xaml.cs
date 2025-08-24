using System;
using WikiViewer.Shared.Uwp.Pages;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace _1809_UWP.Pages
{
    public sealed partial class EditPage : EditPageBase
    {
        public EditPage() => this.InitializeComponent();

        protected override TextBox WikitextEditorTextBox => WikitextEditor;
        protected override TextBox SummaryTextBoxTextBox => SummaryTextBox;
        protected override TextBlock PageTitleTextBlock => PageTitle;
        protected override TextBlock LoadingTextBlock => LoadingText;
        protected override Grid LoadingOverlayGrid => LoadingOverlay;
        protected override Grid SplitterGrid => SplitGrid;
        protected override Button SaveAppBarButton => SaveButton;
        protected override Button PreviewAppBarButton => PreviewButton;

        protected override async void ShowPreview(string htmlContent)
        {
            await PreviewWebView.EnsureCoreWebView2Async();
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

        private void InsertBold_Click(object sender, RoutedEventArgs e) =>
            InsertWikitext("'''", "'''", "bold text");

        private void InsertItalic_Click(object sender, RoutedEventArgs e) =>
            InsertWikitext("''", "''", "italic text");

        private void InsertInternalLink_Click(object sender, RoutedEventArgs e) =>
            InsertWikitext("[[", "]]", "Page title");

        private void InsertExternalLink_Click(object sender, RoutedEventArgs e) =>
            InsertWikitext("[", "]", "http://www.example.com link text");

        private void InsertGenericTemplate_Click(object sender, RoutedEventArgs e) =>
            InsertWikitext("{{", "}}", "template");

        private void InsertCiteWeb_Click(object sender, RoutedEventArgs e) =>
            InsertWikitext("{{Cite web|url=|title=|access-date=}}");

        private void InsertUnreferenced_Click(object sender, RoutedEventArgs e) =>
            InsertWikitext("{{Unreferenced|date=}}");

        private void InsertReference_Click(object sender, RoutedEventArgs e) =>
            InsertWikitext("<ref>", "</ref>", "reference text");

        private void InsertHeading2_Click(object sender, RoutedEventArgs e) =>
            InsertWikitext("\n== ", " ==\n", "Heading");

        private void InsertBulletedListItem_Click(object sender, RoutedEventArgs e) =>
            InsertWikitext("\n* ", "", "List item");
    }
}
