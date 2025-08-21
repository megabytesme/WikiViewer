using System;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Media.Imaging;

namespace WikiViewer.Shared.Uwp.Converters
{
    public class StringUrlToImageSourceConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is string imageUrl && !string.IsNullOrEmpty(imageUrl))
            {
#if UWP_1703
                try
                {
                    return new BitmapImage(new Uri(imageUrl));
                }
                catch
                {
                    return null;
                }
#endif
#if UWP_1809
                var imageUri = new Uri(imageUrl);

                if (imageUrl.EndsWith(".svg", StringComparison.OrdinalIgnoreCase))
                {
                    return new SvgImageSource(imageUri);
                }
                else
                {
                    return new BitmapImage(imageUri);
                }
#endif
            }
            return null;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }
}
