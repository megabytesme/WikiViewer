using System;
using System.Collections.ObjectModel;
using System.Linq;
using WikiViewer.Core.Models;
using WikiViewer.Core.Services;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;

namespace WikiViewer.Shared.Uwp.Pages
{
    public sealed partial class WikiManagementPage : Page
    {
        private readonly ObservableCollection<WikiInstance> Wikis = new ObservableCollection<WikiInstance>();

        public WikiManagementPage()
        {
            this.InitializeComponent();
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            LoadWikis();
            WikiManager.WikisChanged += WikiManager_WikisChanged;
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);
            WikiManager.WikisChanged -= WikiManager_WikisChanged;
        }

        private void WikiManager_WikisChanged(object sender, EventArgs e)
        {
            LoadWikis();
        }

        private void LoadWikis()
        {
            Wikis.Clear();
            foreach (var wiki in WikiManager.GetWikis().OrderBy(w => w.Name))
            {
                Wikis.Add(wiki);
            }
        }

        private void WikiListView_ItemClick(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is WikiInstance wiki)
            {
                Frame.Navigate(typeof(WikiDetailPage), wiki.Id);
            }
        }

        private async void AddWikiButton_Click(object sender, Windows.UI.Xaml.RoutedEventArgs e)
        {
            var newWiki = new WikiInstance
            {
                Name = "New Wiki",
                BaseUrl = "https://www.mediawiki.org/"
            };
            await WikiManager.AddWikiAsync(newWiki);
        }
    }
}