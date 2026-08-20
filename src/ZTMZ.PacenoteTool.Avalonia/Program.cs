using System.Reflection;
using System.Runtime.InteropServices;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia;
using Avalonia.Threading;
using FluentAvalonia.UI.Controls;
using NLog;
using ZTMZ.PacenoteTool.Avalonia.Services;
using ZTMZ.PacenoteTool.Base;
using ZTMZ.PacenoteTool.Core;
#if WINDOWS
using ZTMZ.PacenoteTool.Platform.Windows;
#else
using ZTMZ.PacenoteTool.Platform.Linux;
#endif

namespace ZTMZ.PacenoteTool.Avalonia;

internal static class Program
{
    private static readonly Logger Logger = NLog.LogManager.GetCurrentClassLogger();
    private static int _unhandledExceptionDialogVisible;

    [STAThread]
    public static void Main(string[] args)
    {
        ConfigureApplicationPaths();
        var toolVersion = ToolUtils.GetToolVersion();
        NLogManager.init(toolVersion);
        ConfigureHostLogging();
        AvaloniaNLogSink.Install();
        SetupExceptionLogging();

        try
        {
            AvaloniaLocalization.Initialize();
            Logger.Info(
                "Application started. Version={0}; Channel={1}; DataHome={2}; ResourceHome={3}; DisplayBackend={4}; OS={5}; Architecture={6}.",
                Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "unknown",
                toolVersion,
                AppLevelVariables.Instance.DataRoot,
                AvaloniaLocalization.ResourceRoot,
                GetDisplayBackend(),
                RuntimeInformation.OSDescription,
                RuntimeInformation.ProcessArchitecture);
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
            Logger.Info("Application stopped normally.");
        }
        catch (Exception exception)
        {
            Logger.Fatal(exception, "Avalonia application terminated unexpectedly.");
            throw;
        }
        finally
        {
            NLog.LogManager.Flush(TimeSpan.FromSeconds(5));
            NLog.LogManager.Shutdown();
        }
    }

    public static AppBuilder BuildAvaloniaApp()
    {
        var builder = AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont();

        if (ShouldUseWayland())
        {
            builder = builder.UseWayland();
        }

        return builder;
    }

    private static void ConfigureApplicationPaths()
    {
#if WINDOWS
        AppLevelVariables.Configure(new WindowsRegistryConfigurationLocation());
#else
        AppLevelVariables.Configure(new LinuxFileConfigurationLocation());
#endif
    }

    private static void ConfigureHostLogging()
    {
        NLogManager.AddAlwaysOnRule(
            "AvaloniaLifecycle",
            typeof(Program).FullName!,
            LogLevel.Info);
        NLogManager.AddAlwaysOnRule(
            "AvaloniaRuntimeLifecycle",
            typeof(DesktopRuntime).FullName!,
            LogLevel.Info);
        NLogManager.AddAlwaysOnRule(
            "AvaloniaFrameworkWarnings",
            "Avalonia.*",
            LogLevel.Warn);
    }

    private static bool ShouldUseWayland()
    {
        if (!OperatingSystem.IsLinux() ||
            string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY")))
        {
            return false;
        }

        return !string.Equals(
            Environment.GetEnvironmentVariable("ZTMZ_DISPLAY_BACKEND"),
            "x11",
            StringComparison.OrdinalIgnoreCase);
    }

    private static string GetDisplayBackend()
    {
        if (OperatingSystem.IsWindows())
        {
            return "Windows";
        }

        if (OperatingSystem.IsLinux())
        {
            return ShouldUseWayland() ? "Wayland" : "X11";
        }

        return "Default";
    }

    private static void SetupExceptionLogging()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, eventArgs) =>
        {
            if (eventArgs.ExceptionObject is Exception exception)
            {
                Logger.Fatal(
                    exception,
                    "Unhandled AppDomain exception. IsTerminating={0}.",
                    eventArgs.IsTerminating);
            }
            else
            {
                Logger.Fatal(
                    "Unhandled AppDomain exception object. IsTerminating={0}; Value={1}.",
                    eventArgs.IsTerminating,
                    eventArgs.ExceptionObject);
            }
        };

        TaskScheduler.UnobservedTaskException += (_, eventArgs) =>
        {
            Logger.Error(eventArgs.Exception, "Unobserved task exception.");
            eventArgs.SetObserved();
        };

        Dispatcher.UIThread.UnhandledException += (_, eventArgs) =>
        {
            Logger.Error(eventArgs.Exception, "Unhandled Avalonia UI dispatcher exception.");
            // Avalonia otherwise terminates the desktop lifetime after this callback returns.
            // The dispatcher can continue after a managed UI failure, so retain the process and
            // surface the failure in the same way as the WPF host.
            eventArgs.Handled = true;
            // Do not create a dialog from inside a failing layout/render callback. Let that
            // callback unwind first, otherwise the dialog's own layout can be swallowed by
            // the same dispatcher failure.
            Dispatcher.UIThread.Post(
                () => _ = ShowUnhandledExceptionDialogAsync(eventArgs.Exception),
                DispatcherPriority.Background);
        };
    }

    private static async Task ShowUnhandledExceptionDialogAsync(Exception exception)
    {
        if (Interlocked.Exchange(ref _unhandledExceptionDialogVisible, 1) != 0)
        {
            return;
        }

        try
        {
            var owner = (Application.Current?.ApplicationLifetime as
                IClassicDesktopStyleApplicationLifetime)?.MainWindow;
            if (owner == null)
            {
                Logger.Warn(exception, "Could not show the unhandled-exception dialog because no main window exists.");
                return;
            }

            var dialog = FaContentDialogFactory.Create(new FAContentDialog
            {
                Title = AvaloniaLocalization.Get("exception.unknown.title"),
                Content = exception.ToString(),
                CloseButtonText = AvaloniaLocalization.Get("dialog.common.btn_ok")
            });
            await dialog.ShowAsync(owner);
        }
        catch (Exception dialogException)
        {
            Logger.Error(dialogException, "Failed to show the Avalonia unhandled-exception dialog.");
        }
        finally
        {
            Interlocked.Exchange(ref _unhandledExceptionDialogVisible, 0);
        }
    }
}
