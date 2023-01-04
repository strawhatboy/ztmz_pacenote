using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using ZTMZ.PacenoteTool.Base;

namespace ZTMZ.PacenoteTool.AudioPackageManager
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        private void App_OnStartup(object sender, StartupEventArgs e)
        {
            initializeI18N();
        }

        private void initializeI18N()
        {
            
            // load from I18NPath
            var jsonPaths = new List<string>{
                AppLevelVariables.Instance.GetPath(Constants.PATH_LANGUAGE),
                AppLevelVariables.Instance.GetPath(Path.Combine(Constants.PATH_GAMES, Constants.PATH_LANGUAGE)),
                AppLevelVariables.Instance.GetPath(Path.Combine(Constants.PATH_DASHBOARDS, Constants.PATH_LANGUAGE))
            };
            I18NLoader.Instance.Initialize(jsonPaths);
            I18NLoader.Instance.SetCulture(Config.Instance.Language);
        }
    }
}
