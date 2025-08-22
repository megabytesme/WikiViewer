using WikiViewer.Shared.Uwp.Pages;
using Windows.UI.Xaml.Controls;

namespace _1703_UWP.Pages
{
    public sealed partial class SettingsPage : SettingsPageBase
    {
        public SettingsPage()
        {
            this.InitializeComponent();
        }

        protected override ToggleSwitch CachingToggleControl => this.CachingToggle;
        protected override ComboBox ConcurrencyComboBoxControl => this.ConcurrencyComboBox;
        protected override TextBlock CacheSizeTextControl => this.CacheSizeText;
        protected override Button ClearCacheButtonControl => this.ClearCacheButton;
        protected override ListView WikiListViewControl => this.WikiListView;
        protected override TextBlock ConcurrencyDescriptionTextControl => this.ConcurrencyDescriptionText;
    }
}