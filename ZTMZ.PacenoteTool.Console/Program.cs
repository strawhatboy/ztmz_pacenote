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
        ZTMZPacenoteTool tool = new();
        tool.onStatusReport += (s) => System.Console.WriteLine(s);
        tool.Init();
        tool.SetGame(tool.Games[Config.Instance.UI_SelectedGame]);
        while (true) {
            Thread.Sleep(2000);
        }
    }
}
