using System;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Media.Imaging;

namespace WikiViewer.Shared.Uwp.Converters
{
	public sealed class StringToBitmapImageConverter : IValueConverter
	{
		public object Convert(object value, Type targetType, object parameter, string language)
		{
			var url = value as string;
			if (string.IsNullOrWhiteSpace(url))
				return null;

			try
			{
				return new BitmapImage(new Uri(url));
			}
			catch
			{
				return null;
			}
		}

		public object ConvertBack(object value, Type targetType, object parameter, string language) =>
			throw new NotImplementedException();
	}
}