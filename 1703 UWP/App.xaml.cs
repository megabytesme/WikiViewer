using Shared_Code;
using System;
using Windows.ApplicationModel;
using Windows.ApplicationModel.Activation;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;

namespace _1703_UWP
{
    sealed partial class App : Application
    {
        public static Panel UIHost { get; set; }
        public static event Action<Type, object> RequestNavigation;
        public static void Navigate(Type sourcePageType, object parameter)
        {
            RequestNavigation?.Invoke(sourcePageType, parameter);
        }

        public App()
        {
            this.InitializeComponent();
            this.Suspending += OnSuspending;
        }

        public static void ResetRootFrame()
        {
            Frame rootFrame = new Frame();
            Window.Current.Content = rootFrame;
            rootFrame.Navigate(typeof(MainPage));
            Window.Current.Activate();
        }

        protected override async void OnLaunched(LaunchActivatedEventArgs e)
        {
            ReviewRequestService.IncrementLaunchCount();
            ReviewRequestService.Initialize();
            await FavouritesService.InitializeAsync();
            await ArticleCacheManager.InitializeAsync();

            Frame rootFrame = Window.Current.Content as Frame;

            if (rootFrame == null)
            {
                rootFrame = new Frame();
                rootFrame.NavigationFailed += OnNavigationFailed;

                if (e.PreviousExecutionState == ApplicationExecutionState.Terminated)
                {
                    //TODO: Load state from previously suspended application
                }

                Window.Current.Content = rootFrame;
            }

            if (e.PrelaunchActivated == false)
            {
                if (rootFrame.Content == null)
                {
                    rootFrame.Navigate(typeof(MainPage), e.Arguments);
                }
                Window.Current.Activate();
            }
        }

        void OnNavigationFailed(object sender, NavigationFailedEventArgs e)
        {
            throw new Exception("Failed to load Page " + e.SourcePageType.FullName);
        }

        private void OnSuspending(object sender, SuspendingEventArgs e)
        {
            var deferral = e.SuspendingOperation.GetDeferral();
            deferral.Complete();
        }
    }
}