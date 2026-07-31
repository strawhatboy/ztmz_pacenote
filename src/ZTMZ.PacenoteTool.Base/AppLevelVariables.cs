using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Microsoft.Win32;


namespace ZTMZ.PacenoteTool.Base
{


    public class AppLevelVariables
    {
        private const string AppRegistryPath = @"Software\ZTMZ Next Generation Pacenote Tool";
        private const string AppConfigFolderRegistryValueName = "ZTMZHome";

        private string _appConfigFolder;
        public string AppConfigFolder {
            get {
                // if not found, use My Documents\My Games\ZTMZClub_nextgen
                if (string.IsNullOrEmpty(_appConfigFolder))
                {
                    // read from registry HKCU\Software\ZTMZ Next Generation Pacenote Tool\ZTMZHome
                    using var key = Registry.CurrentUser.OpenSubKey(AppRegistryPath);
                    _appConfigFolder = key?.GetValue(AppConfigFolderRegistryValueName) as string;

                    if (string.IsNullOrWhiteSpace(_appConfigFolder))
                    {
                        _appConfigFolder = Path.Combine(
                            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                            "My Games",
                            "ZTMZClub_nextgen");
                    }

                    Directory.CreateDirectory(_appConfigFolder);
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

        public void SetAppConfigFolderForNextLaunch(string folderPath)
        {
            if (string.IsNullOrWhiteSpace(folderPath))
            {
                throw new ArgumentException("A data-folder path is required.", nameof(folderPath));
            }

            var normalizedPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folderPath));
            if (!Directory.Exists(normalizedPath))
            {
                throw new DirectoryNotFoundException($"The data folder does not exist: {normalizedPath}");
            }

            using var key = Registry.CurrentUser.CreateSubKey(AppRegistryPath);
            if (key == null)
            {
                throw new InvalidOperationException("The application registry key could not be opened.");
            }

            key.SetValue(AppConfigFolderRegistryValueName, normalizedPath, RegistryValueKind.String);
        }
    }
}
