using System;
using System.Diagnostics;
using WikiViewer.Core.Models;
using WikiViewer.Core.Services;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media.Imaging;

namespace WikiViewer.Shared.Uwp.Controls
{
    public sealed partial class ImageViewerDialog : ContentDialog
    {
        public ImageMetadata Metadata { get; }
        private readonly WikiInstance _wikiContext;

        public ImageViewerDialog(ImageMetadata metadata, WikiInstance wikiContext)
        {
            this.InitializeComponent();
            this.Metadata = metadata;

            _wikiContext = wikiContext;

            this.Loaded += ImageViewerDialog_Loaded;

            this.Closing += (s, e) =>
            {
                RasterImageView.ImageOpened -= Image_LoadComplete;
                RasterImageView.ImageFailed -= Image_LoadFailed;
                SvgWebView.NavigationCompleted -= WebView_LoadComplete;
                SvgWebView.NavigationFailed -= WebView_LoadFailed;
            };
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
                Debug.WriteLine($"[ImageViewerDialog] Requesting local URI for '{remoteImageUrl}'");
                string localUriString = await MediaCacheService.GetLocalUriAsync(
                    remoteImageUrl,
                    _wikiContext
                );

                if (string.IsNullOrEmpty(localUriString))
                {
                    throw new Exception("Failed to download or cache the image.");
                }

                Debug.WriteLine($"[ImageViewerDialog] Got local URI: '{localUriString}'");
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
            System.Diagnostics.Debug.WriteLine($"[ImageViewerDialog] ERROR: {message}");
        }
    }
}
