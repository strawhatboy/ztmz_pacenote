namespace ZTMZ.PacenoteTool.WpfGUI.Models;


public class UpdateFile
{
    public string version { set; get; }
    public string url { set; get; }
    public bool urlRedirected { set; get; } = false;
    public string changelog { set; get; }
    public string minVersionSupported { set; get; }
}
