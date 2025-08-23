using System;
using Windows.UI.Xaml.Data;

namespace _1703_UWP.Converters
{
    public class BoolToExpandCollapseSymbolConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is bool isExpanded && isExpanded)
            {
                return "\uE70E";
            }

            return "\uE70D";
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }
}