using System;
using System.IO;
using System.Reflection;


namespace ZTMZ.PacenoteTool.Base
{


    public class AppLevelVariables
    {
        private string _appConfigFolder;
        public string AppConfigFolder => _appConfigFolder ?? (_appConfigFolder = Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.CommonDocuments), "ZTMZClub"));

        private static AppLevelVariables _instance;
        public static AppLevelVariables Instance => _instance ?? (_instance = new AppLevelVariables());

        public string GetPath(string path)
        {
#if RELEASE_GREEN
            return path;
#else
            return Path.Join(AppLevelVariables.Instance.AppConfigFolder, path);
#endif
        }

        public bool IsGreenVersion()
        {
#if RELEASE_GREEN
            return true;
#else
            return false;
#endif
        }
    }
}
