using System;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Media.Imaging;

namespace _1809_UWP
{
    public class StringUrlToImageSourceConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is string imageUrl && !string.IsNullOrEmpty(imageUrl))
            {
                var imageUri = new Uri(imageUrl);

                if (imageUrl.EndsWith(".svg", StringComparison.OrdinalIgnoreCase))
                {
                    return new SvgImageSource(imageUri);
                }
                else
                {
                    return new BitmapImage(imageUri);
                }
            }
            return null;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }
}