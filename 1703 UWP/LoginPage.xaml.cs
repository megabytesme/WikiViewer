using System;
using WikiViewer.Shared.Uwp.Pages;
using Windows.UI.Xaml.Controls;

namespace _1703_UWP
{
    public sealed partial class LoginPage : LoginPageBase
    {
        public LoginPage()
        {
            this.InitializeComponent();
        }

        protected override TextBlock LoginTitleTextBlock => LoginTitle;
        protected override TextBox UsernameTextBox => UsernameBox;
        protected override PasswordBox UserPasswordBox => PasswordBox;
        protected override CheckBox RememberMeCheckBoxControl => RememberMeCheckBox;
        protected override Button LoginButtonControl => LoginButton;
        protected override ProgressRing LoadingProgressRing => LoadingRing;
        protected override TextBlock ErrorTextBlockControl => ErrorTextBlock;

        protected override Type GetCreateAccountPageType() => typeof(Pages.CreateAccountPage);
    }
}
