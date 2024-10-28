namespace ZTMZ.PacenoteTool.Base;

using System;
using System.Collections;
using System.Collections.Generic;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Extensibility;

[Obsolete("This class is deprecated, we will not collect data for analysis because of the unstability of providers")]
public class AzureAppInsightsManager {
    private TelemetryClient telemetryClient;
    public void init() {
        TelemetryConfiguration telemetryConfiguration = TelemetryConfiguration.CreateDefault();
        telemetryConfiguration.ConnectionString = "InstrumentationKey=6ca7ac93-0219-4c20-b7dd-758f1ecb2d5b;IngestionEndpoint=https://eastasia-0.in.applicationinsights.azure.com/;LiveEndpoint=https://eastasia.livediagnostics.monitor.azure.com/";
        telemetryClient = new TelemetryClient(telemetryConfiguration);
    }

    public AzureAppInsightsManager() {
        init();
    }

    public void TrackEvent(string eventName, Dictionary<string, string> properties = null, Dictionary<string, double> metrics = null) {
        return;
        if (Config.Instance.EnableOnlineAnalytics) {
            try {
                telemetryClient.TrackEvent(eventName, properties, metrics);
            } catch {}
        }
    }

    public void TrackException(Exception ex, Dictionary<string, string> properties = null, Dictionary<string, double> metrics = null) {
        return;
        if (Config.Instance.EnableOnlineAnalytics) {
            try {
                telemetryClient.TrackException(ex, properties, metrics);
            } catch {}
        }
    }

    public void TrackPageView(string pageName) {
        return;
        if (Config.Instance.EnableOnlineAnalytics) {
            try {
                telemetryClient.TrackPageView(pageName);
            } catch {}
        }
    }
}
