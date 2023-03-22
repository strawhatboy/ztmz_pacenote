using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;

namespace VRGameOverlay.VROverlayWindow
{
    /// <summary>
    /// Configuration for the VR Settings Form
    /// </summary>
    public class VROverlayConfiguration
    {
        public string HighlightColor { get; set; } = "#FFFF00"; //yellow

        //public List<VROverlaySettings.HotKeyMapping> HotKeys { get; set; } = new List<VROverlaySettings.HotKeyMapping>();
        public List<VROverlayWindow> Windows { get; set; } = new List<VROverlayWindow>();

        /// <summary>
        /// File location where this instance was loaded
        /// </summary>
        [JsonIgnore]
        public FileInfo FileInfo { get; set; }

        static readonly DirectoryInfo MyDocuments;
        static readonly FileInfo OldSettingsFile;
        static readonly FileInfo DefaultFile;

        static VROverlayConfiguration()
        {
            MyDocuments = new DirectoryInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "CrewChiefV4"));
            OldSettingsFile = new FileInfo(Path.Combine(MyDocuments.FullName, "vr_overlay_windows.json"));
            DefaultFile = new FileInfo(Path.Combine(MyDocuments.FullName, "CrewChiefV4.vrconfig.json"));
        }

        private void Save(FileInfo file)
        {

        }

        /// <summary>
        /// Save the settings file to the Default Location
        /// </summary>
        public void Save()
        {
            Save(FileInfo);
        }

        /// <summary>
        /// Load the settings file from the default location
        /// </summary>
        /// <returns></returns>
        public static VROverlayConfiguration FromFile()
        {
            return FromFile(DefaultFile);
        }


        /// <summary>
        /// Helper function to handle migration from old file type
        /// </summary>
        /// <param name="fileInfo"></param>
        /// <returns></returns>
        public static VROverlayConfiguration FromFile(FileInfo fileInfo)
        {
            return null;
        }
    }
}
