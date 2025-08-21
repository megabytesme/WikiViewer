using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using WikiViewer.Core;
using WikiViewer.Core.Models;
using WikiViewer.Core.Services;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;

namespace WikiViewer.Shared.Uwp.Pages
{
    public abstract class CreateAccountPageBase : Page
    {
        private List<AuthRequest> _requiredFields;
        private readonly Dictionary<string, string> _hiddenFields =
            new Dictionary<string, string>();

        protected abstract TextBlock PageTitleTextBlock { get; }
        protected abstract StackPanel FieldsStackPanel { get; }
        protected abstract Button CreateAccountButton { get; }
        protected abstract ProgressRing LoadingProgressRing { get; }
        protected abstract void ShowError(string message);
        protected abstract UIElement CreateFormattedContentPresenter(string header, string content);

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            PageTitleTextBlock.Text = $"Create Account on {AppSettings.Host}";
            await LoadRequiredFieldsAsync();
        }

        private async Task LoadRequiredFieldsAsync()
        {
            LoadingProgressRing.IsActive = true;
            FieldsStackPanel.Children.Clear();
            _hiddenFields.Clear();
            var renderedFields = new HashSet<string>();

            try
            {
                _requiredFields = await AuthService.GetCreateAccountFieldsAsync();
                foreach (var request in _requiredFields)
                {
                    if (request.Fields == null)
                        continue;
                    foreach (var field in request.Fields)
                    {
                        var fieldName = field.Key;
                        var fieldInfo = field.Value;
                        if (
                            renderedFields.Contains(fieldName)
                            || (
                                string.IsNullOrEmpty(fieldInfo.Label)
                                && string.IsNullOrEmpty(fieldInfo.Value)
                            )
                        )
                            continue;

                        switch (fieldInfo.Type)
                        {
                            case "hidden":
                                _hiddenFields[fieldName] = fieldInfo.Value;
                                break;
                            case "info":
                                FieldsStackPanel.Children.Add(
                                    CreateFormattedContentPresenter(null, fieldInfo.Label)
                                );
                                break;
                            case "password":
                                var passwordBox = new PasswordBox
                                {
                                    Header = fieldInfo.Label,
                                    Tag = fieldName,
                                    PlaceholderText = fieldInfo.Help,
                                };
                                passwordBox.PasswordChanged += ValidateInput;
                                FieldsStackPanel.Children.Add(passwordBox);
                                renderedFields.Add(fieldName);
                                break;
                            default:
                                if (!string.IsNullOrEmpty(fieldInfo.Value))
                                {
                                    FieldsStackPanel.Children.Add(
                                        CreateFormattedContentPresenter(
                                            fieldInfo.Label,
                                            fieldInfo.Value
                                        )
                                    );
                                }
                                else
                                {
                                    var textBox = new TextBox
                                    {
                                        Header = fieldInfo.Label,
                                        Tag = fieldName,
                                        PlaceholderText = fieldInfo.Help,
                                    };
                                    textBox.TextChanged += ValidateInput;
                                    FieldsStackPanel.Children.Add(textBox);
                                }
                                renderedFields.Add(fieldName);
                                break;
                        }
                    }
                }
            }
            catch (Exception ex)
           {
                ShowError($"Failed to load account fields: {ex.Message}");
                CreateAccountButton.IsEnabled = false;
            }
            finally
            {
                LoadingProgressRing.IsActive = false;
            }
        }

        protected void ValidateInput(object sender, RoutedEventArgs e)
        {
            if (sender is TextBox tb)
                tb.BorderBrush = string.IsNullOrWhiteSpace(tb.Text)
                    ? new SolidColorBrush(Windows.UI.Colors.Red)
                    : (SolidColorBrush)Application.Current.Resources["TextBoxBorderThemeBrush"];
            else if (sender is PasswordBox pb)
                pb.BorderBrush = string.IsNullOrWhiteSpace(pb.Password)
                    ? new SolidColorBrush(Windows.UI.Colors.Red)
                    : (SolidColorBrush)Application.Current.Resources["TextBoxBorderThemeBrush"];
        }

        protected async void CreateButton_Click(object sender, RoutedEventArgs e)
        {
            LoadingProgressRing.IsActive = true;
            CreateAccountButton.IsEnabled = false;

            var postData = new Dictionary<string, string>(_hiddenFields);
            foreach (var child in FieldsStackPanel.Children)
            {
                if (child is FrameworkElement fe && fe.Tag is string fieldName)
                {
                    if (child is TextBox textBox)
                        postData[fieldName] = textBox.Text;
                    else if (child is PasswordBox passwordBox)
                        postData[fieldName] = passwordBox.Password;
                }
            }
            try
            {
                var result = await AuthService.PerformCreateAccountAsync(postData);
                if (result.Status == "PASS")
                {
                    var successDialog = new ContentDialog
                    {
                        Title = "Account Created",
                        Content =
                            $"Your account '{result.Username}' was created successfully. You can now log in.",
                        CloseButtonText = "OK",
                    };
                    await successDialog.ShowAsync();
                    if (Frame.CanGoBack)
                        Frame.GoBack();
                }
                else
                {
                    ShowError(
                        result.Message ?? $"Account creation failed with status: {result.Status}"
                    );
                }
            }
            catch (Exception ex)
            {
                ShowError($"An error occurred: {ex.Message}");
            }
            finally
            {
                LoadingProgressRing.IsActive = false;
                CreateAccountButton.IsEnabled = true;
            }
        }
    }
}
