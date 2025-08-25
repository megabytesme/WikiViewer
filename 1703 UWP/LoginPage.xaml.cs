using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using WikiViewer.Core.Models;
using WikiViewer.Shared.Uwp.Pages;
using Windows.UI.Xaml;
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

        protected override async Task ShowInteractiveLoginAsync(
            AuthUiRequiredException authException,
            Account account
        )
        {
            var loginResult = authException.LoginResult;

            var panel = new StackPanel { Margin = new Thickness(12) };
            var textBoxes = new Dictionary<string, TextBox>();

            foreach (var request in loginResult.Requests)
            {
                foreach (var field in request.Fields)
                {
                    panel.Children.Add(
                        new TextBlock
                        {
                            Text = field.Value.Label,
                            Margin = new Thickness(0, 8, 0, 0),
                        }
                    );
                    var textBox = new TextBox { PlaceholderText = field.Value.Help };
                    panel.Children.Add(textBox);
                    textBoxes.Add(field.Key, textBox);
                }
            }

            var dialog = new ContentDialog
            {
                Title = "Two-Factor Authentication",
                Content = panel,
                PrimaryButtonText = "Submit",
                SecondaryButtonText = "Cancel"
            };

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                var fieldData = textBoxes.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.Text);

                LoadingProgressRing.IsActive = true;
                LoginButtonControl.IsEnabled = false;
                try
                {
                    //TODO: implement 2fa - await new AuthenticationService(account, SessionManager.CurrentWiki, App.ApiWorkerFactory).ContinueLoginAsync(fieldData);

                    throw new NotImplementedException(
                        "Interactive login continuation must be implemented in AuthenticationService."
                    );
                }
                catch (Exception ex)
                {
                    ErrorTextBlockControl.Text = $"An error occurred: {ex.Message}";
                }
                finally
                {
                    LoadingProgressRing.IsActive = false;
                    LoginButtonControl.IsEnabled = true;
                }
            }
        }
    }
}
