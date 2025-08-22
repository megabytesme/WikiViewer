using System;
using System.Linq;
using WikiViewer.Core.Models;
using WikiViewer.Core.Services;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace WikiViewer.Shared.Uwp.Controls
{
    public sealed partial class WikiActionsFlyout : UserControl
    {
        public event Action<Guid> GoToHomePageRequested;
        public event Action<Guid> GoToRandomPageRequested;
        public event Action ManageWikisRequested;

        public WikiActionsFlyout()
        {
            this.InitializeComponent();
            var wikis = WikiManager.GetWikis();
            HomeListView.ItemsSource = wikis;
            RandomListView.ItemsSource = wikis;
        }

        private void HomeListView_ItemClick(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is WikiInstance wiki) GoToHomePageRequested?.Invoke(wiki.Id);
        }

        private void RandomListView_ItemClick(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is WikiInstance wiki) GoToRandomPageRequested?.Invoke(wiki.Id);
        }

        private void SurpriseMeButton_Click(object sender, RoutedEventArgs e)
        {
            var wikis = WikiManager.GetWikis();
            if (wikis.Any())
            {
                var randomWiki = wikis[new Random().Next(wikis.Count)];
                GoToRandomPageRequested?.Invoke(randomWiki.Id);
            }
        }

        private void ManageButton_Click(object sender, RoutedEventArgs e)
        {
            ManageWikisRequested?.Invoke();
        }
    }
}