#nullable enable
using System.Collections.Generic;
using GoogleAnalyticsTracker.Simple;

namespace ZTMZ.PacenoteTool.Base;
public class GoogleAnalyticsHelper
{
    public static string EVENT_WINDOW = "window";
    public static string EVENT_DIALOG = "dialog";
    public static string EVENT_LAUNCH = "launch";
    public static string EVENT_RACE = "race";
    public static string EVENT_RECORD = "record";
    public static string EVENT_CONFIG_TOGGLE = "config";
    public static string EVENT_EXCEPTION = "exception";
    public static string LABEL_DIALOG_CONFIRMED = "confirmed";
    public static string LABEL_DIALOG_CANCELED = "canceled";
    public static string LABEL_CONFIG_TOGGLE_ON = "on";
    public static string LABEL_CONFIG_TOGGLE_OFF = "off";
    private static string _googleAnalyticsId = "UA-222473351-1";

    private SimpleTracker _tracker;

    private static GoogleAnalyticsHelper? _instance;
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
    public void TrackDialogEventConfirmed(string dialogName,
        IDictionary<int, string?>? customDimensions = null,
        IDictionary<int, long?>? customMetrics = null) => 
        TrackDialogEvent(dialogName, LABEL_DIALOG_CONFIRMED, customDimensions, customMetrics);

    public void TrackDialogEventCancelled(string dialogName,
        IDictionary<int, string?>? customDimensions = null,
        IDictionary<int, long?>? customMetrics = null) => 
        TrackDialogEvent(dialogName, LABEL_DIALOG_CANCELED, customDimensions, customMetrics);

    public void TrackDialogEvent(string dialogName, string dialogAction, 
        IDictionary<int, string?>? customDimensions = null,
        IDictionary<int, long?>? customMetrics = null) => 
        TrackEvent(EVENT_DIALOG, dialogName, dialogAction, customDimensions, customMetrics);
    public void TrackWindowEvent(string windowName, string action, 
        IDictionary<int, string?>? customDimensions = null,
        IDictionary<int, long?>? customMetrics = null) => 
        TrackEvent(EVENT_WINDOW, windowName, action, customDimensions, customMetrics);
    public void TrackLaunchEvent(string launchMode, string version, 
        IDictionary<int, string?>? customDimensions = null,
        IDictionary<int, long?>? customMetrics = null) => 
        TrackEvent(EVENT_LAUNCH, launchMode, version, customDimensions, customMetrics);
    public void TrackRaceEvent(string raceStatus, string codriver="",
        IDictionary<int, string?>? customDimensions = null,
        IDictionary<int, long?>? customMetrics = null) => 
        TrackEvent(EVENT_RACE, raceStatus, codriver, customDimensions, customMetrics);
    public void TrackRecordEvent(string recordType, string recordAction,
        IDictionary<int, string?>? customDimensions = null,
        IDictionary<int, long?>? customMetrics = null) => 
        TrackEvent(EVENT_RECORD, recordType, recordAction, customDimensions, customMetrics);
    public void TrackConfigToggleEvent(string configName, string onOrOff,
        IDictionary<int, string?>? customDimensions = null,
        IDictionary<int, long?>? customMetrics = null) => 
        TrackEvent(EVENT_CONFIG_TOGGLE, configName, onOrOff, customDimensions, customMetrics);
    public void TrackConfigToggleEventOn(string configName,
        IDictionary<int, string?>? customDimensions = null,
        IDictionary<int, long?>? customMetrics = null) => 
        TrackConfigToggleEvent(configName, LABEL_CONFIG_TOGGLE_ON, customDimensions, customMetrics);
    public void TrackConfigToggleEventOff(string configName,
        IDictionary<int, string?>? customDimensions = null,
        IDictionary<int, long?>? customMetrics = null) => 
        TrackConfigToggleEvent(configName, LABEL_CONFIG_TOGGLE_OFF, customDimensions, customMetrics);
    public void TrackExceptionEvent(string exceptionName, string exceptionContent,
        IDictionary<int, string?>? customDimensions = null,
        IDictionary<int, long?>? customMetrics = null) => 
        TrackEvent(EVENT_EXCEPTION, exceptionName, exceptionContent, customDimensions, customMetrics);

    async public void TrackEvent(string category, string action, string label, 
        IDictionary<int, string?>? customDimensions = null,
        IDictionary<int, long?>? customMetrics = null,
        long value = 1)
    {
        try
        {
            if (Config.Instance.EnableOnlineAnalytics)
                await _tracker.TrackEventAsync(category, action, label, customDimensions, customMetrics, value);
        }
        catch
        {

        }
    }

    async public void TrackPageView(string pageTitle, string pageUrl,
        IDictionary<int, string?>? customDimensions = null,
        IDictionary<int, long?>? customMetrics = null)
    {
        try
        {
            if (Config.Instance.EnableOnlineAnalytics)
                await _tracker.TrackPageViewAsync(pageTitle, pageUrl, customDimensions, customMetrics);
        }
        catch
        {

        }
    }
}
