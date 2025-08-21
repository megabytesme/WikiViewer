using WikiViewer.Shared.Uwp.Pages;
using Windows.UI.Xaml.Controls;

namespace _1703_UWP.Pages
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
    }
}
