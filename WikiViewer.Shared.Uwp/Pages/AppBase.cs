using System;
using System.Threading.Tasks;
using WikiViewer.Core;
using WikiViewer.Core.Interfaces;
using WikiViewer.Core.Managers;
using WikiViewer.Core.Services;
using WikiViewer.Shared.Uwp.Services;
using Windows.ApplicationModel;
using Windows.ApplicationModel.Activation;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;

namespace WikiViewer.Shared.Uwp
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
            this.InitializeComponent();
            this.Suspending += OnSuspending;
        }

        public static void ResetRootFrame()
        {
            SessionManager.IsResetPending = true;

            Frame rootFrame = new Frame();
            Window.Current.Content = rootFrame;
#if UWP_1507
            rootFrame.Navigate(typeof(_1507_UWP.Pages.MainPage));
#else
            rootFrame.Navigate(typeof(_1809_UWP.Pages.MainPage));
#endif
            Window.Current.Activate();
        }

        protected override async void OnLaunched(LaunchActivatedEventArgs e)
        {
            ReviewRequestService.IncrementLaunchCount();
            ReviewRequestService.Initialize();
#if UWP_1507
            ApiWorkerFactory = new _1507_UWP.Services.ApiWorkerFactory();
#else
            ApiWorkerFactory = new _1809_UWP.Services.ApiWorkerFactory();
#endif

            var storageProvider = new UwpStorageProvider();
            var settingsProvider = new UwpSettingsProvider();
            var credentialService = new CredentialService();

            AppSettings.SettingsProvider = settingsProvider;
            ArticleCacheManager.StorageProvider = storageProvider;
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
#if UWP_1507
                    rootFrame.Navigate(typeof(_1507_UWP.Pages.MainPage), e.Arguments);
#else
                    rootFrame.Navigate(typeof(_1809_UWP.Pages.MainPage), e.Arguments);
#endif
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
