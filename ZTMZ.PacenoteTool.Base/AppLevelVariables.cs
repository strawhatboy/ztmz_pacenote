using System;
using System.Collections.Generic;
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

        public static List<char> InvalidCharsForWindowsPath;

        static AppLevelVariables()
        {
            InvalidCharsForWindowsPath = new()
            {
                '*', '(', ')', '!', '"', '?'
            };
        }

        public string GetPath(string path)
        {
            // remove invalid characters
            foreach (var c in InvalidCharsForWindowsPath)
            {
                path = path.Replace(c, '_');
            }
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
