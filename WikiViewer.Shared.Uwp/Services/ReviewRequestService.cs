using System;
using System.Diagnostics;
using Windows.Services.Store;
using Windows.Storage;
#if UWP_1809
using Windows.System;
#endif

namespace WikiViewer.Shared.Uwp.Services
{
    public static class ReviewRequestService
    {
        private static StoreContext _storeContext;
        private static readonly ApplicationDataContainer _localSettings = ApplicationData
            .Current
            .LocalSettings;
        private const string LaunchCountKey = "AppLaunchCount";
        private const string PageLoadCountKey = "PageLoadCount";
        private const string ReviewRequestShownKey = "ReviewRequestShown";

        public static void Initialize()
        {
#if UWP_1809
            _storeContext =
                User.GetDefault() != null
                    ? StoreContext.GetForUser(User.GetDefault())
                    : StoreContext.GetDefault();
#else
            _storeContext = StoreContext.GetDefault();
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

        public static async void TryRequestReview()
        {
            if (_storeContext == null)
                return;
            bool alreadyShown = _localSettings.Values[ReviewRequestShownKey] as bool? ?? false;
            int launchCount = _localSettings.Values[LaunchCountKey] as int? ?? 0;
            int pageLoadCount = _localSettings.Values[PageLoadCountKey] as int? ?? 0;

            if (!alreadyShown && launchCount >= 2 && pageLoadCount >= 3)
            {
                _localSettings.Values[ReviewRequestShownKey] = true;
#if UWP_1809
                await _storeContext.RequestRateAndReviewAppAsync();
#endif
            }
        }
    }
}
