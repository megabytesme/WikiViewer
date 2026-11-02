using System;
using System.Threading.Tasks;
using WikiViewer.Core;
using WikiViewer.Core.Interfaces;
using WikiViewer.Core.Managers;
using WikiViewer.Core.Services;
using WikiViewer.Uwp.Services;
using Windows.ApplicationModel;
using Windows.ApplicationModel.Activation;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;

namespace WikiViewer.Uwp
{
    public sealed partial class App : Application
    {
        public static Panel UIHost { get; set; }
        public static IApiWorkerFactory ApiWorkerFactory { get; private set; }
        private static readonly TaskCompletionSource<bool> _uiReadySignal =
            new TaskCompletionSource<bool>();
        public static Task UIReady => _uiReadySignal.Task;

        public static void SignalUIReady()
        {
            _uiReadySignal.TrySetResult(true);
        }

        public static event Action<Type, object> RequestNavigation;

        public static void Navigate(Type sourcePageType, object parameter) =>
            RequestNavigation?.Invoke(sourcePageType, parameter);

        public App()
        {
            this.UnhandledException += OnUnhandledException;
            this.InitializeComponent();
            this.Suspending += OnSuspending;
        }

        private void OnUnhandledException(object sender, Windows.UI.Xaml.UnhandledExceptionEventArgs e)
        {
            // Preserve diagnostics locally without hiding the original failure from Windows.
            try
            {
                var path = System.IO.Path.Combine(Windows.Storage.ApplicationData.Current.LocalFolder.Path, "unhandled-error.log");
                System.IO.File.WriteAllText(path, DateTime.UtcNow.ToString("O") + Environment.NewLine + e.Message + Environment.NewLine + e.Exception);
            }
            catch (Exception) { }
        }

        public static void ResetRootFrame()
        {
            SessionManager.IsResetPending = true;

            Frame rootFrame = new Frame();
            Window.Current.Content = rootFrame;
            rootFrame.Navigate(typeof(WikiViewer.Uwp.Pages.MainPage));

            Window.Current.Activate();
        }

        protected override async void OnLaunched(LaunchActivatedEventArgs e)
        {
            ReviewRequestService.IncrementLaunchCount();
            ReviewRequestService.Initialize();
            ApiWorkerFactory = new WikiViewer.Uwp.Services.ApiWorkerFactory();


            var storageProvider = new UwpStorageProvider();
            var settingsProvider = new UwpSettingsProvider();
            var credentialService = new CredentialService();

            AppSettings.SettingsProvider = settingsProvider;
            ArticlePresentation.GetThemeCssAsync = ThemeManager.GetThemeCssAsync;
            ArticlePresentation.IsDarkMode = () => Application.Current.RequestedTheme == ApplicationTheme.Dark;
            FavouritesService.StorageProvider = storageProvider;
            MediaCacheService.DirectWorkerFactory = () => new UwpHttpClientApiWorker();
            MediaCacheService.LocalUriFactory = path => "ms-appdata:///local" + path;
            ResolverConfigurationService.Current = new ResolverConfigurationService(settingsProvider, credentialService);
            ResolverConfigurationService.Current.Changed += (sender, args) =>
            {
                foreach (var wiki in WikiManager.GetWikis() ?? new System.Collections.Generic.List<WikiViewer.Core.Models.WikiInstance>()) wiki.ResolvedConnectionMethod = null;
            };
            ArticleCacheManager.StorageProvider = storageProvider;
            WikiResourceCache.Shared = new WikiResourceCache(storageProvider);
            ArticleProcessingService.ApiWorkerFactory = ApiWorkerFactory;
            ArticleLoadMetrics.StorageProvider = storageProvider;
            AccountManager.StorageProvider = storageProvider;
            AccountManager.CredentialService = credentialService;
            WikiManager.StorageProvider = storageProvider;

            MediaCacheService.ApiWorkerFactory = ApiWorkerFactory;
            MediaCacheService.StorageProvider = storageProvider;
            MediaCacheService.DispatcherInvoker = (func) =>
                Window.Current.Dispatcher.RunTaskAsync(func);

            ImageMetadataCacheService.StorageProvider = storageProvider;
            await ImageMetadataCacheService.InitializeAsync();

            SessionManager.ApiWorkerFactory = ApiWorkerFactory;
            SessionManager.CredentialService = credentialService;
            SessionManager.PlatformReady = UIReady;

            await WikiManager.InitializeAsync();
            await AccountManager.InitializeAsync();
            await FavouritesService.InitializeAsync();
            await MediaCacheService.InitializeAsync();

            Frame rootFrame = Window.Current.Content as Frame;
            if (rootFrame == null)
            {
                rootFrame = new Frame();
                rootFrame.NavigationFailed += OnNavigationFailed;
                Window.Current.Content = rootFrame;
            }

            if (e.PrelaunchActivated == false)
            {
                if (rootFrame.Content == null)
                {
                    rootFrame.Navigate(typeof(WikiViewer.Uwp.Pages.MainPage), e.Arguments);

                }
                Window.Current.Activate();
            }
        }

        void OnNavigationFailed(object sender, NavigationFailedEventArgs e) =>
            throw new Exception("Failed to load Page " + e.SourcePageType.FullName);

        private void OnSuspending(object sender, SuspendingEventArgs e) =>
            e.SuspendingOperation.GetDeferral().Complete();
    }
}
