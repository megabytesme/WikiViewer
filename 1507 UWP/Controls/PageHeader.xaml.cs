using System;
using Windows.Foundation;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace _1507_UWP.Controls
{
    public sealed partial class PageHeader : UserControl
    {
        public string Title
        {
            get { return (string)GetValue(TitleProperty); }
            set { SetValue(TitleProperty, value); }
        }
        public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
            "Title",
            typeof(string),
            typeof(PageHeader),
            new PropertyMetadata(null, OnTitleChanged)
        );

        private static void OnTitleChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var control = d as PageHeader;
            control.PageTitleTextBlock.Text = e.NewValue as string;
            control.PageTitleTextBlock.Visibility = string.IsNullOrEmpty(e.NewValue as string)
                ? Visibility.Collapsed
                : Visibility.Visible;
        }

        public AutoSuggestBox SearchBox => this.NavSearchBox;
        public event RoutedEventHandler HamburgerClick;

        public event TypedEventHandler<
            AutoSuggestBox,
            AutoSuggestBoxTextChangedEventArgs
        > SearchTextChanged;
        public event TypedEventHandler<
            AutoSuggestBox,
            AutoSuggestBoxQuerySubmittedEventArgs
        > SearchQuerySubmitted;

        public PageHeader()
        {
            this.InitializeComponent();
            this.NavSearchBox.TextChanged += (s, e) => SearchTextChanged?.Invoke(s, e);
            this.NavSearchBox.QuerySubmitted += (s, e) => SearchQuerySubmitted?.Invoke(s, e);
        }

        private void HamburgerButton_Click(object sender, RoutedEventArgs e)
        {
            HamburgerClick?.Invoke(this, e);
        }
    }
}
