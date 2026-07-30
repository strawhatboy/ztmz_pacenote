using System;
using System.Collections.Generic;
using System.Linq;
using ZTMZ.PacenoteTool.Base;

namespace ZTMZ.PacenoteTool.WpfGUI.Models;


public class UpdateFile
{
    public string version { set; get; } = string.Empty;
    public string url { set; get; } = string.Empty;
    public bool urlRedirected { set; get; } = false;
    // Kept for older clients and as the fallback when a culture has no dedicated notes.
    public string changelog { set; get; } = string.Empty;
    public Dictionary<string, string>? changelogs { set; get; }
    public string minVersionSupported { set; get; } = string.Empty;

    public string ResolveChangelogUrl(string? cultureName)
    {
        if (changelogs == null || changelogs.Count == 0 || string.IsNullOrWhiteSpace(cultureName))
        {
            return changelog;
        }

        var requestedCulture = cultureName.Trim().Replace('_', '-');
        var exactMatch = changelogs.FirstOrDefault(entry =>
            string.Equals(entry.Key, requestedCulture, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(exactMatch.Value))
        {
            return exactMatch.Value;
        }

        var requestedLanguage = GetLanguageCode(requestedCulture);
        var languageMatch = changelogs.FirstOrDefault(entry =>
            string.Equals(GetLanguageCode(entry.Key), requestedLanguage, StringComparison.OrdinalIgnoreCase));

        return string.IsNullOrWhiteSpace(languageMatch.Value) ? changelog : languageMatch.Value;
    }

    private static string GetLanguageCode(string cultureName)
    {
        var separatorIndex = cultureName.IndexOf('-');
        return separatorIndex < 0 ? cultureName : cultureName[..separatorIndex];
    }
}

public partial class CodriverPackageUpdateFile: ObservableObject {
    public CodriverPackageUpdateFile() {}
    public CodriverPackageUpdateFile(CoDriverPackageInfo info) {
        this.Id = info.id;
        this.Name = info.name;
        this.Description = info.description;
        this.Gender = info.gender;
        this.Language = info.language;
        this.Homepage = info.homepage;
        this.OriginalVersion = info.version;
        this.Path = info.Path;
        this.Integrity_simple = info.integrity_simple;
        this.Integrity_normal = info.integrity_normal;
        this.Integrity_complex = info.integrity_complex;
    }
    [ObservableProperty]
    public string id;  // uuid
    [ObservableProperty]
    public string name;
    [ObservableProperty]
    public string description;
    [ObservableProperty]
    public string gender;
    [ObservableProperty]
    public string language;
    [ObservableProperty]
    public string homepage;
    [ObservableProperty]
    public string version;
    [ObservableProperty]
    public string originalVersion;

    [ObservableProperty]
    public string url;

    [ObservableProperty]
    public bool urlRedirected;
    [ObservableProperty]
    public string changelog;
    [ObservableProperty]
    public bool needUpdate;
    [ObservableProperty]
    public bool isDownloading;
    [ObservableProperty]
    public bool isInstalling = false;
    [ObservableProperty]
    public bool isExporting = false;
    [ObservableProperty]
    public bool isAvailable = false;
    [ObservableProperty]
    public float downloadProgress = 0;
    [ObservableProperty]
    public bool needDownload = false;    // no local file

    [ObservableProperty]
    public string path;

    [ObservableProperty]
    public float integrity_simple = 0;
    [ObservableProperty]
    public float integrity_normal = 0;
    [ObservableProperty]
    public float integrity_complex = 0;

    public string GenderStr
    {
        get
        {
            switch (gender)
            {
                case "M":
                {
                    return I18NLoader.Instance["misc.gender_male"];
                }
                case "F":
                {
                    return I18NLoader.Instance["misc.gender_female"];
                }
            }

            return I18NLoader.Instance["misc.gender_unknown"];
        }
    }

    public string DisplayText =>
        string.Format("[{0}][{1}] {2}", Language, GenderStr, Name);
}
