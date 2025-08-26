using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices.WindowsRuntime;
using WikiViewer.Core.Models;
using WikiViewer.Core.Services;
using WikiViewer.Shared.Uwp.Pages;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.Storage.Streams;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Imaging;

namespace WikiViewer.Shared.Uwp.Controls
{
    public sealed partial class ImageViewerDialog : ContentDialog
    {
        public ImageMetadata Metadata { get; }
        private readonly WikiInstance _wikiContext;
        private DataTransferManager _dataTransferManager;
        private StorageFile _localImageFile;

        public ImageViewerDialog(ImageMetadata metadata, WikiInstance wikiContext)
        {
            this.InitializeComponent();
            this.Metadata = metadata;
            _wikiContext = wikiContext;

            ApplyModernStyling();

            this.Loaded += ImageViewerDialog_Loaded;

            this.Closing += (s, e) =>
            {
                if (_dataTransferManager != null)
                {
                    _dataTransferManager.DataRequested -= OnDataRequested;
                }

                RasterImageView.ImageOpened -= Image_LoadComplete;
                RasterImageView.ImageFailed -= Image_LoadFailed;
                SvgWebView.NavigationCompleted -= WebView_LoadComplete;
                SvgWebView.NavigationFailed -= WebView_LoadFailed;
            };
        }

        private void ApplyModernStyling()
        {
#if UWP_1809
            this.Background = (Brush)
                Application.Current.Resources["SystemControlAcrylicWindowBrush"];
            this.CornerRadius = new CornerRadius(8);

            var buttonStyle = new Style(typeof(Button));
            buttonStyle.Setters.Add(new Setter(Button.CornerRadiusProperty, new CornerRadius(4)));

            ImageContainerBorder.CornerRadius = new CornerRadius(6);

            if (this.FindName("ShareButton") is Button shareButton)
            {
                shareButton.Style = buttonStyle;
            }
            if (this.FindName("DownloadButton") is Button downloadButton)
            {
                downloadButton.Style = buttonStyle;
            }
#endif
        }

        private async void ImageViewerDialog_Loaded(object sender, RoutedEventArgs e)
        {
            var remoteImageUrl = Metadata.FullImageUrl;
            if (string.IsNullOrEmpty(remoteImageUrl) || _wikiContext == null)
            {
                ShowError("Image URL is missing or invalid.");
                return;
            }

            try
            {
                string localUriString = await MediaCacheService.GetLocalUriAsync(
                    remoteImageUrl,
                    _wikiContext
                );

                if (string.IsNullOrEmpty(localUriString))
                {
                    throw new Exception("Failed to download or cache the image.");
                }

                _localImageFile = await StorageFile.GetFileFromApplicationUriAsync(
                    new Uri(localUriString)
                );

                ShareButton.IsEnabled = true;
                DownloadButton.IsEnabled = true;

                var imageUri = new Uri(localUriString);
                bool isSvg = imageUri.AbsolutePath.EndsWith(
                    ".svg",
                    StringComparison.OrdinalIgnoreCase
                );

                RasterImageView.ImageOpened += Image_LoadComplete;
                RasterImageView.ImageFailed += Image_LoadFailed;
                SvgWebView.NavigationCompleted += WebView_LoadComplete;
                SvgWebView.NavigationFailed += WebView_LoadFailed;

                if (isSvg)
                {
#if UWP_1809
                    RasterImageView.Source = new SvgImageSource(imageUri);
#else
                    ImageScrollViewer.Visibility = Visibility.Collapsed;
                    SvgWebView.Visibility = Visibility.Visible;
                    SvgWebView.Navigate(imageUri);
#endif
                }
                else
                {
                    RasterImageView.Source = new BitmapImage(imageUri);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ImageViewerDialog] CRITICAL FAILURE: {ex.Message}");
                ShowError(
                    $"Failed to load image. It may be an unsupported format or blocked.\n\nError: {ex.Message}"
                );
            }
        }

        private void ShareButton_Click(object sender, RoutedEventArgs e)
        {
            if (_localImageFile == null)
                return;

            _dataTransferManager = DataTransferManager.GetForCurrentView();
            _dataTransferManager.DataRequested += OnDataRequested;
            DataTransferManager.ShowShareUI();
        }

        private void OnDataRequested(DataTransferManager sender, DataRequestedEventArgs args)
        {
            if (_localImageFile == null)
            {
                args.Request.FailWithDisplayText("Image data is not available to share.");
                return;
            }

            var request = args.Request;
            request.Data.Properties.Title = Metadata.PageTitle ?? "Image";
            request.Data.Properties.Description = Metadata.Description ?? "Shared from WikiViewer";

            request.Data.SetStorageItems(new[] { _localImageFile });
            request.Data.SetWebLink(new Uri(_wikiContext.GetWikiPageUrl(Metadata.PageTitle)));

            _dataTransferManager.DataRequested -= OnDataRequested;
        }

        private async void DownloadButton_Click(object sender, RoutedEventArgs e)
        {
            if (_localImageFile == null)
                return;

            var savePicker = new FileSavePicker
            {
                SuggestedStartLocation = PickerLocationId.PicturesLibrary,
                SuggestedFileName = string.Join(
                    "_",
                    (Metadata.PageTitle ?? "image").Split(Path.GetInvalidFileNameChars())
                ),
            };

            string extension = Path.GetExtension(_localImageFile.Name);
            savePicker.FileTypeChoices.Add("Image", new[] { extension });

            StorageFile destinationFile = await savePicker.PickSaveFileAsync();
            if (destinationFile != null)
            {
                await _localImageFile.CopyAndReplaceAsync(destinationFile);
            }
        }

        private void Image_LoadComplete(object sender, RoutedEventArgs e)
        {
            LoadingIndicator.IsActive = false;
        }

        private void Image_LoadFailed(object sender, ExceptionRoutedEventArgs e)
        {
            ShowError($"The image could not be displayed.\n\nError: {e.ErrorMessage}");
        }

        private void WebView_LoadComplete(WebView sender, WebViewNavigationCompletedEventArgs args)
        {
            if (args.IsSuccess)
            {
                LoadingIndicator.IsActive = false;
            }
            else
            {
                ShowError(
                    $"The SVG image could not be displayed in the WebView.\n\nError: {args.WebErrorStatus}"
                );
            }
        }

        private void WebView_LoadFailed(object sender, WebViewNavigationFailedEventArgs e)
        {
            ShowError(
                $"The SVG image could not be displayed in the WebView.\n\nError: {e.WebErrorStatus}"
            );
        }

        private void ShowError(string message)
        {
            LoadingIndicator.IsActive = false;
            ShareButton.IsEnabled = false;
            DownloadButton.IsEnabled = false;
            System.Diagnostics.Debug.WriteLine($"[ImageViewerDialog] ERROR: {message}");
        }
    }
}
