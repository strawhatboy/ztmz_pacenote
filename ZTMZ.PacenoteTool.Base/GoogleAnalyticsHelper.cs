
using GoogleAnalyticsTracker.Simple;

namespace ZTMZ.PacenoteTool.Base;
public class GoogleAnalyticsHelper
{
    private static string _googleAnalyticsId = "UA-222473351-1";

    private SimpleTracker _tracker;

    private static GoogleAnalyticsHelper _instance;
    public static GoogleAnalyticsHelper Instance
    {
        get
        {
            if (_instance == null)
            {
                _instance = new GoogleAnalyticsHelper();
            }
            return _instance;
        }
    }

    private GoogleAnalyticsHelper()
    {
        var osVersion = System.Environment.OSVersion;
        _tracker = new SimpleTracker(_googleAnalyticsId, new SimpleTrackerEnvironment(
            osVersion.Platform.ToString(), osVersion.Version.ToString(), osVersion.ServicePack
        ));
    }

    async public void TrackEvent(string category, string action, string label = null)
    {
        try
        {
            await _tracker.TrackEventAsync(category, action, label);
        }
        catch
        {

        }
    }

    async public void TrackPageView(string pageTitle, string pageUrl)
    {
        try
        {
            await _tracker.TrackPageViewAsync(pageTitle, pageUrl);
        }
        catch
        {

        }
    }
}
