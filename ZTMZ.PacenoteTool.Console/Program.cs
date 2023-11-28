// See https://aka.ms/new-console-template for more information

using ZTMZ.PacenoteTool.Core;
using ZTMZ.PacenoteTool.Base;
using System;
namespace ZTMZ.PacenoteTool.Console;

public class Program
{
    private static NLog.Logger _logger = NLog.LogManager.GetCurrentClassLogger();
    public static void Main(string[] args)
    {
        
        // var jsonPaths = new List<string>{
        //         AppLevelVariables.Instance.GetPath(Constants.PATH_LANGUAGE),
        //         AppLevelVariables.Instance.GetPath(Path.Combine(Constants.PATH_GAMES, Constants.PATH_LANGUAGE)),
        //         AppLevelVariables.Instance.GetPath(Path.Combine(Constants.PATH_DASHBOARDS, Constants.PATH_LANGUAGE))
        //     };
        // I18NLoader.Instance.Initialize(jsonPaths);
        // I18NLoader.Instance.SetCulture(Config.Instance.Language);
        GoogleAnalyticsHelper.Instance.TrackLaunchEvent("language", Config.Instance.Language);

        
        NLogManager.init(ToolVersion.TEST);
        _logger.Info("Application started");

        ZTMZPacenoteTool tool = new();
        tool.onStatusReport += (s) => System.Console.WriteLine(s);
        tool.Init();
        tool.SetFromConfiguration();
        tool.SetGame(Config.Instance.UI_SelectedGame);
        while (true) {
            Thread.Sleep(2000);
        }
    }
}
