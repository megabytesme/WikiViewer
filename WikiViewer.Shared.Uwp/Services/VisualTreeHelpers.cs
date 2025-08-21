using System.Collections.Generic;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Media;

namespace WikiViewer.Shared.Uwp.Services
{
    public static class VisualTreeHelpers
    {
        public static IEnumerable<T> FindChildren<T>(this DependencyObject parent)
            where T : DependencyObject
        {
            if (parent != null)
            {
                int count = VisualTreeHelper.GetChildrenCount(parent);
                for (int i = 0; i < count; i++)
                {
                    DependencyObject child = VisualTreeHelper.GetChild(parent, i);
                    if (child is T typedChild)
                    {
                        yield return typedChild;
                    }

                    foreach (T nestedChild in FindChildren<T>(child))
                    {
                        yield return nestedChild;
                    }
                }
            }
        }
    }
}
