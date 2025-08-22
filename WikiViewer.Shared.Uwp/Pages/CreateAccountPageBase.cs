using System;
using System.Collections.Generic;
using System.Threading.Tasks;
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
        protected WikiInstance _wikiForAccountCreation;
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

            if (e.Parameter is Guid wikiId)
            {
                _wikiForAccountCreation = WikiManager.GetWikiById(wikiId);
            }

            if (_wikiForAccountCreation == null)
            {
                ShowError("Cannot create account: No wiki has been selected.");
                CreateAccountButton.IsEnabled = false;
                return;
            }

            PageTitleTextBlock.Text = $"Create Account on {_wikiForAccountCreation.Host}";
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
                using (var worker = App.ApiWorkerFactory.CreateApiWorker(_wikiForAccountCreation))
                {
                    await worker.InitializeAsync(_wikiForAccountCreation.BaseUrl);
                    string url = $"{_wikiForAccountCreation.ApiEndpoint}?action=query&meta=authmanagerinfo&amirequestsfor=create&format=json";
                    string json = await worker.GetJsonFromApiAsync(url);
                    var response =
                        Newtonsoft.Json.JsonConvert.DeserializeObject<AuthManagerInfoResponse>(
                            json
                        );
                    _requiredFields =
                        response?.Query?.AuthManagerInfo?.Requests
                        ?? throw new Exception(
                            "Could not retrieve required fields for account creation."
                        );
                }

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
                CreateAccountResult result;
                using (var worker = App.ApiWorkerFactory.CreateApiWorker(_wikiForAccountCreation))
                {
                    await worker.InitializeAsync(_wikiForAccountCreation.BaseUrl);
                    string tokenUrl = $"{_wikiForAccountCreation.ApiEndpoint}?action=query&meta=tokens&type=createaccount&format=json";
                    string tokenJson = await worker.GetJsonFromApiAsync(tokenUrl);
                    var tokenResponse = Newtonsoft.Json.Linq.JObject.Parse(tokenJson);
                    string createToken = tokenResponse?["query"]?["tokens"]?["createaccounttoken"]?.ToString();
                    if (string.IsNullOrEmpty(createToken)) throw new Exception("Failed to retrieve a createaccount token.");

                    var finalPostData = new Dictionary<string, string>(postData)
                    {
                        { "action", "createaccount" },
                        { "createtoken", createToken },
                        { "format", "json" },
                        { "createreturnurl", _wikiForAccountCreation.BaseUrl },
                    };
                    string resultJson = await worker.PostAndGetJsonFromApiAsync(_wikiForAccountCreation.ApiEndpoint, finalPostData);
                    var resultObj = Newtonsoft.Json.JsonConvert.DeserializeObject<CreateAccountResponse>(resultJson);
                    result = resultObj?.CreateAccount ?? throw new Exception("Received an invalid response from the server.");
                }

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