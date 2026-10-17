using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ZTMZ.PacenoteTool.WpfGUI;

internal static class Program
{
    [STAThread]
    public static int Main()
    {
        // A migration helper must not write to its parent's startup session.
        if (Environment.GetCommandLineArgs().Contains("--move-data-folder"))
            Environment.SetEnvironmentVariable("ZTMZ_STARTUP_STATUS", null);
        StartupProgress.Report.Progress("managed_entry");
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception error) StartupProgress.Report.Failed(error);
        };
        try { return RunApplication(); }
        catch (Exception error)
        {
            StartupProgress.Report.Failed(error);
            if (!StartupProgress.HasLauncher)
                MessageBoxW(IntPtr.Zero, error.ToString(), "ZTMZ — Startup failed / 启动失败", 0x10);
            return 1;
        }
    }

    // Keep App's type initialization and dependency loading inside Main's catch.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int RunApplication()
    {
        var app = new App();
        app.InitializeComponent();
        return app.Run();
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBoxW(IntPtr window, string text, string caption, uint type);
}
