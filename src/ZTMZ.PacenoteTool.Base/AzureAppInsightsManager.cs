namespace ZTMZ.PacenoteTool.Base;

using System;
using System.Collections;
using System.Collections.Generic;
[Obsolete("This class is deprecated, we will not collect data for analysis because of the unstability of providers")]
public class AzureAppInsightsManager {
    public void init() { }

    public AzureAppInsightsManager() {
        init();
    }

    public void TrackEvent(string eventName, Dictionary<string, string> properties = null, Dictionary<string, double> metrics = null) { }

    public void TrackException(Exception ex, Dictionary<string, string> properties = null, Dictionary<string, double> metrics = null) { }

    public void TrackPageView(string pageName) { }
}
