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

public class CodriverPackageUpdateFile: CoDriverPackageInfo {
    public CodriverPackageUpdateFile() {}
    public CodriverPackageUpdateFile(CoDriverPackageInfo info) {
        this.name = info.name;
        this.description = info.description;
        this.gender = info.gender;
        this.language = info.language;
        this.homepage = info.homepage;
        this.version = info.version;
        this.Path = info.Path;
    }
    public string url { set; get; }
    public bool urlRedirected { set; get; } = false;
    public string changelog { set; get; }
    public bool needUpdate {set;get;} = false;
    public bool isDownloading {set;get;} = false;
    public bool isInstalling {set;get;} = false;
    public float downloadProgress {set;get;} = 0;
    public bool needDownload {set;get;} = false;    // no local file
}
