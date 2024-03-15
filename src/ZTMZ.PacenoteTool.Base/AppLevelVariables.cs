using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Microsoft.Win32;


namespace ZTMZ.PacenoteTool.Base
{


    public class AppLevelVariables
    {
        private string _appConfigFolder;
        public string AppConfigFolder {
            get {
                // if not found, use My Documents\My Games\ZTMZClub_nextgen
                if (string.IsNullOrEmpty(_appConfigFolder))
                {
                    // read from registry HKCU\Software\ZTMZ Next Generation Pacenote Tool\ZTMZHome
                    var key = Registry.CurrentUser.OpenSubKey(@"Software\ZTMZ Next Generation Pacenote Tool");
                    if (key != null)
                    {
                        _appConfigFolder = key.GetValue("ZTMZHome") as string;
                    }
                }

                return _appConfigFolder;
            }
        }

        private static AppLevelVariables _instance;
        public static AppLevelVariables Instance => _instance ?? (_instance = new AppLevelVariables());

        public static List<char> InvalidCharsForWindowsPath;

        static AppLevelVariables()
        {
            InvalidCharsForWindowsPath = new()
            {
                '*', '{', '}', '!', '"', '?'
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
