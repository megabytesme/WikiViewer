using System;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;

namespace App1
{
    public sealed partial class MainPage : Page
    {
        public MainPage()
        {
            this.InitializeComponent();
        }

        private void FetchButton_Click(object sender, RoutedEventArgs e)
        {
            ResultTextBlock.Text = "Navigating to verification page...";
            string apiUrl = "https://betawiki.net/api.php?action=query&list=random&rnlimit=1&format=json";
            Frame.Navigate(typeof(VerificationPage), apiUrl);
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            if (e.NavigationMode == NavigationMode.Back && App.VerificationResult != null)
            {
                ResultTextBlock.Text = App.VerificationResult;
                App.VerificationResult = null;
            }
        }
    }
}