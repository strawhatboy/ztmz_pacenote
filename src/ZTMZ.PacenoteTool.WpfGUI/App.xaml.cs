using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Wpf.Ui.Extensions;

// using Wpf.Ui.Contracts;
using ZTMZ.PacenoteTool.Base;
using ZTMZ.PacenoteTool.Base.UI;
using ZTMZ.PacenoteTool.Core;
using ZTMZ.PacenoteTool.WpfGUI.Services;

namespace ZTMZ.PacenoteTool.WpfGUI;

public partial class App : Application
{
    public App() {
        var jsonPaths = new List<string>{
            AppLevelVariables.Instance.GetPath(Constants.PATH_LANGUAGE),
            AppLevelVariables.Instance.GetPath(Path.Combine(Constants.PATH_GAMES, Constants.PATH_LANGUAGE)),
            AppLevelVariables.Instance.GetPath(Path.Combine(Constants.PATH_DASHBOARDS, Constants.PATH_LANGUAGE)),
        };
            
        NLogManager.init(ToolUtils.GetToolVersion());
        _logger.Info("Application started");
        I18NLoader.Instance.Initialize(jsonPaths);
        I18NLoader.Instance.SetCulture(Config.Instance.Language);
        _logger.Info("i18n Loaded.");
        GetService<AzureAppInsightsManager>().TrackEvent("Application started", new Dictionary<string, string> {
            { "Language", Config.Instance.Language },
            { "Version", ToolUtils.GetToolVersion().ToString() },
            { "OSVersion", Environment.OSVersion.ToString() },
            { "AppVersion", System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? String.Empty }
        });
    }

    private static NLog.Logger _logger = NLog.LogManager.GetCurrentClassLogger();
    
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    private static readonly IHost _host = Host
        .CreateDefaultBuilder()
        // .ConfigureAppConfiguration(c => { c.SetBasePath(Path.GetDirectoryName(AppContext.BaseDirectory)); })
        .ConfigureServices((context, services) =>
        {
            // App Host
            services.AddHostedService<ApplicationHostService>();

            // Page resolver service
            services.AddSingleton<IPageService, PageService>();

            // Theme manipulation
            services.AddSingleton<IThemeService, ThemeService>();

            // TaskBar manipulation
            services.AddSingleton<ITaskBarService, TaskBarService>();

            // Service containing navigation, same as INavigationWindow... but without window
            services.AddSingleton<INavigationService, NavigationService>();

            services.AddSingleton<IContentDialogService, ContentDialogService>();

            services.AddSingleton<UpdateService>();
            services.AddSingleton<StartupService>();
            services.AddSingleton<GameOverlayManager>();
            services.AddSingleton<VRGameOverlayManager>();
            services.AddSingleton<AzureAppInsightsManager>();

            services.AddSingleton<ZTMZ.PacenoteTool.Core.ZTMZPacenoteTool>();

            // Main window with navigation
            services.AddScoped<INavigationWindow, Views.MainWindow>();
            services.AddScoped<ViewModels.MainWindowVM>();

            // Views and ViewModels
            // services.AddScoped<Views.Pages.DashboardPage>();
            // services.AddScoped<ViewModels.DashboardViewModel>();
            // services.AddScoped<Views.Pages.DataPage>();
            // services.AddScoped<ViewModels.DataViewModel>();
            services.AddScoped<Views.SettingsPage>();
            services.AddScoped<ViewModels.SettingsVM>();
            services.AddScoped<Views.HomePage>();
            services.AddScoped<ViewModels.HomePageVM>();
            services.AddScoped<Views.GeneralPage>();
            services.AddScoped<ViewModels.GeneralPageVM>();
            services.AddScoped<Views.VoicePage>();
            services.AddScoped<ViewModels.VoicePageVM>();
            services.AddScoped<Views.VoiceSettingsPage>();
            services.AddScoped<ViewModels.VoiceSettingsPageVM>();
            services.AddScoped<Views.VoicePackagePage>();
            services.AddScoped<ViewModels.VoicePackagePageVM>();
            services.AddScoped<Views.PlayPage>();
            services.AddScoped<ViewModels.PlayPageVM>();
            services.AddScoped<Views.HudPage>();
            services.AddScoped<ViewModels.HudPageVM>();
            services.AddScoped<Views.UserPage>();
            services.AddScoped<ViewModels.UserPageVM>();
            services.AddScoped<Views.VrPage>();
            services.AddScoped<ViewModels.VrPageVM>();

            // Configuration
            // services.Configure<AppConfig>(context.Configuration.GetSection(nameof(AppConfig)));
        }).Build();

    /// <summary>
    /// Gets registered service.
    /// </summary>
    /// <typeparam name="T">Type of the service to get.</typeparam>
    /// <returns>Instance of the service or <see langword="null"/>.</returns>
    public static T GetService<T>()
        where T : class
    {
        return _host.Services.GetService(typeof(T)) as T;
    }


    /// <summary>
    /// Occurs when the application is loading.
    /// </summary>
    private async void OnStartup(object sender, StartupEventArgs e)
    {
        SetupExceptionHandling();
        await _host.StartAsync();
    }

    /// <summary>
    /// Occurs when the application is closing.
    /// </summary>
    private async void OnExit(object sender, ExitEventArgs e)
    {
        await _host.StopAsync();

        _host.Dispose();
    }

    /// <summary>
    /// Occurs when an exception is thrown by an application but not handled.
    /// </summary>
    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        _logger.Error(e.Exception, "Unhandled exception occurred.");
        // For more info see https://docs.microsoft.com/en-us/dotnet/api/system.windows.application.dispatcherunhandledexception?view=windowsdesktop-6.0
        GetService<AzureAppInsightsManager>().TrackException(e.Exception);
        Dispatcher.Invoke(() => {
            new Wpf.Ui.Controls.MessageBox
            {
                Title = I18NLoader.Instance["exception.unknown.title"],
                Content = e.Exception.ToString(),
                CloseButtonText = I18NLoader.Instance["dialog.common.btn_ok"]
            }.ShowDialogAsync().Wait();
        });
    }

    private void SetupExceptionHandling()
    {
        AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            LogUnhandledException((Exception)e.ExceptionObject, "AppDomain.CurrentDomain.UnhandledException");

        DispatcherUnhandledException += (s, e) =>
        {
            LogUnhandledException(e.Exception, "Application.Current.DispatcherUnhandledException");
            e.Handled = true;
        };

        TaskScheduler.UnobservedTaskException += (s, e) =>
        {
            if (e.Exception.StackTrace == null)
            {
                _logger.Error("Unhandled Exception with no stacktrace: {0}", e.Exception);
                // ignore it. maybe raised by SocketException in finalizer thread.
                return;
            }
            LogUnhandledException(e.Exception, "TaskScheduler.UnobservedTaskException");
            e.SetObserved();
        };
    }

    private void LogUnhandledException(Exception exception, string source)
    {
        string message = $"Unhandled exception ({source})";
        try
        {
            System.Reflection.AssemblyName assemblyName = System.Reflection.Assembly.GetExecutingAssembly().GetName();
            message = string.Format("Unhandled exception in {0} v{1}", assemblyName.Name, assemblyName.Version);
        }
        catch (Exception ex)
        {
            _logger.Error("Unknown Error when try to handle unhandled exception: {0}", ex);
            new Wpf.Ui.Controls.MessageBox
            {
                Title = I18NLoader.Instance["exception.unknown.title"],
                Content = ex.ToString(),
                CloseButtonText = I18NLoader.Instance["dialog.common.btn_ok"]
            }.ShowDialogAsync().Wait();
        }
        finally
        {
            var exceptionStr = exception.ToString();
            _logger.Fatal("Unhandled Exception: {0}", message + exceptionStr);
            GetService<AzureAppInsightsManager>().TrackException(exception);
            new Wpf.Ui.Controls.MessageBox
            {
                Title = I18NLoader.Instance["exception.unknown.title"],
                Content = message + exceptionStr,
                CloseButtonText = I18NLoader.Instance["dialog.common.btn_ok"]
            }.ShowDialogAsync().Wait();
        }
    }

}
