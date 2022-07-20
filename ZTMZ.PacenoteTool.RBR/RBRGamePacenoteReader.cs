// Another way to read pacenote
// from the memory
// (0x7EABA8) + 0x10, seems to be 0x440BBB0
// then (0x440BBB0) + 0x20 is the pacenotes count
// (0x440BBB0) + 0x24 is the pointer of pacenotes
// read pacenotes from (pointer of pacenotes) by pacenote count


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

namespace ZTMZ.PacenoteTool.RBR;

public class RBRGamePacenoteReader : BasePacenoteReader
{
    public Dictionary<int, string> TrackNoNameMap = new();
    public Dictionary<int, string> TrackNoStageNameMap = new();
    public string RBRRootDir { get; private set; } = "";

    public static string WEATHER_SUFFIX_MORNING = "_M";
    public static string WEATHER_SUFFIX_EVENING = "_E";
    public static string WEATHER_SUFFIX_NOON = "_N";
    public static string WEATHER_SUFFIX_OVERCAST = "_O";
    public static string FILE_EXTENSION_DRIVELINE = ".dls";
    public static string FILE_EXTENSION_PACENOTE = ".pacenote";
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
            // HU rbr not installed.
            return;
        }

        // 2. load Tracks.ini
        var parser = new FileIniDataParser();
        IniData data = parser.ReadFile(Path.Join(RBRRootDir, "Maps\\Tracks.ini"));
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
            TrackNoStageNameMap.Add(mapNo, mapStageName);
        }

        data = parser.ReadFile(Path.Join(RBRRootDir, "Plugins\\NGPCarMenu.ini"));
        var defaultSection = data.Sections.FirstOrDefault(s => s.SectionName.Equals("Default"));
        if (defaultSection != null) 
        {
            if (defaultSection.Keys.ContainsKey("MyPacenotesPath"))
            {
                var customPacenotePath = trimIniValue(defaultSection.Keys["MyPacenotesPath"]);
                if (Directory.Exists(Path.Join(RBRRootDir, customPacenotePath)))
                {
                    _hasCustomPacenote = true;
                    _customPacenoteFolder = customPacenotePath;
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
            // read from Memory first
            var rbrMemReader = ((RBRGameDataReader)game.GameDataReader).memDataReader;

            var sr = rbrMemReader.ReadPacenotesFromMemory();
            if (sr.PacenoteRecords.Count > 0) 
            {
                return sr;
            }

            if (fileName.EndsWith(FILE_EXTENSION_DRIVELINE)) 
            {
                return ReadPacenoteRecordFromDLSFile(fileName);
            } else {
                return ReadPacenoteRecordFromIniFile(fileName);
            }
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
                DynamicPacenoteRecord record = new DynamicPacenoteRecord();
                if (section.Keys.ContainsKey("type"))
                {
                    var type = int.Parse(section.Keys["type"]);
                    if (ScriptResource.ID_2_PACENOTE.ContainsKey(type))
                    {
                        record.Pacenote = ScriptResource.ID_2_PACENOTE[type];
                    }
                }

                if (section.Keys.ContainsKey("distance"))
                {
                    var distance = float.Parse(section.Keys["distance"]);
                    record.Distance = distance;
                }

                if (section.Keys.ContainsKey("flag"))
                {
                    var flag = int.Parse(section.Keys["flag"]);
                    record.Modifier = getModifiersFromFlag(flag);
                }

                if (!string.IsNullOrEmpty(record.Pacenote))
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
                Debug.WriteLine("Invalid number of pacenote records: " + numOfPacenoteRecords);
                return null;
            }

            var dataOffset = numOfPacenoteOffset + 0x20;
            fs.Seek(dataOffset, SeekOrigin.Begin);
            var dword = new byte[4];
            var dataOffsetRead = fs.Read(dword, 0, 4);
            fs.Seek(dataOffset, SeekOrigin.Begin);
            if (dataOffsetRead != 4)
            {
                Debug.WriteLine("Failed to read data offset");
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
        foreach (var kv in ScriptResource.ID_2_MODIFIER)
        {
            if ((flag & kv.Key) == kv.Key)
            {
                modifiers.Add(kv.Value);
            }
        }
        return string.Join(",", modifiers);
    }

    public static DynamicPacenoteRecord GetDynamicPacenoteRecord(int ptype, int pflag, float pdistance)
    {
        DynamicPacenoteRecord record = new DynamicPacenoteRecord();
        if (ScriptResource.ID_2_PACENOTE.ContainsKey(ptype))
        {
            record.Pacenote = ScriptResource.ID_2_PACENOTE[ptype];
        }
        record.Modifier = getModifiersFromFlag(pflag);
        record.Distance = pdistance;

        if (!string.IsNullOrEmpty(record.Pacenote))
        {
            return record;
        }

        return null;
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
        if (_hasCustomPacenote && Directory.Exists(Path.Join(_customPacenoteFolder, trackName)))
        {
            // use latest!
            var files = Directory.GetFiles(Path.Join(_customPacenoteFolder, trackName), "*.ini");
            var latestFile = files.Select(f => new FileInfo(f)).OrderByDescending(f => f.LastWriteTime).FirstOrDefault();
            if (latestFile != null) 
            {
                return latestFile.FullName;
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
