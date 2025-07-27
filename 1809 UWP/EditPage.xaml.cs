using Microsoft.Web.WebView2.Core;
using System;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;

namespace _1809_UWP
{
    public sealed partial class EditPage : Page
    {
        private string _pageTitle;
        private string _initialUrl;

        public EditPage()
        {
            this.InitializeComponent();
            Environment.SetEnvironmentVariable("WEBVIEW2_DEFAULT_BACKGROUND_COLOR", "00FFFFFF");
            _ = EditWebView.EnsureCoreWebView2Async();
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            _pageTitle = e.Parameter as string;
            if (string.IsNullOrEmpty(_pageTitle)) return;

            PageTitle.Text = $"Editing: {_pageTitle.Replace('_', ' ')}";

            EditWebView.CoreWebView2Initialized += (s, args) =>
            {
                EditWebView.CoreWebView2.NavigationCompleted += EditWebView_NavigationCompleted;
                EditWebView.CoreWebView2.NavigationStarting += EditWebView_NavigationStarting;

                _initialUrl = $"https://betawiki.net/index.php?title={Uri.EscapeDataString(_pageTitle)}&action=edit";
                EditWebView.CoreWebView2.Navigate(_initialUrl);
            };
        }

        private void EditWebView_NavigationStarting(CoreWebView2 sender, CoreWebView2NavigationStartingEventArgs args)
        {
            if (!args.Uri.Equals(_initialUrl, StringComparison.OrdinalIgnoreCase))
            {
                args.Cancel = true;
                if (Frame.CanGoBack)
                {
                    Frame.GoBack();
                }
            }
        }

        private async void EditWebView_NavigationCompleted(CoreWebView2 sender, CoreWebView2NavigationCompletedEventArgs args)
        {
            if (!args.IsSuccess)
            {
                LoadingRing.IsActive = false;
                PageTitle.Text = "Failed to load editor";
                return;
            }

            var isDarkTheme = Application.Current.RequestedTheme == ApplicationTheme.Dark;
            string css = GetThemeCss(isDarkTheme);
            string script = $"var style = document.createElement('style'); style.innerHTML = `{css}`; document.head.appendChild(style);";

            await sender.ExecuteScriptAsync(script);
            LoadingRing.IsActive = false;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            if (Frame.CanGoBack)
            {
                Frame.GoBack();
            }
        }

        private string GetThemeCss(bool isDark)
        {
            string cssVariables = isDark ? @":root {
                --text-primary: #FFFFFF; --text-secondary: #C3C3C3; --link-color: #85B9F3; --card-shadow: rgba(0, 0, 0, 0.4);
                --card-background: rgba(44, 44, 44, 0.7); --card-border: rgba(255, 255, 255, 0.1); --card-header-background: rgba(255, 255, 255, 0.08);
                --item-hover-background: rgba(255, 255, 255, 0.07); --item-pressed-background: rgba(255, 255, 255, 0.05);
                --accent-bg: #0078D4; --accent-text: #FFFFFF; --accent-hover-bg: #106EBE;
            }" : @":root {
                --text-primary: #000000; --text-secondary: #505050; --link-color: #0066CC; --card-shadow: rgba(0, 0, 0, 0.13);
                --card-background: rgba(249, 249, 249, 0.7); --card-border: rgba(0, 0, 0, 0.1); --card-header-background: rgba(0, 0, 0, 0.05);
                --item-hover-background: rgba(0, 0, 0, 0.05); --item-pressed-background: rgba(0, 0, 0, 0.04);
                --accent-bg: #0078D4; --accent-text: #FFFFFF; --accent-hover-bg: #005A9E;
            }";

            return $@"
                {cssVariables}

                #mw-head, #mw-panel, #footer, .mw-indicators, #siteSub, #contentSub, 
                #contentSub2, #jump-to-nav, .mw-editsection, .editOptions, 
                .mw-cookiewarning-container, #firstHeading, #mw-head-base, #mw-page-base {{
                    display: none !important;
                }}

                html, body {{
                    height: 100vh !important;
                    width: 100vw !important;
                    background-color: transparent !important;
                }}
                
                #content, .mw-body {{
                    position: static !important; height: 100% !important;
                    margin: 0 !important; border: none !important; padding: 0 !important;
                    background-color: transparent !important;
                    display: flex !important; flex-direction: column !important;
                }}

                .ve-init-target-source {{
                     display: flex !important; flex-direction: column !important;
                     flex-grow: 1 !important; min-height: 0;
                }}

                .ve-init-mw-desktopArticleTarget-toolbar {{
                    flex-shrink: 0 !important; position: relative !important; z-index: 10;
                }}
                .oo-ui-toolbar-bar {{
                    background: var(--card-background) !important;
                    border-bottom: 1px solid var(--card-border) !important;
                    box-shadow: none !important; display: flex !important; justify-content: space-between !important;
                }}

                #mw-content-text {{
                    flex-grow: 1 !important; overflow-y: auto !important;
                    padding: 12px !important; box-sizing: border-box;
                }}

                #editform, .ve-init-mw-desktopArticleTarget-editableContent {{
                    height: 100%; display: flex; flex-direction: column; margin: 0 !important;
                }}
                #wpTextbox1 {{
                    flex-grow: 1 !important; background-color: var(--card-background) !important;
                    color: var(--text-primary) !important; border: 1px solid var(--card-border) !important;
                    border-radius: 4px; font-family: 'Consolas', 'Courier New', monospace;
                    font-size: 14px; padding: 8px; margin: 0 !important;
                }}

                .oo-ui-tool-link, .oo-ui-popupToolGroup-handle {{
                    border-radius: 4px !important; border: 1px solid transparent !important;
                    background-color: transparent !important; transition: background-color 0.2s ease-in-out !important;
                }}
                .oo-ui-tool-link:hover, .oo-ui-popupToolGroup-handle:hover {{
                    background-color: var(--item-hover-background) !important;
                }}
                .oo-ui-tool-link:active, .oo-ui-popupToolGroup-handle:active, .oo-ui-tool-active > .oo-ui-tool-link {{
                    background-color: var(--item-pressed-background) !important;
                }}
                .oo-ui-iconElement-icon {{ {(isDark ? "filter: invert(1);" : "")} }}

                .ve-ui-toolbar-saveButton > .oo-ui-tool-link {{
                    background-color: var(--accent-bg) !important;
                    color: var(--accent-text) !important;
                    border-color: var(--accent-bg) !important;
                }}
                .ve-ui-toolbar-saveButton > .oo-ui-tool-link:hover {{
                    background-color: var(--accent-hover-bg) !important;
                }}
                .ve-ui-toolbar-saveButton .oo-ui-tool-title, .ve-ui-toolbar-saveButton .oo-ui-tool-accel, .ve-ui-toolbar-saveButton .oo-ui-iconElement-icon {{
                    color: var(--accent-text) !important;
                    filter: none !important; /* Remove invert filter for primary buttons */
                }}

                .oo-ui-popupWidget-popup, .oo-ui-popupToolGroup-tools {{
                    background-color: var(--card-background) !important;
                    border: 1px solid var(--card-border) !important;
                    border-radius: 4px;
                }}
                .oo-ui-popupWidget-head, .oo-ui-tool-title, .oo-ui-labelElement-label {{
                    color: var(--text-primary) !important;
                }}
                .oo-ui-tool.oo-ui-optionWidget-highlighted {{
                    background-color: var(--item-hover-background) !important;
                }}
                .oo-ui-tool.oo-ui-optionWidget-selected > .oo-ui-tool-link {{
                    background-color: var(--item-pressed-background) !important;
                }}

                .oo-ui-window-frame {{
                    background-color: var(--card-background) !important; border-color: var(--card-border) !important;
                    box-shadow: 0 4px 12px var(--card-shadow) !important; border-radius: 8px;
                }}
                .oo-ui-window-head, .oo-ui-window-foot {{
                    background-color: transparent !important; border-color: var(--card-border) !important;
                    box-shadow: none !important;
                }}
                .oo-ui-window-body {{
                    background-color: transparent !important; color: var(--text-primary) !important;
                }}
                .oo-ui-processDialog-actions .oo-ui-buttonElement-button {{
                    background-color: var(--item-hover-background) !important; border: 1px solid var(--card-border) !important;
                    color: var(--text-primary) !important; border-radius: 4px !important;
                }}
                .oo-ui-processDialog-actions .oo-ui-flaggedElement-primary.oo-ui-flaggedElement-progressive .oo-ui-buttonElement-button {{
                     background-color: var(--accent-bg) !important; color: var(--accent-text) !important;
                     border-color: var(--accent-bg) !important;
                }}
                .oo-ui-processDialog-actions .oo-ui-flaggedElement-primary.oo-ui-flaggedElement-progressive .oo-ui-buttonElement-button:hover {{
                     background-color: var(--accent-hover-bg) !important;
                }}

                .oo-ui-fieldLayout-header, label, div, p, span, li, h1, h2, h3, h4 {{
                    color: var(--text-primary) !important;
                }}
                #wpSummary, .oo-ui-textInputWidget > input {{
                    background-color: var(--card-background) !important; color: var(--text-primary) !important;
                    border: 1px solid var(--card-border) !important; border-radius: 4px !important;
                }}
                .oo-ui-tool-accel {{
                    color: var(--text-secondary) !important;
                }}
            ";
        }
    }
}