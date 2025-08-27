using System;
using System.Threading.Tasks;
using WikiViewer.Core;
using WikiViewer.Core.Enums;
using WikiViewer.Core.Models;
using WikiViewer.Core.Services;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;

namespace WikiViewer.Shared.Uwp.Controls
{
    public sealed partial class AddWikiPromptDialog : ContentDialog
    {
        private readonly Uri _targetUri;
        public WikiInstance NewWikiInstance { get; private set; }

        public AddWikiPromptDialog(Uri targetUri)
        {
            this.InitializeComponent();
            ApplyModernStyling();
            _targetUri = targetUri;
            this.Loaded += OnLoaded;
        }

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            await DetectWikiAsync();
        }

        private void ApplyModernStyling()
        {
#if UWP_1809
            this.Background = (Brush)
                Application.Current.Resources["SystemControlAcrylicWindowBrush"];
            this.CornerRadius = new CornerRadius(8);
#endif
        }

        private async Task DetectWikiAsync()
        {
            this.IsPrimaryButtonEnabled = false;
            var baseUrl = $"{_targetUri.Scheme}://{_targetUri.Host}";
            LoadingText.Text = "Finding best connection method...";

            var testResult = await ConnectionTesterService.FindWorkingMethodAndPathsAsync(
                baseUrl,
                App.ApiWorkerFactory
            );

            if (testResult.IsSuccess)
            {
                NewWikiInstance = new WikiInstance
                {
                    BaseUrl = baseUrl,
                    ScriptPath = testResult.Paths.ScriptPath,
                    ArticlePath = testResult.Paths.ArticlePath,
                    PreferredConnectionMethod = ConnectionMethod.Auto,
                };

                LoadingPanel.Visibility = Visibility.Collapsed;
                ResultPanel.Visibility = Visibility.Visible;
                ResultMessage.Text =
                    $"Successfully connected to '{_targetUri.Host}' using the '{testResult.Method}' method. Add to app?";
                WikiNameTextBox.Text = _targetUri.Host;
                WikiNameTextBox.Visibility = Visibility.Visible;
                this.IsPrimaryButtonEnabled = true;
            }
            else
            {
                ShowNonWikiResult();
            }
        }

        private void ShowNonWikiResult()
        {
            LoadingPanel.Visibility = Visibility.Collapsed;
            ResultPanel.Visibility = Visibility.Visible;
            ResultMessage.Text =
                "This link does not appear to be a compatible MediaWiki site. You can open it in your browser.";

            this.PrimaryButtonText = "";
        }

        private void ContentDialog_PrimaryButtonClick(
            ContentDialog sender,
            ContentDialogButtonClickEventArgs args
        )
        {
            if (NewWikiInstance != null && !string.IsNullOrWhiteSpace(WikiNameTextBox.Text))
            {
                NewWikiInstance.Name = WikiNameTextBox.Text.Trim();
            }
            else
            {
                args.Cancel = true;
                WikiNameTextBox.Focus(FocusState.Programmatic);
            }
        }

        private void ContentDialog_SecondaryButtonClick(
            ContentDialog sender,
            ContentDialogButtonClickEventArgs args
        )
        {
            NewWikiInstance = null;
        }
    }
}
