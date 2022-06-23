using System;
using System.IO;
using System.Reflection;


namespace ZTMZ.PacenoteTool.Base
{


    public class AppLevelVariables
    {
        private string _appConfigFolder;
        public string AppConfigFolder => _appConfigFolder ?? (_appConfigFolder = Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "My Games\\ZTMZClub"));

        private static AppLevelVariables _instance;
        public static AppLevelVariables Instance => _instance ?? (_instance = new AppLevelVariables());

        public string GetPath(string path)
        {
#if RELEASE_PORTABLE
            return path;
#else
            return Path.Join(AppLevelVariables.Instance.AppConfigFolder, path);
#endif
        }

        public bool IsPortableVersion()
        {
#if RELEASE_PORTABLE
            return true;
#else
            return false;
#endif
        }
    }
}
