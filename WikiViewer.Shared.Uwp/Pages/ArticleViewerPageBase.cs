using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using HtmlAgilityPack;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using WikiViewer.Core.Models;
using WikiViewer.Core.Services;
using WikiViewer.Shared.Uwp.Controls;
using WikiViewer.Shared.Uwp.Services;
using Windows.ApplicationModel.DataTransfer;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;

namespace WikiViewer.Shared.Uwp.Pages
{
    public abstract class ArticleViewerPageBase : Page
    {
        protected bool _isVerificationOnlyFlow = false;
        public WikiInstance _pageWikiContext;
        protected string _pageTitleToFetch = "";
        protected string _verificationUrl = null;
        protected bool _isInitialized = false;
        protected readonly Stack<string> _articleHistory = new Stack<string>();
        public bool CanGoBackInPage => _articleHistory.Count > 1;
        protected abstract AppBarButton RefreshAppBarButton { get; }
        protected abstract Type GetArticleViewerPageType();

        protected abstract TextBlock ArticleTitleTextBlock { get; }
        protected abstract TextBlock LoadingTextBlock { get; }
        protected abstract TextBlock LastUpdatedTextBlock { get; }
        protected abstract Grid LoadingOverlayGrid { get; }
        protected abstract Grid VerificationPanelGrid { get; }
        protected abstract AppBarButton EditAppBarButton { get; }
        protected abstract AppBarButton FavoriteAppBarButton { get; }
        protected abstract void ShowLoadingOverlay();
        protected abstract void HideLoadingOverlay();
        protected abstract Task DisplayProcessedHtmlAsync(string html);
        protected abstract void ShowVerificationPanel(string url);
        protected abstract void InitializePlatformControls();
        protected abstract void UninitializePlatformControls();
        protected abstract Type GetEditPageType();
        protected abstract Task ExecuteScriptInWebViewAsync(string script);
        protected abstract string GetImageUpdateScript(string originalUrl, string localPath);
        protected abstract Type GetLoginPageType();
        protected abstract Type GetCreateAccountPageType();
        protected abstract void UpdateRefreshButtonVisibility();

        public ArticleViewerPageBase()
        {
            AuthenticationService.AuthenticationStateChanged += OnAuthenticationStateChanged;
            this.Loaded += Page_Loaded;
            this.Unloaded += Page_Unloaded;
        }

        private async void EnhanceDisplayedHtmlWithCachedMediaAsync(string html)
        {
            if (string.IsNullOrEmpty(html) || _pageWikiContext == null)
                return;

            var doc = new HtmlDocument();
            doc.LoadHtml(html);
            var baseUri = new Uri(_pageWikiContext.BaseUrl);

            var mediaNodes = doc.DocumentNode.SelectNodes("//img[@src or @data-src]");
            if (mediaNodes == null)
                return;

            var uniqueImageUrls = new Dictionary<string, string>();
            foreach (var node in mediaNodes)
            {
                string originalUrl =
                    node.GetAttributeValue("data-src", null) ?? node.GetAttributeValue("src", null);

                if (
                    string.IsNullOrEmpty(originalUrl)
                    || originalUrl.StartsWith("data:")
                    || originalUrl.StartsWith("ms-appdata")
                )
                    continue;

                try
                {
                    var absoluteUrl = new Uri(baseUri, originalUrl).AbsoluteUri;
                    string elementIdentifier =
                        node.GetAttributeValue("data-src", null)
                        ?? node.GetAttributeValue("src", null);

                    if (!uniqueImageUrls.ContainsKey(absoluteUrl))
                    {
                        uniqueImageUrls.Add(absoluteUrl, elementIdentifier);
                    }
                }
                catch (UriFormatException ex)
                {
                    Debug.WriteLine(
                        $"[EnhanceHtml] Invalid URL format encountered: {originalUrl}. Error: {ex.Message}"
                    );
                }
            }

            foreach (var kvp in uniqueImageUrls)
            {
                string absoluteUrl = kvp.Key;
                string originalSrcValue = kvp.Value;

                _ = Task.Run(async () =>
                {
                    try
                    {
                        string localUri = await MediaCacheService.GetLocalUriAsync(
                            absoluteUrl,
                            _pageWikiContext
                        );

                        if (!string.IsNullOrEmpty(localUri))
                        {
                            string updateScript = GetImageUpdateScript(originalSrcValue, localUri);

                            await Dispatcher.RunAsync(
                                Windows.UI.Core.CoreDispatcherPriority.Normal,
                                async () =>
                                {
                                    await ExecuteScriptInWebViewAsync(updateScript);
                                }
                            );
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine(
                            $"[EnhanceHtml Task] Failed to process image '{absoluteUrl}': {ex.Message}"
                        );
                    }
                });
            }
        }

        private void OnAuthenticationStateChanged(
            object sender,
            AuthenticationStateChangedEventArgs e
        )
        {
            if (_pageWikiContext != null && e.Wiki.Id == _pageWikiContext.Id)
            {
                _ = Dispatcher.RunAsync(
                    Windows.UI.Core.CoreDispatcherPriority.Normal,
                    () =>
                    {
                        EditAppBarButton.Visibility = e.IsLoggedIn
                            ? Visibility.Visible
                            : Visibility.Collapsed;
                    }
                );
            }
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            _isVerificationOnlyFlow = false;

            ArticleNavigationParameter navParam = null;
            if (e.Parameter is string jsonParam)
            {
                try
                {
                    navParam = JsonConvert.DeserializeObject<ArticleNavigationParameter>(jsonParam);
                }
                catch
                {
                    Debug.WriteLine(
                        "Navigation parameter was not a valid JSON for ArticleNavigationParameter."
                    );
                }
            }
            else if (e.Parameter is ArticleNavigationParameter directParam)
            {
                navParam = directParam;
            }

            if (navParam != null)
            {
                _pageWikiContext = WikiManager.GetWikiById(navParam.WikiId);

                if (navParam.IsVerificationFlow)
                {
                    _isVerificationOnlyFlow = true;
                    _verificationUrl = navParam.PageTitle;
                    _pageTitleToFetch = "";
                }
                else
                {
                    string normalizedTitle = navParam.PageTitle.Replace(' ', '_');
                    if (_articleHistory.Count == 0 || _articleHistory.Peek() != normalizedTitle)
                    {
                        _articleHistory.Clear();
                        _articleHistory.Push(normalizedTitle);
                    }
                    _pageTitleToFetch = _articleHistory.Peek();
                }
            }

            var account =
                _pageWikiContext != null
                    ? AccountManager
                        .GetAccountsForWiki(_pageWikiContext.Id)
                        .FirstOrDefault(a => a.IsLoggedIn)
                    : null;
            EditAppBarButton.Visibility =
                account != null ? Visibility.Visible : Visibility.Collapsed;
            UpdateFavoriteButton();

            if (_isInitialized)
            {
                StartArticleFetch();
            }
        }

        protected void NavigateToInternalPage(string newTitle)
        {
            string normalizedTitle = newTitle.Replace(' ', '_');
            _pageTitleToFetch = normalizedTitle;
            _articleHistory.Push(_pageTitleToFetch);
            StartArticleFetch();
        }

        protected bool HandleSpecialLink(Uri uri)
        {
            if (_pageWikiContext == null || uri == null)
                return false;

            string pathAndQuery = uri.PathAndQuery.ToLowerInvariant();
            if (
                pathAndQuery.Contains("special:createaccount")
                || (
                    pathAndQuery.Contains("special:userlogin")
                    && pathAndQuery.Contains("type=signup")
                )
            )
            {
                Frame.Navigate(GetCreateAccountPageType(), _pageWikiContext.Id);
                return true;
            }

            if (
                pathAndQuery.Contains("special:login") || pathAndQuery.Contains("special:userlogin")
            )
            {
                Frame.Navigate(GetLoginPageType(), _pageWikiContext.Id);
                return true;
            }

            return false;
        }

        protected void Page_Loaded(object sender, RoutedEventArgs e)
        {
            DataTransferManager.GetForCurrentView().DataRequested += OnDataRequested;
            if (_isInitialized)
                return;

            InitializePlatformControls();
            _isInitialized = true;
            UpdateRefreshButtonVisibility();
            if (
                _pageWikiContext != null
                && (
                    !string.IsNullOrEmpty(_pageTitleToFetch)
                    || !string.IsNullOrEmpty(_verificationUrl)
                )
            )
            {
                StartArticleFetch();
            }
        }

        private void Page_Unloaded(object sender, RoutedEventArgs e)
        {
            DataTransferManager.GetForCurrentView().DataRequested -= OnDataRequested;
            AuthenticationService.AuthenticationStateChanged -= OnAuthenticationStateChanged;
            UninitializePlatformControls();
        }

        protected async void StartArticleFetch()
        {
            if (!string.IsNullOrEmpty(_verificationUrl))
            {
                ShowVerificationPanel(_verificationUrl);
                return;
            }
            if (
                !_isInitialized
                || _pageWikiContext == null
                || VerificationPanelGrid.Visibility == Visibility.Visible
            )
                return;

            if (_pageTitleToFetch.StartsWith("Special:", StringComparison.OrdinalIgnoreCase))
            {
                var dummyUri = new Uri(
                    $"{_pageWikiContext.BaseUrl.TrimEnd('/')}/{_pageWikiContext.ArticlePath.TrimStart('/')}{_pageTitleToFetch}"
                );
                if (HandleSpecialLink(dummyUri))
                {
                    if (_articleHistory.Any())
                    {
                        _articleHistory.Pop();
                    }
                    return;
                }
            }

            ReviewRequestService.IncrementPageLoadCount();
            ReviewRequestService.TryRequestReview();
            var fetchStopwatch = Stopwatch.StartNew();
            ShowLoadingOverlay();
            LastUpdatedTextBlock.Visibility = Visibility.Collapsed;

            var mainPage = this.FindParent<MainPageBase>();
            var displayTitle = _pageTitleToFetch.Replace('_', ' ');
            if (mainPage != null)
            {
                mainPage.SetPageTitle(displayTitle);
            }
            ArticleTitleTextBlock.Text = displayTitle;
            LoadingTextBlock.Text = $"Loading '{displayTitle}'...";

            try
            {
                var worker = await App.ApiWorkerFactory.CreateApiWorkerAsync(_pageWikiContext);
                var (htmlContent, resolvedTitle) =
                    await ArticleProcessingService.FetchAndProcessArticleAsync(
                        _pageTitleToFetch,
                        fetchStopwatch,
                        worker,
                        _pageWikiContext
                    );

                var doc = new HtmlDocument();
                doc.LoadHtml(htmlContent);

                var redirectNode = doc.DocumentNode.SelectSingleNode(
                    "//div[contains(@class, 'redirectMsg')]//a[@title]"
                );

                if (redirectNode != null)
                {
                    string newTitle = redirectNode.GetAttributeValue("title", null);
                    if (!string.IsNullOrEmpty(newTitle))
                    {
                        Debug.WriteLine(
                            $"HTML redirect detected. Navigating from '{_pageTitleToFetch}' to '{newTitle}'."
                        );
                        NavigateToInternalPage(newTitle);
                        return;
                    }
                }

                if (_pageTitleToFetch.Equals("Special:Random", StringComparison.OrdinalIgnoreCase))
                {
                    _pageTitleToFetch = resolvedTitle.Replace(' ', '_');
                    if (_articleHistory.Any())
                        _articleHistory.Pop();
                    _articleHistory.Push(_pageTitleToFetch);
                    ArticleTitleTextBlock.Text = resolvedTitle;
                    mainPage?.SetPageTitle(resolvedTitle);
                }

                var processedHtml = await ArticleProcessingService.BuildArticleHtmlAsync(
                    htmlContent,
                    _pageTitleToFetch,
                    _pageWikiContext
                );

                await DisplayProcessedHtmlAsync(processedHtml);
                EnhanceDisplayedHtmlWithCachedMediaAsync(htmlContent);
                await UpdateEditButtonForPageAsync();

                var lastUpdated = await ArticleProcessingService.FetchLastUpdatedTimestampAsync(
                    _pageTitleToFetch,
                    worker,
                    _pageWikiContext
                );
                if (lastUpdated.HasValue)
                {
                    LastUpdatedTextBlock.Text =
                        $"Last updated: {lastUpdated.Value.ToLocalTime():g}";
                    LastUpdatedTextBlock.Visibility = Visibility.Visible;
                }
            }
            catch (NeedsUserVerificationException ex)
            {
#if UWP_1809
                ShowVerificationPanel(ex.Url);

#else
                ArticleTitleTextBlock.Text = "Unable to load page";
                LoadingTextBlock.Text = "A security check is preventing access.";
                var dialog = new ContentDialog
                {
                    Title = "Verification Required",
                    Content =
                        "This site is protected by a security check that is incompatible with this version of WebView.\n\nPlease go to Settings -> Manage Wikis, edit this wiki, and switch its 'Connection Backend' to 'Proxy' to access this content.",
                    PrimaryButtonText = "OK",
                };
                await dialog.ShowAsync();
#endif
            }
            catch (Exception ex)
            {
                ArticleTitleTextBlock.Text = "An error occurred";
                LoadingTextBlock.Text = ex.Message;
            }
            finally
            {
                HideLoadingOverlay();
                UpdateFavoriteButton();
            }
        }

        protected async void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            string css = await ThemeManager.GetThemeCssAsync();
            string escapedCss = JsonConvert.ToString(css);
            string script =
                $@"
        var styleElement = document.getElementById('custom-theme-style');
        if (styleElement) {{
            styleElement.innerHTML = {escapedCss};
        }}";
            await ExecuteScriptInWebViewAsync(script);
        }

        public bool GoBackInPage()
        {
            if (CanGoBackInPage)
            {
                _articleHistory.Pop();
                _pageTitleToFetch = _articleHistory.Peek();
                StartArticleFetch();
                return true;
            }
            return false;
        }

        protected void EditButton_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(_pageTitleToFetch))
            {
                App.Navigate(
                    GetEditPageType(),
                    new ArticleNavigationParameter
                    {
                        WikiId = _pageWikiContext.Id,
                        PageTitle = _pageTitleToFetch,
                    }
                );
            }
        }

        protected async void FavoriteButton_Click(object sender, RoutedEventArgs e)
        {
            if (_pageWikiContext == null || string.IsNullOrEmpty(_pageTitleToFetch))
                return;

            var account = AccountManager
                .GetAccountsForWiki(_pageWikiContext.Id)
                .FirstOrDefault(a => a.IsLoggedIn);
            var authService =
                (account != null)
                    ? new AuthenticationService(account, _pageWikiContext, App.ApiWorkerFactory)
                    : null;

            if (FavouritesService.IsFavourite(_pageTitleToFetch, _pageWikiContext.Id))
            {
                await FavouritesService.RemoveFavoriteAsync(
                    _pageTitleToFetch,
                    _pageWikiContext.Id,
                    authService
                );
            }
            else
            {
                await FavouritesService.AddFavoriteAsync(
                    _pageTitleToFetch,
                    _pageWikiContext.Id,
                    authService
                );
            }
            UpdateFavoriteButton();
        }

        protected void UpdateFavoriteButton()
        {
            if (FavoriteAppBarButton == null || _pageWikiContext == null)
                return;
            if (
                string.IsNullOrEmpty(_pageTitleToFetch)
                || _pageTitleToFetch.Equals("Special:Random", StringComparison.OrdinalIgnoreCase)
            )
            {
                FavoriteAppBarButton.Visibility = Visibility.Collapsed;
                return;
            }
            FavoriteAppBarButton.Visibility = Visibility.Visible;
            if (FavouritesService.IsFavourite(_pageTitleToFetch, _pageWikiContext.Id))
            {
                FavoriteAppBarButton.Label = "Remove from Favourites";
                if (FavoriteAppBarButton.Icon is SymbolIcon icon)
                    icon.Symbol = Symbol.UnFavorite;
            }
            else
            {
                FavoriteAppBarButton.Label = "Add to Favourites";
                if (FavoriteAppBarButton.Icon is SymbolIcon icon)
                    icon.Symbol = Symbol.Favorite;
            }
        }

        protected void ShareButton_Click(object sender, RoutedEventArgs e)
        {
            DataTransferManager.ShowShareUI();
        }

        private void OnDataRequested(DataTransferManager sender, DataRequestedEventArgs args)
        {
            DataRequest request = args.Request;
            if (
                _pageWikiContext != null
                && !string.IsNullOrEmpty(_pageTitleToFetch)
                && !_pageTitleToFetch.Equals("Special:Random", StringComparison.OrdinalIgnoreCase)
            )
            {
                request.Data.Properties.Title = ArticleTitleTextBlock.Text;
                request.Data.Properties.Description =
                    $"Check out this article on {_pageWikiContext.Host}.";
                request.Data.SetWebLink(
                    new Uri(_pageWikiContext.GetWikiPageUrl(_pageTitleToFetch))
                );
            }
            else
            {
                request.FailWithDisplayText("There is no article loaded to share.");
            }
        }

        private async Task UpdateEditButtonForPageAsync()
        {
            System.Diagnostics.Debug.WriteLine(
                $"[EditCheck] Starting for '{_pageTitleToFetch}' on wiki '{_pageWikiContext?.Id}'"
            );

            if (_pageWikiContext == null || string.IsNullOrEmpty(_pageTitleToFetch))
            {
                System.Diagnostics.Debug.WriteLine(
                    "[EditCheck] Missing wiki context or page title — aborting."
                );
                return;
            }

            try
            {
                var worker = SessionManager.GetAnonymousWorkerForWiki(_pageWikiContext);

                var apiUrl =
                    $"{_pageWikiContext.ApiEndpoint}?action=query&meta=userinfo&uiprop=rights|groups"
                    + $"&prop=info&inprop=protection&titles={Uri.EscapeDataString(_pageTitleToFetch)}&format=json";

                System.Diagnostics.Debug.WriteLine($"[EditCheck] API URL: {apiUrl}");

                var json = await worker.GetJsonFromApiAsync(apiUrl);
                System.Diagnostics.Debug.WriteLine($"[EditCheck] JSON length: {json?.Length ?? 0}");

                dynamic result = Newtonsoft.Json.JsonConvert.DeserializeObject(json);

                var rightsToken = result?.query?.userinfo?.rights;
                var rights =
                    rightsToken != null
                        ? ((Newtonsoft.Json.Linq.JArray)rightsToken).Select(r => (string)r).ToList()
                        : new List<string>();

                System.Diagnostics.Debug.WriteLine(
                    $"[EditCheck] Rights: {(rights.Any() ? string.Join(", ", rights) : "(none)")}"
                );
                bool canEdit = rights.Contains("edit");
                System.Diagnostics.Debug.WriteLine($"[EditCheck] Has 'edit' right: {canEdit}");

                var pagesObj = result?.query?.pages as JObject;
                var page = pagesObj?.Properties().FirstOrDefault()?.Value as JObject;

                if (
                    page != null
                    && page.TryGetValue("protection", out var protectionToken)
                    && protectionToken is JArray protection
                )
                {
                    Debug.WriteLine($"[EditCheck] Raw protection JSON: {protection}");
                    Debug.WriteLine($"[EditCheck] Protection entries: {protection.Count}");

                    var editProt = protection.FirstOrDefault(p => (string)p["type"] == "edit");
                    if (editProt != null)
                    {
                        var level = (string)editProt["level"];
                        Debug.WriteLine($"[EditCheck] Edit restriction level: {level}");

                        var groupsToken = result?.query?.userinfo?.groups;
                        var groups =
                            groupsToken != null
                                ? ((JArray)groupsToken).Select(g => (string)g).ToList()
                                : new List<string>();

                        Debug.WriteLine(
                            $"[EditCheck] Groups: {(groups.Any() ? string.Join(", ", groups) : "(none)")}"
                        );
                        if (!groups.Contains(level))
                        {
                            Debug.WriteLine(
                                "[EditCheck] User does not meet restriction level — disabling edit."
                            );
                            canEdit = false;
                        }
                        else
                        {
                            Debug.WriteLine("[EditCheck] User meets restriction level.");
                        }
                    }
                    else
                    {
                        Debug.WriteLine("[EditCheck] No edit-specific protection found.");
                    }
                }
                else
                {
                    Debug.WriteLine("[EditCheck] No protection array present in API response.");
                }

                await Dispatcher.RunAsync(
                    Windows.UI.Core.CoreDispatcherPriority.Normal,
                    () =>
                    {
                        EditAppBarButton.Visibility = canEdit
                            ? Visibility.Visible
                            : Visibility.Collapsed;
                        EditAppBarButton.IsEnabled = canEdit;
                        System.Diagnostics.Debug.WriteLine(
                            $"[EditCheck] Edit button Visibility set to {EditAppBarButton.Visibility}, IsEnabled set to {canEdit}"
                        );
                    }
                );

                System.Diagnostics.Debug.WriteLine("[EditCheck] Completed successfully.");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[EditCheck] FAILED: {ex}");
                await Dispatcher.RunAsync(
                    Windows.UI.Core.CoreDispatcherPriority.Normal,
                    () =>
                    {
                        EditAppBarButton.Visibility = Visibility.Collapsed;
                        EditAppBarButton.IsEnabled = false;
                    }
                );
            }
        }

        protected async Task ShowImageViewerAsync(string filePageTitle)
        {
            var loadingDialog = new ContentDialog { Title = "Loading Image..." };
            var loadingTask = loadingDialog.ShowAsync();
            ImageMetadata metadata = null;

            try
            {
                metadata = ImageMetadataCacheService.GetMetadata(
                    _pageWikiContext.Id,
                    filePageTitle
                );

                if (metadata == null)
                {
                    Debug.WriteLine(
                        $"[ImageViewer] Metadata for '{filePageTitle}' not cached. Fetching from API."
                    );
                    metadata = await FetchImageMetadataFromApiAsync(filePageTitle);

                    if (metadata != null)
                    {
                        ImageMetadataCacheService.StoreMetadata(
                            _pageWikiContext.Id,
                            filePageTitle,
                            metadata
                        );
                    }
                }
                else
                {
                    Debug.WriteLine(
                        $"[ImageViewer] Loaded metadata for '{filePageTitle}' from cache."
                    );
                }

                if (metadata != null)
                {
                    loadingDialog.Hide();

                    var imageDialog = new ImageViewerDialog(metadata, _pageWikiContext);

                    await imageDialog.ShowAsync();
                }
                else
                {
                    loadingDialog.Hide();
                    throw new Exception(
                        "Failed to retrieve image metadata from both cache and API."
                    );
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ImageViewer] Failed to load image details: {ex.Message}");
                loadingDialog.Hide();

                var errorDialog = new ContentDialog
                {
                    Title = "Could Not Load Image",
                    Content =
                        $"There was a problem getting the image details. The file might be a video or another unsupported type.\n\nError: {ex.Message}",
                    PrimaryButtonText = "Open in Browser",
                    SecondaryButtonText = "Close",
                };
                var result = await errorDialog.ShowAsync();
                if (result == ContentDialogResult.Primary)
                {
                    await Windows.System.Launcher.LaunchUriAsync(
                        new Uri(_pageWikiContext.GetWikiPageUrl(filePageTitle))
                    );
                }
            }
        }

        private async Task<ImageMetadata> FetchImageMetadataFromApiAsync(string filePageTitle)
        {
            var worker = SessionManager.GetAnonymousWorkerForWiki(_pageWikiContext);

            string fullFileTitle = filePageTitle;
            if (
                !fullFileTitle.StartsWith("File:", StringComparison.OrdinalIgnoreCase)
                && !fullFileTitle.StartsWith("Image:", StringComparison.OrdinalIgnoreCase)
            )
            {
                fullFileTitle = "File:" + fullFileTitle;
            }

            string apiUrl =
                $"{_pageWikiContext.ApiEndpoint}?action=query&prop=imageinfo&iiprop=url|comment|user|timestamp&format=json&titles={Uri.EscapeDataString(fullFileTitle)}";
            string json = await worker.GetJsonFromApiAsync(apiUrl);

            if (string.IsNullOrEmpty(json))
                return null;

            var response = JObject.Parse(json);
            var pagesContainer = response?["query"]?["pages"] as JObject;
            var page = pagesContainer?.Properties().FirstOrDefault()?.Value as JObject;
            var imageInfo = page?["imageinfo"]?.FirstOrDefault();

            if (imageInfo == null)
            {
                var redirects = response?["query"]?["redirects"]?.FirstOrDefault();
                if (redirects != null)
                {
                    var newTitle = redirects["to"]?.ToString();
                    if (!string.IsNullOrEmpty(newTitle))
                    {
                        Debug.WriteLine(
                            $"[ImageViewer] Redirect detected from '{filePageTitle}' to '{newTitle}'. Retrying fetch..."
                        );
                        return await FetchImageMetadataFromApiAsync(newTitle);
                    }
                }
                return null;
            }

            var metadata = new ImageMetadata
            {
                PageTitle = page?["title"]?.ToString(),
                FullImageUrl = imageInfo["url"]?.ToString(),
                Description = imageInfo["comment"]?.ToString(),
                Author = imageInfo["user"]?.ToString(),
                Date = imageInfo["timestamp"]?.ToObject<DateTime>().ToString("g"),
                LicensingInfo = "Licensing information not available via API.",
            };

            if (!string.IsNullOrEmpty(metadata.Description) && metadata.Description.Contains("{{"))
            {
                try
                {
                    string parseApiUrl =
                        $"{_pageWikiContext.ApiEndpoint}?action=parse&text={Uri.EscapeDataString(metadata.Description)}&contentmodel=wikitext&prop=text&disablelimitreport=true&format=json";

                    string parsedJson = await worker.GetJsonFromApiAsync(parseApiUrl);

                    if (!string.IsNullOrEmpty(parsedJson))
                    {
                        var parsedResponse = JObject.Parse(parsedJson);
                        string parsedHtml = parsedResponse?["parse"]?["text"]?["*"]?.ToString();
                        if (!string.IsNullOrEmpty(parsedHtml))
                        {
                            var doc = new HtmlAgilityPack.HtmlDocument();
                            doc.LoadHtml(parsedHtml);
                            doc.DocumentNode.SelectNodes("//style|//script")
                                ?.ToList()
                                .ForEach(n => n.Remove());
                            metadata.Description = System.Net.WebUtility.HtmlDecode(
                                doc.DocumentNode.InnerText.Trim()
                            );
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine(
                        $"[ImageViewer] Could not parse wikitext description: {ex.Message}"
                    );
                }
            }

            string filePageUrl = _pageWikiContext.GetWikiPageUrl(fullFileTitle);
            string html = await worker.GetRawHtmlFromUrlAsync(filePageUrl);
            ParseImagePageHtml(html, metadata);

            return metadata;
        }

        private void ParseImagePageHtml(string html, ImageMetadata metadata)
        {
            if (string.IsNullOrEmpty(html) || metadata == null)
                return;

            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            doc.DocumentNode.SelectNodes("//style|//script")?.ToList().ForEach(n => n.Remove());

            var fullImageLinkNode =
                doc.DocumentNode.SelectSingleNode("//div[@class='fullImageLink']//a")
                ?? doc.DocumentNode.SelectSingleNode("//div[@class='fullMedia']//a");

            if (fullImageLinkNode != null)
            {
                string href = fullImageLinkNode.GetAttributeValue("href", string.Empty);
                if (!string.IsNullOrEmpty(href))
                {
                    try
                    {
                        var absoluteUri = new Uri(new Uri(_pageWikiContext.BaseUrl), href);
                        metadata.FullImageUrl = absoluteUri.ToString();
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine(
                            $"[ImageViewer] Failed to resolve relative image URL '{href}': {ex.Message}"
                        );
                    }
                }
            }
            var descriptionNode =
                doc.DocumentNode.SelectSingleNode(
                    "//td[@id='fileinfotpl_desc']/following-sibling::td[1]"
                )
                ?? doc.DocumentNode.SelectSingleNode(
                    "//div[contains(@class, 'description') and @lang='en']"
                )
                ?? doc.DocumentNode.SelectSingleNode("//div[@id='mw-imagepage-content']//p");
            if (descriptionNode != null)
            {
                string descText = System.Net.WebUtility.HtmlDecode(
                    descriptionNode.InnerText.Trim()
                );
                if (!string.IsNullOrWhiteSpace(descText) && descText != metadata.Description)
                {
                    metadata.Description = descText;
                }
            }

            var licenseNode =
                doc.DocumentNode.SelectSingleNode(
                    "//div[contains(@class, 'licensetpl_wrapper')]//div[contains(@class, 'rlicense-desc')]"
                ) ?? doc.DocumentNode.SelectSingleNode("//div[contains(@class, 'im-license')]");
            if (licenseNode != null)
            {
                metadata.LicensingInfo = System.Net.WebUtility.HtmlDecode(
                    licenseNode.InnerText.Trim().Replace("\n", " ").Replace("\t", " ")
                );
            }
        }

        protected async Task ShowWikiDetectionPromptAsync(Uri uri)
        {
            var dialog = new Controls.AddWikiPromptDialog(uri);
            var result = await dialog.ShowAsync();

            if (result == ContentDialogResult.Primary && dialog.NewWikiInstance != null)
            {
                await WikiManager.AddWikiAsync(dialog.NewWikiInstance);

                string articlePathPrefix = $"/{dialog.NewWikiInstance.ArticlePath}";
                if (
                    uri.AbsolutePath.StartsWith(
                        articlePathPrefix,
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                {
                    string newTitle = uri.AbsolutePath.Substring(articlePathPrefix.Length);

                    Frame.Navigate(
                        GetArticleViewerPageType(),
                        new ArticleNavigationParameter
                        {
                            WikiId = dialog.NewWikiInstance.Id,
                            PageTitle = newTitle,
                        }
                    );
                }
            }
            else
            {
                await Windows.System.Launcher.LaunchUriAsync(uri);
            }
        }
    }
}
