using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;

namespace ZTMZ.PacenoteTool.WpfGUI.Services;

[ComImport]
[Guid("00021401-0000-0000-C000-000000000046")]
internal class ShellLink
{
}

[ComImport]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[Guid("000214F9-0000-0000-C000-000000000046")]
internal interface IShellLink
{
    void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile, int cchMaxPath, out IntPtr pfd, int fFlags);
    void GetIDList(out IntPtr ppidl);
    void SetIDList(IntPtr pidl);
    void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName, int cchMaxName);
    void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
    void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir, int cchMaxPath);
    void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
    void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs, int cchMaxPath);
    void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
    void GetHotkey(out short pwHotkey);
    void SetHotkey(short wHotkey);
    void GetShowCmd(out int piShowCmd);
    void SetShowCmd(int iShowCmd);
    void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath, int cchIconPath, out int piIcon);
    void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
    void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, int dwReserved);
    void Resolve(IntPtr hwnd, int fFlags);
    void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
}

public class StartupService {

    private NLog.Logger _logger = NLog.LogManager.GetCurrentClassLogger();
    public void AddApplicationToCurrentUserStartup() {
        string applicationName = Assembly.GetExecutingAssembly().GetName().Name;
        string applicationPath = Environment.ProcessPath;
        string startupPath = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
        string shortcutPath = Path.Combine(startupPath, applicationName + ".lnk");

        // creat shortcut using IShellLink
        IShellLink link = (IShellLink)new ShellLink();
        link.SetDescription(applicationName);
        link.SetPath(applicationPath);
        link.SetWorkingDirectory(Path.GetDirectoryName(applicationPath));
        IPersistFile file = (IPersistFile)link;
        file.Save(shortcutPath, false);

        _logger.Debug("Created shortcut at {0}, with applicationPath: {1}", shortcutPath, applicationPath);
    }

    public void RemoveApplicationFromCurrentUserStartup() {
        string applicationName = Assembly.GetExecutingAssembly().GetName().Name;
        string startupPath = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
        string shortcutPath = Path.Combine(startupPath, applicationName + ".lnk");

        if (File.Exists(shortcutPath)) {
            File.Delete(shortcutPath);
        }

        _logger.Debug("Deleted shortcut at {0}", shortcutPath);
    }
}
