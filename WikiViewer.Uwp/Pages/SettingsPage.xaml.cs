using System;
using WikiViewer.Uwp.Pages;
using Windows.UI.Xaml.Controls;

namespace WikiViewer.Uwp.Pages
{
    public sealed partial class SettingsPage : SettingsPageBase
    {
        public SettingsPage()
        {
            this.InitializeComponent();
        }

        protected override ToggleSwitch CachingToggleControl => this.CachingToggle;
        protected override ToggleSwitch NativeReaderDefaultToggleControl => this.NativeReaderDefaultToggle;
        protected override ComboBox ConcurrencyComboBoxControl => this.ConcurrencyComboBox;
        protected override TextBlock CacheSizeTextControl => this.CacheSizeText;
        protected override Button ClearCacheButtonControl => this.ClearCacheButton;
        protected override ListView WikiListViewControl => this.WikiListView;
        protected override TextBlock ConcurrencyDescriptionTextControl => this.ConcurrencyDescriptionText;
        protected override ToggleSwitch EnableCustomThemingToggleControl => this.EnableCustomThemingToggle;
        protected override ToggleSwitch ShowCssRefreshButtonToggleControl => ShowCssRefreshToggle;
        protected override ComboBox DefaultConnectionMethodComboBoxControl => this.DefaultConnectionMethodComboBox;
        protected override TextBox ResolverUrlControl => this.ResolverUrl;
        protected override TextBox ResolverUsernameControl => this.ResolverUsername;
        protected override PasswordBox ResolverPasswordControl => this.ResolverPassword;
        protected override TextBlock ResolverStatusControl => this.ResolverStatus;

        protected override Type GetWikiDetailPageType() => typeof(WikiDetailPage);
    }
}
