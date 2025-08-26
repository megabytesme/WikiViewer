using System;
using WikiViewer.Core.Models;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media.Imaging;

namespace WikiViewer.Shared.Uwp.Controls
{
    public sealed partial class ImageViewerDialog : ContentDialog
    {
        public ImageMetadata Metadata { get; }

        public ImageViewerDialog(ImageMetadata metadata)
        {
            this.InitializeComponent();
            this.Metadata = metadata;
            this.Loaded += ImageViewerDialog_Loaded;

            RasterImageView.ImageOpened += (s, e) => LoadingIndicator.IsActive = false;
            RasterImageView.ImageFailed += (s, e) => LoadingIndicator.IsActive = false;
            SvgWebView.NavigationCompleted += (s, e) => LoadingIndicator.IsActive = false;
        }

        private void ImageViewerDialog_Loaded(object sender, RoutedEventArgs e)
        {
            var imageUrl = Metadata.FullImageUrl;
            if (string.IsNullOrEmpty(imageUrl))
            {
                LoadingIndicator.IsActive = false;
                return;
            }

            var imageUri = new Uri(imageUrl);
            bool isSvg = imageUri.AbsolutePath.EndsWith(".svg", StringComparison.OrdinalIgnoreCase);

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
    }
}