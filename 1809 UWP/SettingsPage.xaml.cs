using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;

namespace _1809_UWP
{
    public sealed partial class SettingsPage : Page
    {
        public SettingsPage()
        {
            this.InitializeComponent();
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            CachingToggle.IsOn = AppSettings.IsCachingEnabled;
        }

        private void CachingToggle_Toggled(object sender, Windows.UI.Xaml.RoutedEventArgs e)
        {
            AppSettings.IsCachingEnabled = CachingToggle.IsOn;
        }
    }
}