using System;
using Windows.Storage;
#if UWP_1809
using Windows.Services.Store;
using Windows.System;
#endif

namespace WikiViewer.Shared.Uwp.Services
{
    public static class ReviewRequestService
    {
#if UWP_1809
        private static StoreContext _storeContext;
#endif

        private static readonly ApplicationDataContainer _localSettings = ApplicationData
            .Current
            .LocalSettings;
        private const string LaunchCountKey = "AppLaunchCount";
        private const string PageLoadCountKey = "PageLoadCount";
        private const string ReviewRequestShownKey = "ReviewRequestShown";

        public static bool CanRequestReview
        {
            get
            {
                bool alreadyShown = _localSettings.Values[ReviewRequestShownKey] as bool? ?? false;
                int launchCount = _localSettings.Values[LaunchCountKey] as int? ?? 0;
                int pageLoadCount = _localSettings.Values[PageLoadCountKey] as int? ?? 0;

                return !alreadyShown && launchCount >= 2 && pageLoadCount >= 5;
            }
        }

        public static bool HasRequestedReview
        {
            get
            {
                return _localSettings.Values[ReviewRequestShownKey] as bool? ?? false;
            }
            set
            {
                _localSettings.Values[ReviewRequestShownKey] = value;
            }
        }

        public static void Initialize()
        {
#if UWP_1809
            _storeContext =
                User.GetDefault() != null
                    ? StoreContext.GetForUser(User.GetDefault())
                    : StoreContext.GetDefault();
#endif
        }

        public static void IncrementLaunchCount()
        {
            int launchCount = _localSettings.Values[LaunchCountKey] as int? ?? 0;
            _localSettings.Values[LaunchCountKey] = launchCount + 1;
        }

        public static void IncrementPageLoadCount()
        {
            int pageLoads = _localSettings.Values[PageLoadCountKey] as int? ?? 0;
            _localSettings.Values[PageLoadCountKey] = pageLoads + 1;
        }

        public static async void RequestReview()
        {
#if UWP_1809
            if (_storeContext == null)
                return;
#endif
            _localSettings.Values[ReviewRequestShownKey] = true;
#if UWP_1809
                await _storeContext.RequestRateAndReviewAppAsync();
#elif UWP_1507
            string storeId = "9nxgg8m4xf48";
            var reviewUri = new Uri($"ms-windows-store://review/?ProductId={storeId}");
            await Windows.System.Launcher.LaunchUriAsync(reviewUri);
#endif
        }

        public static void TryRequestReview()
        {
            if (CanRequestReview)
            {
                RequestReview();
            }
        }
    }
}
