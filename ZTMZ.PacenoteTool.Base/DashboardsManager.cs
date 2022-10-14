using System;
using System.Collections.Generic;
using ZTMZ.PacenoteTool.Base.Game;

namespace ZTMZ.PacenoteTool.Base;

public class Dashboard 
{
    public CommonGameConfigs Configurations { set; get; }
}

public class DashboardsManager
{
    private static DashboardsManager _instance;
    public static DashboardsManager Instance { 
        get {
            if (_instance == null)
                _instance = new DashboardsManager();
            return _instance;
        }
    }

    public List<Dashboard> Dashboards { set; get; } = new List<Dashboard>();

    private DashboardsManager()
    {
    }

    private List<Dashboard> loadDashboards()
    {
        
        return null;
    }
}
