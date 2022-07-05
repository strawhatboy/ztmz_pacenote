
using Microsoft.Win32;
using ZTMZ.PacenoteTool.Base;
using ZTMZ.PacenoteTool.Base.Game;
using IniParser;
using IniParser.Model;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System;

namespace ZTMZ.PacenoteTool.RBR;

public class RBRGamePacenoteReader : IGamePacenoteReader
{
    public Dictionary<int, string> TrackNoNameMap = new();
    public Dictionary<int, string> TrackNoStageNameMap = new();
    public string RBRRootDir { get; private set; } = "";

    public static string WEATHER_SUFFIX_MORNING = "_M";
    public static string WEATHER_SUFFIX_EVENING = "_E";
    public static string WEATHER_SUFFIX_NOON = "_N";
    public static string WEATHER_SUFFIX_OVERCAST = "_O";
    public static string FILE_EXTENSION_DRIVELINE = ".dls";
    public static string FILE_INI_PACENOTE = "pacenotes.ini";
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

    public ScriptReader ReadPacenoteRecord(string profile, IGame game, string track)
    {
        // // extract trackNo from track name
        // var trackNoEnd = track.IndexOf("]");
        // var trackNoStart = track.IndexOf("[");
        // var trackNo = getIntegerFromString(track.Substring(trackNoStart + 1, trackNoEnd - trackNoStart - 1));
        // if (trackNo < 0)
        // {
        //     return null;
        // }

        // // extract weather from track name
        // var weather = "";
        // var weatherStart = track.IndexOf("(");
        // var weatherEnd = track.IndexOf(")");
        // if (weatherStart >= 0 && weatherEnd >= 0)
        // {
        //     weather = track.Substring(weatherStart + 1, weatherEnd - weatherStart - 1);
        // }
        // if (string.IsNullOrEmpty(weather))
        // {
        //     weather = "";
        // }
        // else
        // {
        //     weather = weather.ToLower();
        // }

        return null;
    }

    public string GetScriptFileForReplaying(string profile, IGame game, string track, bool fallbackToDefault = true)
    {
        return "";
    }

    public string GetScriptFileForRecording(string profile, IGame game, string track)
    {
        return "";
    }
}
