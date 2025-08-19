using Microsoft.UI.Xaml.Controls;
using Shared_Code;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;

namespace _1809_UWP
{
    public sealed partial class CreateAccountPage : Page
    {
        private List<AuthRequest> _requiredFields;
        private readonly Dictionary<string, string> _hiddenFields = new Dictionary<string, string>();

        public CreateAccountPage()
        {
            this.InitializeComponent();
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            PageTitle.Text = $"Create Account on {AppSettings.Host}";
            await LoadRequiredFieldsAsync();
        }

        private async Task LoadRequiredFieldsAsync()
        {
            LoadingRing.IsActive = true;
            ErrorWebView.Visibility = Visibility.Collapsed;
            FieldsPanel.Children.Clear();
            _hiddenFields.Clear();
            var renderedFields = new HashSet<string>();

            try
            {
                _requiredFields = await AuthService.GetCreateAccountFieldsAsync();

                foreach (var request in _requiredFields)
                {
                    if (request.Fields == null) continue;

                    foreach (var field in request.Fields)
                    {
                        var fieldName = field.Key;
                        var fieldInfo = field.Value;

                        if (renderedFields.Contains(fieldName) || (string.IsNullOrEmpty(fieldInfo.Label) && string.IsNullOrEmpty(fieldInfo.Value))) continue;

                        switch (fieldInfo.Type)
                        {
                            case "hidden":
                                _hiddenFields[fieldName] = fieldInfo.Value;
                                break;

                            case "info":
                                FieldsPanel.Children.Add(CreateFormattedContentPresenter(null, fieldInfo.Label));
                                break;

                            case "password":
                                var passwordBox = new PasswordBox
                                {
                                    Header = fieldInfo.Label,
                                    Tag = fieldName,
                                    PlaceholderText = fieldInfo.Help
                                };
                                passwordBox.PasswordChanged += ValidateInput;
                                FieldsPanel.Children.Add(passwordBox);
                                renderedFields.Add(fieldName);
                                break;

                            default:
                                if (!string.IsNullOrEmpty(fieldInfo.Value))
                                {
                                    FieldsPanel.Children.Add(CreateFormattedContentPresenter(fieldInfo.Label, fieldInfo.Value));
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
                                    FieldsPanel.Children.Add(textBox);
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
                CreateButton.IsEnabled = false;
            }
            finally
            {
                LoadingRing.IsActive = false;
            }
        }

        private void ValidateInput(object sender, RoutedEventArgs e)
        {
            if (sender is TextBox tb)
            {
                tb.BorderBrush = string.IsNullOrWhiteSpace(tb.Text) ? new SolidColorBrush(Windows.UI.Colors.Red) : (SolidColorBrush)Application.Current.Resources["TextBoxBorderThemeBrush"];
            }
            else if (sender is PasswordBox pb)
            {
                pb.BorderBrush = string.IsNullOrWhiteSpace(pb.Password) ? new SolidColorBrush(Windows.UI.Colors.Red) : (SolidColorBrush)Application.Current.Resources["TextBoxBorderThemeBrush"];
            }
        }

        private UIElement CreateFormattedContentPresenter(string header, string content)
        {
            var panel = new StackPanel();
            if (!string.IsNullOrEmpty(header))
            {
                panel.Children.Add(CreateWikitextBlock(header));
            }

            string combinedContent = content ?? header;
            bool isRichContent = System.Text.RegularExpressions.Regex.IsMatch(combinedContent, @"(\[\[.*?\]\]|<.*?>|&.*?;|\/index\.php\?)");

            if (isRichContent)
            {
                var webView = new WebView2
                {
                    Height = 120,
                };

                string htmlContent = ParseWikitextToHtml(combinedContent);

                webView.Loaded += async (s, ev) => {
                    var wv = s as WebView2;
                    await wv.EnsureCoreWebView2Async();

                    wv.CoreWebView2.NavigationStarting += async (coreWv, args) => {
                        if (args.Uri != null && (args.Uri.StartsWith("http") || args.Uri.StartsWith("https")))
                        {
                            args.Cancel = true;
                            await Windows.System.Launcher.LaunchUriAsync(new Uri(args.Uri));
                        }
                    };
                    wv.NavigateToString(htmlContent);
                };
                panel.Children.Add(webView);
            }
            else
            {
                var textBox = new TextBox
                {
                    Text = combinedContent,
                    IsReadOnly = true,
                    BorderThickness = new Thickness(0),
                    TextWrapping = TextWrapping.Wrap,
                    AcceptsReturn = true,
                    Height = Double.NaN
                };
                panel.Children.Add(textBox);
            }
            return panel;
        }

        private TextBlock CreateWikitextBlock(string text)
        {
            var textBlock = new TextBlock { TextWrapping = TextWrapping.Wrap };
            var parts = System.Text.RegularExpressions.Regex.Split(text, @"(\[\[.*?\]\])");

            foreach (var part in parts)
            {
                if (part.StartsWith("[[") && part.EndsWith("]]"))
                {
                    var linkContent = part.Substring(2, part.Length - 4);
                    var linkParts = linkContent.Split('|');
                    var target = linkParts[0];
                    var displayText = linkParts.Length > 1 ? linkParts[1] : target;

                    var hyperlink = new Windows.UI.Xaml.Documents.Hyperlink();
                    hyperlink.Inlines.Add(new Windows.UI.Xaml.Documents.Run { Text = displayText });
                    hyperlink.NavigateUri = new Uri(AppSettings.GetWikiPageUrl(target));
                    hyperlink.Click += async (s, e) => await Windows.System.Launcher.LaunchUriAsync(s.NavigateUri);

                    textBlock.Inlines.Add(hyperlink);
                }
                else
                {
                    textBlock.Inlines.Add(new Windows.UI.Xaml.Documents.Run { Text = part });
                }
            }
            return textBlock;
        }

        private string ParseWikitextToHtml(string text)
        {
            string bodyContent = text;

            if (bodyContent.StartsWith("/w/index.php?") && bodyContent.Contains("Captcha/image"))
            {
                string imageUrl = AppSettings.BaseUrl.TrimEnd('/') + bodyContent;
                bodyContent = $"<img src='{imageUrl}' alt='CAPTCHA Image' />";
            }
            else
            {
                bodyContent = System.Net.WebUtility.HtmlDecode(bodyContent);
                bodyContent = System.Text.RegularExpressions.Regex.Replace(bodyContent, @"(src|href)=""//", match => {
                    return $"{match.Groups[1].Value}=\"https://";
                });
                bodyContent = System.Text.RegularExpressions.Regex.Replace(bodyContent, @"\[\[(.*?)\]\]", match => {
                    var linkContent = match.Groups[1].Value;
                    var linkParts = linkContent.Split('|');
                    var target = linkParts[0];
                    var displayText = linkParts.Length > 1 ? linkParts[1] : target;
                    var url = AppSettings.GetWikiPageUrl(target);
                    return $"<a href='{url}'>{displayText}</a>";
                });
            }

            return $@"
                <html>
                    <head>
                        <style>
                            body {{ 
                                font-family: 'Segoe UI', sans-serif; 
                                color: {(Application.Current.RequestedTheme == ApplicationTheme.Dark ? "white" : "black")}; 
                                background-color: transparent; margin: 0; padding: 8px; font-size: 14px;
                                text-align: center; display: flex; flex-direction: column;
                                align-items: center; justify-content: center; height: 100vh;
                                box-sizing: border-box;
                            }}
                            img {{ max-width: 100%; height: auto; margin-top: 4px; border: 1px solid gray; background-color: white; padding: 2px; }}
                            a {{ color: {(Application.Current.RequestedTheme == ApplicationTheme.Dark ? "#85B9F3" : "#0066CC")}; }}
                            .fmbox {{ border: 1px solid red; padding: 8px; border-radius: 4px; background-color: rgba(255,0,0,0.1); }}
                        </style>
                    </head>
                    <body><div>{bodyContent}</div></body>
                </html>";
        }

        private async void CreateButton_Click(object sender, RoutedEventArgs e)
        {
            LoadingRing.IsActive = true;
            ErrorWebView.Visibility = Visibility.Collapsed;
            CreateButton.IsEnabled = false;

            var postData = new Dictionary<string, string>(_hiddenFields);

            foreach (var child in FieldsPanel.Children)
            {
                if (child is FrameworkElement fe && fe.Tag is string fieldName)
                {
                    if (child is TextBox textBox) postData[fieldName] = textBox.Text;
                    else if (child is PasswordBox passwordBox) postData[fieldName] = passwordBox.Password;
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
                        Content = $"Your account '{result.Username}' was created successfully. You can now log in.",
                        CloseButtonText = "OK",
                        XamlRoot = this.XamlRoot
                    };
                    await successDialog.ShowAsync();
                    if (Frame.CanGoBack) Frame.GoBack();
                }
                else
                {
                    ShowError(result.Message ?? $"Account creation failed with status: {result.Status}");
                }
            }
            catch (Exception ex)
            {
                ShowError($"An error occurred: {ex.Message}");
            }
            finally
            {
                LoadingRing.IsActive = false;
                CreateButton.IsEnabled = true;
            }
        }

        private async void ShowError(string message)
        {
            await ErrorWebView.EnsureCoreWebView2Async();
            ErrorWebView.NavigateToString(ParseWikitextToHtml(message));
            ErrorWebView.Visibility = Visibility.Visible;
        }
    }
}