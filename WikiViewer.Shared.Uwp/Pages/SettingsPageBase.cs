using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using WikiViewer.Core;
using WikiViewer.Core.Enums;
using WikiViewer.Core.Interfaces;
using WikiViewer.Core.Models;
using WikiViewer.Core.Services;
using WikiViewer.Shared.Uwp.Managers;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;

namespace WikiViewer.Shared.Uwp.Pages
{
    public abstract class SettingsPageBase : Page
    {
        private WikiInstance _selectedWiki;
        protected ObservableCollection<WikiInstance> Wikis { get; } = new ObservableCollection<WikiInstance>();
        protected ObservableCollection<Account> AccountsForSelectedWiki { get; } = new ObservableCollection<Account>();

        // ---- THE FIX: RE-INTRODUCE ABSTRACT PROPERTIES FOR ALL UI CONTROLS ----
        protected abstract ListView WikiListView { get; }
        protected abstract Panel DetailPanel { get; }
        protected abstract Button AddWikiButton { get; }
        protected abstract Button RemoveWikiButton { get; }
        protected abstract TextBox WikiNameTextBox { get; }
        protected abstract TextBox WikiUrlTextBox { get; }
        protected abstract TextBox ScriptPathTextBox { get; }
        protected abstract TextBox ArticlePathTextBox { get; }
        protected abstract ToggleSwitch ConnectionMethodToggleSwitch { get; }
        protected abstract TextBlock DetectionStatusTextBlock { get; }
        protected abstract Button ApplyWikiChangesButton { get; }
        protected abstract Button DetectPathsButton { get; }
        protected abstract ListView AccountListView { get; }
        protected abstract Button AddAccountButton { get; }
        protected abstract Grid LoadingOverlay { get; }
        protected abstract TextBlock LoadingText { get; }
        protected abstract Grid VerificationPanel { get; }
        protected abstract void ShowVerificationPanel(string url);
        protected abstract void ResetAppRootFrame();
        protected abstract Type GetLoginPageType();

        public SettingsPageBase()
        {
            this.Loaded += Page_Loaded;
            this.Unloaded += Page_Unloaded;
        }

        private void Page_Loaded(object sender, RoutedEventArgs e)
        {
            AuthenticationService.AuthenticationStateChanged += OnAuthenticationStateChanged;
            LoadWikis();
            WikiListView.ItemsSource = Wikis;
            AccountListView.ItemsSource = AccountsForSelectedWiki;

            var wikiToSelect = Wikis.FirstOrDefault(w => w.Id == SessionManager.CurrentWiki?.Id) ?? Wikis.FirstOrDefault();
            if (wikiToSelect != null)
            {
                WikiListView.SelectedItem = wikiToSelect;
            }
        }

        private void Page_Unloaded(object sender, RoutedEventArgs e)
        {
            AuthenticationService.AuthenticationStateChanged -= OnAuthenticationStateChanged;
        }

        private void OnAuthenticationStateChanged(object sender, AuthenticationStateChangedEventArgs e)
        {
            if (_selectedWiki != null && e.Wiki.Id == _selectedWiki.Id)
            {
                _ = Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, LoadAccountsForSelectedWiki);
            }
        }

        private void LoadWikis()
        {
            Wikis.Clear();
            var wikisFromManager = WikiManager.GetWikis();
            if (wikisFromManager == null) return;

            foreach (var wiki in wikisFromManager.OrderBy(w => w.Name))
            {
                Wikis.Add(wiki);
            }
        }

        protected void WikiListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (e.AddedItems.FirstOrDefault() is WikiInstance selected)
            {
                _selectedWiki = selected;
                PopulateDetailPanel(selected);
                DetailPanel.Visibility = Visibility.Visible;
                RemoveWikiButton.IsEnabled = Wikis.Count > 1;
            }
            else
            {
                _selectedWiki = null;
                DetailPanel.Visibility = Visibility.Collapsed;
            }
        }

        private void PopulateDetailPanel(WikiInstance wiki)
        {
            WikiNameTextBox.Text = wiki.Name;
            WikiUrlTextBox.Text = wiki.BaseUrl;
            ScriptPathTextBox.Text = wiki.ScriptPath;
            ArticlePathTextBox.Text = wiki.ArticlePath;
            ConnectionMethodToggleSwitch.IsOn = wiki.PreferredConnectionMethod == ConnectionMethod.HttpClientProxy;
            LoadAccountsForSelectedWiki();
        }

        private void LoadAccountsForSelectedWiki()
        {
            AccountsForSelectedWiki.Clear();
            if (_selectedWiki == null) return;

            var accounts = AccountManager.GetAccountsForWiki(_selectedWiki.Id);
            foreach (var account in accounts.OrderBy(a => a.Username))
            {
                AccountsForSelectedWiki.Add(account);
            }
        }

        protected async void AddWikiButton_Click(object sender, RoutedEventArgs e)
        {
            var newWiki = new WikiInstance
            {
                Name = "New Wiki",
                BaseUrl = "https://www.mediawiki.org/"
            };

            await WikiManager.AddWikiAsync(newWiki);

            LoadWikis();

            var newlyAddedWiki = Wikis.FirstOrDefault(w => w.Id == newWiki.Id);
            if (newlyAddedWiki != null)
            {
                WikiListView.SelectedItem = newlyAddedWiki;
            }
        }

        protected async void RemoveWikiButton_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedWiki == null || Wikis.Count <= 1) return;

            var dialog = new ContentDialog
            {
                Title = $"Delete '{_selectedWiki.Name}'?",
                Content = "This will permanently delete the wiki, all associated accounts, and its favourites from the app. This action cannot be undone.",
                PrimaryButtonText = "Delete",
                CloseButtonText = "Cancel"
            };
            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            {
                await WikiManager.RemoveWikiAsync(_selectedWiki.Id);
                LoadWikis();
            }
        }

        protected async void ApplyWikiChangesButton_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedWiki == null) return;

            _selectedWiki.Name = WikiNameTextBox.Text;
            _selectedWiki.BaseUrl = WikiUrlTextBox.Text;
            _selectedWiki.ScriptPath = ScriptPathTextBox.Text;
            _selectedWiki.ArticlePath = ArticlePathTextBox.Text;
            _selectedWiki.PreferredConnectionMethod = ConnectionMethodToggleSwitch.IsOn ? ConnectionMethod.HttpClientProxy : ConnectionMethod.WebView;

            await WikiManager.SaveAsync();

            if (SessionManager.CurrentWiki.Id == _selectedWiki.Id)
            {
                var dialog = new ContentDialog
                {
                    Title = "Reload Required",
                    Content = "You have modified the currently active wiki. The app needs to reload to apply these changes.",
                    PrimaryButtonText = "Reload Now"
                };
                await dialog.ShowAsync();
                ResetAppRootFrame();
            }
            else
            {
                LoadWikis();
            }
        }

        protected void AddAccountButton_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedWiki == null) return;
            Frame.Navigate(GetLoginPageType(), _selectedWiki.Id);
        }

        protected async void DetectPathsButton_Click(object sender, RoutedEventArgs e)
        {
            string urlToDetect = WikiUrlTextBox.Text.Trim();
            if (string.IsNullOrEmpty(urlToDetect))
            {
                DetectionStatusTextBlock.Text = "Please enter a Base URL first.";
                DetectionStatusTextBlock.Foreground = new SolidColorBrush(Colors.Red);
                DetectionStatusTextBlock.Visibility = Visibility.Visible;
                return;
            }

            LoadingOverlay.Visibility = Visibility.Visible;
            LoadingText.Text = "Detecting paths...";
            DetectionStatusTextBlock.Visibility = Visibility.Collapsed;

            using (var tempWorker = App.ApiWorkerFactory.CreateApiWorker(ConnectionMethod.WebView))
            {
                try
                {
                    await tempWorker.InitializeAsync(urlToDetect);
                    var detectedPaths = await WikiPathDetectorService.DetectPathsAsync(urlToDetect, tempWorker);

                    if (detectedPaths.WasDetectedSuccessfully)
                    {
                        ScriptPathTextBox.Text = detectedPaths.ScriptPath;
                        ArticlePathTextBox.Text = detectedPaths.ArticlePath;
                        DetectionStatusTextBlock.Text = "Detection successful! Review the paths and click Apply.";
                        DetectionStatusTextBlock.Foreground = new SolidColorBrush(Colors.Green);
                    }
                    else
                    {
                        DetectionStatusTextBlock.Text = "Could not automatically detect paths. Please enter them manually.";
                        DetectionStatusTextBlock.Foreground = new SolidColorBrush(Colors.Red);
                    }
                }
                catch (NeedsUserVerificationException ex)
                {
                    ShowVerificationPanel(ex.Url);
                }
                catch (Exception ex)
                {
                    DetectionStatusTextBlock.Text = $"An error occurred during detection: {ex.Message}";
                    DetectionStatusTextBlock.Foreground = new SolidColorBrush(Colors.Red);
                }
                finally
                {
                    DetectionStatusTextBlock.Visibility = Visibility.Visible;
                    LoadingOverlay.Visibility = Visibility.Collapsed;
                }
            }
        }
    }
}