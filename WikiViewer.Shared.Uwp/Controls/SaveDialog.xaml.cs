using Windows.UI.Xaml.Controls;

namespace WikiViewer.Shared.Uwp.Controls
{
    public sealed partial class SaveDialog : ContentDialog
    {
        public string Summary { get; private set; }
        public bool IsMinorEdit { get; private set; }

        public SaveDialog()
        {
            this.InitializeComponent();
        }

        private void SaveDialog_PrimaryButtonClick(
            ContentDialog sender,
            ContentDialogButtonClickEventArgs args
        )
        {
            this.Summary = SummaryTextBox.Text;
            this.IsMinorEdit = MinorEditCheckBox.IsChecked ?? false;
        }
    }
}
