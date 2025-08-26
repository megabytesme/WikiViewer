using System;
using System.Threading.Tasks;
using WikiViewer.Core;
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

            var tempWikiForDetection = new WikiInstance
            {
                BaseUrl = baseUrl,
                PreferredConnectionMethod = AppSettings.DefaultConnectionMethod,
            };

            LoadingText.Text =
                $"Analyzing link using '{tempWikiForDetection.PreferredConnectionMethod}' method...";

            using (var detectorWorker = App.ApiWorkerFactory.CreateApiWorker(tempWikiForDetection))
            {
                try
                {
                    await detectorWorker.InitializeAsync(baseUrl);
                    var detectedPaths = await WikiPathDetectorService.DetectPathsAsync(
                        baseUrl,
                        detectorWorker
                    );

                    if (detectedPaths.WasDetectedSuccessfully)
                    {
                        NewWikiInstance = new WikiInstance
                        {
                            BaseUrl = baseUrl,
                            ScriptPath = detectedPaths.ScriptPath,
                            ArticlePath = detectedPaths.ArticlePath,
                            PreferredConnectionMethod = AppSettings.DefaultConnectionMethod,
                        };

                        LoadingPanel.Visibility = Visibility.Collapsed;
                        ResultPanel.Visibility = Visibility.Visible;
                        ResultMessage.Text =
                            $"This link appears to be a MediaWiki site. Would you like to add '{_targetUri.Host}' to the app?";
                        WikiNameTextBox.Text = _targetUri.Host;
                        WikiNameTextBox.Visibility = Visibility.Visible;
                        this.IsPrimaryButtonEnabled = true;
                    }
                    else
                    {
                        ShowNonWikiResult();
                    }
                }
                catch (NeedsUserVerificationException)
                {
                    LoadingPanel.Visibility = Visibility.Collapsed;
                    ResultPanel.Visibility = Visibility.Visible;
                    ResultMessage.Text =
                        $"Detection failed: This site is protected by a security check (like Cloudflare).\n\nPlease go to Settings, change the 'Default connection method for new wikis' to 'Proxy', and try again.";

                    this.PrimaryButtonText = "";
                    this.SecondaryButtonText = "Close";
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"[AddWikiPrompt] Detection failed with general exception: {ex.Message}"
                    );
                    ShowNonWikiResult();
                }
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
