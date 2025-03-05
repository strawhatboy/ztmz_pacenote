// Another way to read pacenote
// from the memory
// (0x7EABA8) + 0x10, seems to be 0x440BBB0
// then (0x440BBB0) + 0x20 is the pacenotes count
// (0x440BBB0) + 0x24 is the pointer of pacenotes
// read pacenotes from (pointer of pacenotes) by pacenote count

//TODO: ScriptReader, ScriptResource should be changed to use sqlite files.

using Microsoft.Win32;
using ZTMZ.PacenoteTool.Base;
using ZTMZ.PacenoteTool.Base.Game;
using IniParser;
using IniParser.Model;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System;
using System.Text.RegularExpressions;
using System.Diagnostics;
using CsvHelper.Expressions;
using System.Threading.Tasks;
using Neo.IronLua;

namespace ZTMZ.PacenoteTool.RBR;

public class RBRGamePacenoteReader : BasePacenoteReader
{
    private static readonly NLog.Logger _logger = NLog.LogManager.GetCurrentClassLogger();
    public Dictionary<int, string> TrackNoNameMap = new();
    public Dictionary<int, string> TrackNoStageNameMap = new();
    public string RBRRootDir { get; private set; } = "";

    public static string WEATHER_SUFFIX_MORNING = "_M";
    public static string WEATHER_SUFFIX_EVENING = "_E";
    public static string WEATHER_SUFFIX_NOON = "_N";
    public static string WEATHER_SUFFIX_OVERCAST = "_O";
    public static string FILE_EXTENSION_DRIVELINE = ".dls";
    public static string FILE_EXTENSION_PACENOTE = ".pacenote";
    public static string FILE_EXTENSION_INI_PACENOTE = ".ini";
    private static List<string> DLS_FILE_SUFFIXES = new List<string>() { 
        WEATHER_SUFFIX_EVENING + FILE_EXTENSION_DRIVELINE,
        WEATHER_SUFFIX_MORNING + FILE_EXTENSION_DRIVELINE,
        WEATHER_SUFFIX_NOON + FILE_EXTENSION_DRIVELINE,
        WEATHER_SUFFIX_OVERCAST + FILE_EXTENSION_DRIVELINE,
    };
    public static string FILE_INI_PACENOTE = "pacenotes.ini";
    public static string BTB_TRACKS_DIR = "RX_CONTENT\\Tracks";
    private bool _hasCustomPacenote = false;
    private string _customPacenoteFolder = "";

    private string escapeTraceName(string traceName) {
        // escape special characters which cannot be used in windows filename/path in traceName
        traceName = string.Join("_", traceName.Split(Path.GetInvalidFileNameChars()));
        traceName = traceName.Replace("'", "_").Replace("`", "_");
        return traceName;
    }

    public bool IsUsingCustomPacenote(string traceName) {
        traceName = escapeTraceName(traceName);
        if (!_hasCustomPacenote) {
            return false;
        }
        // try the txt file inside the folder _customPacenoteFolder, there should be only one txt file.
        // either _default.txt or _latest.txt or _[CustomPacenoteFilename].txt
        var customPacenoteFolder = getCustomPacenoteFolder(traceName);
        if (!Directory.Exists(customPacenoteFolder)) {
            _logger.Warn("Custom pacenote folder {0} not found", customPacenoteFolder);
            return false;
        }
        var files = Directory.GetFiles(customPacenoteFolder, "*.txt");
        if (files.Length != 1) {
            return false;
        }

        var file = files.First();
        if (file.EndsWith("_default.txt")) {
            return false;
        }

        if (file.EndsWith("_latest.txt")) {
            // read the latest ini file
            var latestFile = Directory.GetFiles(customPacenoteFolder, "*.ini").OrderByDescending(f => new FileInfo(f).LastWriteTime).FirstOrDefault();
            if (latestFile != null) {
                if (latestFile.EndsWith("_default.ini")) {
                    return false;
                } else {
                    return true;
                }
            }
        }

        // get the custom pacenote filename
        var customPacenoteFilename = Path.GetFileNameWithoutExtension(file);
        if (customPacenoteFilename.StartsWith("mypacenote_")) {
            //
            customPacenoteFilename = customPacenoteFilename.Substring("mypacenote_".Length);
            // find the ini file
            var customPacenoteIniFile = Path.Join(customPacenoteFolder, customPacenoteFilename + ".ini");
            if (File.Exists(customPacenoteIniFile) && !customPacenoteFilename.EndsWith("_default")) {
                return true;
            }
        }

        return false;
    }
    public RBRGamePacenoteReader()
    {
        // 1. find RBR Root installation folder
        var key = Registry.LocalMachine.OpenSubKey("Software\\Wow6432Node\\Rallysimfans RBR");
        if (key != null)
        {
            var rootDir = key.GetValue("InstallPath") as string;
            if (rootDir != null) 
            {
                RBRRootDir = rootDir;
            } else {
                return;
            }
        } else 
        {
            // RSF rbr not installed.
            return;
        }


        var trackFilePath = Path.Join(RBRRootDir, "Maps\\Tracks.ini");
        if (!File.Exists(trackFilePath)) 
        {
            // RSF rbr not valid or uninstalled properly.
            return;
        }

        // 2. load Tracks.ini
        var parser = new FileIniDataParser();

        var personalDefPath = Path.Join(RBRRootDir, "rallysimfans_personal.ini");
        var personalDefNoNameMap = new Dictionary<int, string>();
        if (File.Exists(personalDefPath)) 
        {
            var personalDefData = parser.ReadFile(personalDefPath);
            foreach (SectionData section in personalDefData.Sections)
            {
                var mapSectionName = section.SectionName;
                if (mapSectionName.ToLower().Contains("stage")) 
                {
                    var mapNo = getIntegerFromString(mapSectionName);
                    var mapTrackName = "";  // the path

                    if (section.Keys.ContainsKey("name"))
                    {
                        mapTrackName = trimIniValue(section.Keys["name"]);
                    }

                    personalDefNoNameMap.Add(mapNo, mapTrackName);
                }
            }
        }

        IniData data = parser.ReadFile(trackFilePath);
        foreach (SectionData section in data.Sections)
        {
            var mapSectionName = section.SectionName;
            if (mapSectionName.ToLower().Equals("general")) 
            {
                continue;
            }
            var mapNo = getIntegerFromString(mapSectionName);
            var mapStageName = "";
            var mapTrackName = "";  // the path

            if (section.Keys.ContainsKey("StageName"))
            {
                mapStageName = trimIniValue(section.Keys["StageName"]);
            }
            if (section.Keys.ContainsKey("TrackName"))
            {
                mapTrackName = trimIniValue(section.Keys["TrackName"]);
            }

            TrackNoNameMap.Add(mapNo, mapTrackName);
            if (personalDefNoNameMap.ContainsKey(mapNo) && mapStageName != personalDefNoNameMap[mapNo]) 
            {
                _logger.Warn("Track {0} has different name in Tracks.ini and rallysimfans_personal.ini, using the name {1} in rallysimfans_personal.ini, instead of {2}", mapNo, personalDefNoNameMap[mapNo], mapStageName);
                mapStageName = personalDefNoNameMap[mapNo];
            }
            TrackNoStageNameMap.Add(mapNo, mapStageName);
        }

        data = parser.ReadFile(Path.Join(RBRRootDir, "Plugins\\NGPCarMenu.ini"));
        var defaultSection = data.Sections.FirstOrDefault(s => s.SectionName.Equals("Default"));
        if (defaultSection != null) 
        {
            if (defaultSection.Keys.ContainsKey("MyPacenotesPath"))
            {
                var customPacenotePath = trimIniValue(defaultSection.Keys["MyPacenotesPath"]);
                if (Directory.Exists(Path.Join(RBRRootDir, customPacenotePath)) && customPacenotePath != "Disabled")
                {
                    _hasCustomPacenote = true;
                    _customPacenoteFolder = Path.Join(RBRRootDir, customPacenotePath);
                }
            }
        }
    }

    private int getIntegerFromString(string s) 
    {
        if (string.IsNullOrEmpty(s)) 
        {
            return -1;
        }

        return int.Parse(new string(s.Where(char.IsDigit).ToArray()));
    }

    private string trimIniValue(string value) 
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        if (value.StartsWith("\""))
        {
            value = value.Substring(1);
        }

        if (value.EndsWith("\""))
        {
            value = value.Substring(0, value.Length - 1);
        }

        return value;
    }

    /// <summary>
    /// <param name="track">the track name with TrackNo inside, like [198]Sipirkakim II Snow</param>
    /// </summary>

    public override ScriptReader ReadPacenoteRecord(string profile, IGame game, string track)
    {
        // extract trackNo from track name
        var trackInfo = extractTrackNoAndName(track);
        var trackNo = trackInfo.Item1;
        var trackName = trackInfo.Item2;
        if (trackNo < 0)
        {
            return null;
        }

        var fileName = GetScriptFileForReplaying(profile, game, track);
        if (fileName.EndsWith(FILE_EXTENSION_PACENOTE)) {
            // *.pacenote
            return base.ReadPacenoteRecord(profile, game, track);
        } else {
            // should read pacenote from memory, even custom pacenote is loaded in memory.
            _logger.Info("Reading pacenote from memory for track {0}", track);
            bool useDefaultDef = false;
            if (_hasCustomPacenote) {
                _logger.Info("Custom pacenote could be loaded in memory");
                var isUsingCustomPacenote = this.IsUsingCustomPacenote(trackName);
                _logger.Info("Is using custom pacenote: {0}", isUsingCustomPacenote);
                if (isUsingCustomPacenote) {
                    // load custom pacenote definition if default pacenote is not used
                    var pacenoteDef = ((CommonGameConfigs)game.GameConfigurations[CommonGameConfigs.Name])["game.rbr.additional_settings.additional_pacenote_def"].ToString();
                    if (RBRScriptResource.Instance.DBPath != pacenoteDef) {
                        Task.Run(() => RBRScriptResource.Instance.LoadData(pacenoteDef)).Wait();
                    }
                } else {
                    useDefaultDef = true;
                }
            } else {
                useDefaultDef = true;
            }
            if (useDefaultDef) {
                game.OnCustomMessage(1, string.Format(I18NLoader.Instance["game.rbr.message.custom_pacenote_not_used"], track));
                _logger.Info("Custom pacenote is not used, load default pacenote definition");
                var pacenoteDef = AppLevelVariables.Instance.GetPath(Path.Combine(Constants.PATH_GAMES, "default.zdb"));    // default pacenote
                if (RBRScriptResource.Instance.DBPath != pacenoteDef) {
                    Task.Run(() => RBRScriptResource.Instance.LoadData(pacenoteDef)).Wait();
                }
            }
            var rbrMemReader = ((RBRGameDataReader)game.GameDataReader).memDataReader;

            var sr = rbrMemReader.ReadPacenotesFromMemory();
            if (sr.PacenoteRecords.Count > 0) 
            {
                _logger.Info("Pacenotes from memory found for track {0}, {1} records", track, sr.PacenoteRecords.Count);
                return sr;
            }

            if (fileName.EndsWith(FILE_EXTENSION_INI_PACENOTE)) {
                _logger.Info("Reading pacenote from ini file {0} for track {1}", fileName, track);
                return ReadPacenoteRecordFromIniFile(fileName);
            }

            if (fileName.EndsWith(FILE_EXTENSION_DRIVELINE)) 
            {
                return ReadPacenoteRecordFromDLSFile(fileName);
            }

            return null;    
        }
        
    }

    private Tuple<int, string> extractTrackNoAndName(string track)
    {
        var trackNoEnd = track.IndexOf("]");
        var trackNoStart = track.IndexOf("[");
        var trackNo = getIntegerFromString(track.Substring(trackNoStart + 1, trackNoEnd - trackNoStart - 1));
        var trackName = track.Substring(trackNoEnd + 1);
        return new Tuple<int, string>(trackNo, trackName);
    }

    public ScriptReader ReadPacenoteRecordFromIniFile(string iniFile) 
    {
        if (!File.Exists(iniFile))
        {
            return null;
        }

        var dynamicRecords = new List<DynamicPacenoteRecord>();
        var parser = new FileIniDataParser();
        IniData data = parser.ReadFile(iniFile);

        foreach (var section in data.Sections)
        {
            if (Regex.IsMatch(section.SectionName, "^P[0-9]+$"))
            {
                int type = -1;
                float distance = 0;
                int flag = 0;
                if (section.Keys.ContainsKey("type"))
                {
                    type = int.Parse(section.Keys["type"]);
                }

                if (section.Keys.ContainsKey("distance"))
                {
                    distance = float.Parse(section.Keys["distance"]);
                }

                if (section.Keys.ContainsKey("flag"))
                {
                    flag = int.Parse(section.Keys["flag"]);
                }

                var record = GetDynamicPacenoteRecord(type, flag, distance);

                if (record != null && !string.IsNullOrEmpty(record.Pacenote))
                {
                    dynamicRecords.Add(record);
                }
            }
        }

        dynamicRecords.Sort((a, b) => a.Distance.CompareTo(b.Distance));

        return ScriptReader.ReadFromDynamicPacenoteRecords(dynamicRecords);
    }
    
    public ScriptReader ReadPacenoteRecordFromDLSFile(string dlsFile) 
    {
        if (!File.Exists(dlsFile))
        {
            return null;
        }

        var dynamicRecords = new List<DynamicPacenoteRecord>();
        using (var fs = File.OpenRead(dlsFile))
        {
            fs.Seek(0x38, SeekOrigin.Begin);
            var offsetType = fs.ReadByte() == 1 ? 0 : 1;

            var offsetFingerPrint = new byte[0x0C];
            var numOfPacenoteRecords = 0;

            var numOfPacenoteOffset = 0x5C;
            while (numOfPacenoteOffset < 0x200)
            {
                fs.Seek(numOfPacenoteOffset, SeekOrigin.Begin);
                var read = fs.Read(offsetFingerPrint, 0, 0x0C);
                // seek back
                fs.Seek(numOfPacenoteOffset, SeekOrigin.Begin);
                if (read < 3)
                {
                    numOfPacenoteRecords = 0;
                    break;
                }

                if (offsetType == 0 || (offsetFingerPrint[0] != 0x00 && offsetFingerPrint[0x04] == 0x00 && offsetFingerPrint[0x08] == 0x1C))
                {
                    numOfPacenoteRecords = BitConverter.ToInt32(offsetFingerPrint.Take(4).ToArray(), 0);
                    break;
                }

                numOfPacenoteOffset += 0x04;
            }

            if (numOfPacenoteRecords <= 0 || numOfPacenoteRecords >= 50000)
            {
                _logger.Error("Invalid number of pacenote records: " + numOfPacenoteRecords);
                return null;
            }

            var dataOffset = numOfPacenoteOffset + 0x20;
            fs.Seek(dataOffset, SeekOrigin.Begin);
            var dword = new byte[4];
            var dataOffsetRead = fs.Read(dword, 0, 4);
            fs.Seek(dataOffset, SeekOrigin.Begin);
            if (dataOffsetRead != 4)
            {
                _logger.Error("Failed to read data offset");
                return null;
            }

            var dataOffsetValue = BitConverter.ToInt32(dword, 0);
            // read pacenotes
            for (var i = 0; i < numOfPacenoteRecords; i++)
            {
                var offset = dataOffsetValue + i * 0x0C; // 32-bit * 4
                fs.Seek(offset, SeekOrigin.Begin);
                dword = new byte[0x0C];
                var pacenoteRecordRead = fs.Read(dword, 0, 0x0C);
                fs.Seek(offset, SeekOrigin.Begin);

                if (pacenoteRecordRead < 0x0C)
                {
                    break;
                }

                var pacenoteType = BitConverter.ToInt32(dword, 0);
                var flag = BitConverter.ToInt32(dword, 0x04);
                var distance = BitConverter.ToSingle(dword, 0x08);
                var record = GetDynamicPacenoteRecord(pacenoteType, flag, distance);

                if (record != null)
                {
                    dynamicRecords.Add(record);
                }
            }
        }

        dynamicRecords.Sort((a, b) => a.Distance.CompareTo(b.Distance));

        return ScriptReader.ReadFromDynamicPacenoteRecords(dynamicRecords);
    }

    private static string getModifiersFromFlag(int flag)
    {
        var modifiers = new List<string>();
        if (flag >= 32768) {
            // too big, unknown flag
            return "";
        }
        foreach (var kv in RBRScriptResource.Instance.ModiferId2ZTMZids)
        {
            if ((flag & kv.Key) == kv.Key)
            {
                var ztmzIds = kv.Value;
                //it's a list!!! use comma separated string
                modifiers.Add(string.Join(",", from p in ztmzIds select Base.Script.ScriptResource.Instance.FilenameDict[p].First()));
            }
        }
        return string.Join(",", modifiers);
    }

    public static DynamicPacenoteRecord GetDynamicPacenoteRecord(int ptype, int pflag, float pdistance)
    {
        if (ptype < 0) {
            // wtf is this.
            return null;
        }
        DynamicPacenoteRecord record = new DynamicPacenoteRecord();
        // remove ID_2_PACENOTE
        if (RBRScriptResource.Instance.PacenoteId2ZTMZIds.ContainsKey(ptype))
        {
            var ztmzIds = RBRScriptResource.Instance.PacenoteId2ZTMZIds[ptype];
            record.Pacenote = string.Join(",", from p in ztmzIds select Base.Script.ScriptResource.Instance.FilenameDict[p].First());
        } else {
            // log warning
            _logger.Warn("Pacenote mapping {0} from RBR to ZTMZ not found, along with flag {1} and distance {2}", ptype, pflag, pdistance);
            if (RBRScriptResource.Instance.PacenotesDict.ContainsKey(ptype)) {
                _logger.Warn("Pacenote type {0} found in RBRScriptResource, which is {1}, but not mapped to ZTMZ", ptype, RBRScriptResource.Instance.PacenotesDict[ptype].First().name);
            } else {
                _logger.Warn("Pacenote type {0} not found in RBRScriptResource", ptype);
            }
        }
        record.Modifier = getModifiersFromFlag(pflag);
        record.Distance = pdistance;

        if (!string.IsNullOrEmpty(record.Pacenote))
        {
            return record;
        }

        return null;
    }

    private string getCustomPacenoteFolder(string trackName)
    {
        var customPacenoteFolder = Path.Join(_customPacenoteFolder, trackName);
        if (!Directory.Exists(customPacenoteFolder)) {
            customPacenoteFolder = Path.Join(_customPacenoteFolder, trackName + " BTB"); // maybe it's a BTB track
        }
        return customPacenoteFolder;
    }

    public override string GetScriptFileForReplaying(string profile, IGame game, string track, bool fallbackToDefault = true)
    {
        // 0. try our pacenotes
        var basePacenoteFile = base.GetScriptFileForReplaying(profile, game, track, fallbackToDefault);
        if (!string.IsNullOrEmpty(basePacenoteFile))
        {
            return basePacenoteFile;
        }

        var trackInfo = extractTrackNoAndName(track);
        var trackNo = trackInfo.Item1;
        var trackName = trackInfo.Item2;
        // 1. try custom pacenote file
        if (_hasCustomPacenote) {
            var customPacenoteFolder = getCustomPacenoteFolder(trackName);

            if (Directory.Exists(customPacenoteFolder)) {
                // load the latest file
                var files = Directory.GetFiles(customPacenoteFolder, "*.ini");
                var latestFile = files.Select(f => new FileInfo(f)).OrderByDescending(f => f.LastWriteTime).FirstOrDefault();
                if (latestFile != null) {
                    return latestFile.FullName;
                }
            }
        }

        if (!TrackNoNameMap.ContainsKey(trackNo))
        {
            // BTB track
            return Path.Join(RBRRootDir, BTB_TRACKS_DIR, trackName, FILE_INI_PACENOTE);
        } else {
            // DLS track
            foreach (var suffix in DLS_FILE_SUFFIXES)
            {
                // we dont care about the weather
                var dlsFile = Path.Join(RBRRootDir, TrackNoNameMap[trackNo] + suffix);
                if (File.Exists(dlsFile))
                {
                    return dlsFile;
                }
            }
            return "";
        }
    }

    public override string GetScriptFileForRecording(string profile, IGame game, string track)
    {
        return base.GetScriptFileForRecording(profile, game, track);
    }

    public string GetTrackNameFromConfigById(int trackId)
    {
        if (TrackNoStageNameMap.ContainsKey(trackId))
        {
            return TrackNoStageNameMap[trackId];
        }
        return "";
    }
}
