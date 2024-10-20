using ZTMZ.PacenoteTool.Base;

namespace ZTMZ.PacenoteTool.WpfGUI.Models;


public class UpdateFile
{
    public string version { set; get; }
    public string url { set; get; }
    public bool urlRedirected { set; get; } = false;
    public string changelog { set; get; }
    public string minVersionSupported { set; get; }
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
    public bool isAvailable = false;
    [ObservableProperty]
    public float downloadProgress = 0;
    [ObservableProperty]
    public bool needDownload = false;    // no local file

    [ObservableProperty]
    public string path;

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
