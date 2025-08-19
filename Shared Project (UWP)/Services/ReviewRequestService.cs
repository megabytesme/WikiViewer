using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Windows.Services.Store;
using Windows.Storage;
using Windows.System;

public static class ReviewRequestService
{
    private static StoreContext _storeContext;
    private static readonly ApplicationDataContainer _localSettings = ApplicationData.Current.LocalSettings;

    private const string LaunchCountKey = "AppLaunchCount";
    private const string PageLoadCountKey = "PageLoadCount";
    private const string ReviewRequestShownKey = "ReviewRequestShown";

    public static void Initialize()
    {
        if (User.GetDefault() != null)
        {
            _storeContext = StoreContext.GetForUser(User.GetDefault());
        }
        else
        {
            _storeContext = StoreContext.GetDefault();
        }
    }

    public static void IncrementLaunchCount()
    {
        int launchCount = _localSettings.Values[LaunchCountKey] as int? ?? 0;
        _localSettings.Values[LaunchCountKey] = launchCount + 1;
        Debug.WriteLine($"[Review] App launch count incremented to: {launchCount + 1}");
    }

    public static void IncrementPageLoadCount()
    {
        int pageLoads = _localSettings.Values[PageLoadCountKey] as int? ?? 0;
        _localSettings.Values[PageLoadCountKey] = pageLoads + 1;
        Debug.WriteLine($"[Review] Page load count incremented to: {pageLoads + 1}");
    }

    public static async void TryRequestReview()
    {
        if (_storeContext == null)
        {
            Debug.WriteLine("[Review] Check skipped: StoreContext is not initialized.");
            return;
        }

        bool alreadyShown = _localSettings.Values[ReviewRequestShownKey] as bool? ?? false;
        int launchCount = _localSettings.Values[LaunchCountKey] as int? ?? 0;
        int pageLoadCount = _localSettings.Values[PageLoadCountKey] as int? ?? 0;

        Debug.WriteLine($"[Review] Checking conditions: alreadyShown={alreadyShown}, launchCount={launchCount}, pageLoadCount={pageLoadCount}. Required: alreadyShown=False, launchCount>=2, pageLoadCount>=3.");

        if (!alreadyShown && launchCount >= 2 && pageLoadCount >= 3)
        {
            _localSettings.Values[ReviewRequestShownKey] = true;
            Debug.WriteLine("[Review] CONDITIONS MET. 'alreadyShown' flag has been set to true.");

            Debug.WriteLine("[Review] Requesting review dialog from the Store service...");
            StoreRateAndReviewResult result = await _storeContext.RequestRateAndReviewAppAsync();

            switch (result.Status)
            {
                case StoreRateAndReviewStatus.Succeeded:
                    Debug.WriteLine("[Review] Store service reported: Succeeded. The user rated or reviewed the app.");
                    break;
                case StoreRateAndReviewStatus.CanceledByUser:
                    Debug.WriteLine("[Review] Store service reported: CanceledByUser. The user dismissed the dialog.");
                    break;
                case StoreRateAndReviewStatus.NetworkError:
                    Debug.WriteLine("[Review] Store service reported: NetworkError.");
                    break;
                case StoreRateAndReviewStatus.Error:
                default:
                    Debug.WriteLine($"[Review] Store service reported: OtherError. This is EXPECTED in a local debug session. Error: {result.ExtendedError}");
                    break;
            }
        }
        else
        {
            Debug.WriteLine("[Review] Conditions not met. Skipping request.");
        }
    }
}