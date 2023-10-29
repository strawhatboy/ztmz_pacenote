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
// using Wpf.Ui.Contracts;
using ZTMZ.PacenoteTool.Base;
using ZTMZ.PacenoteTool.WpfGUI.Services;

namespace ZTMZ.PacenoteTool.WpfGUI;

public partial class App : Application
{
    public App() {
        var jsonPaths = new List<string>{
            AppLevelVariables.Instance.GetPath(Constants.PATH_LANGUAGE),
            AppLevelVariables.Instance.GetPath(Path.Combine(Constants.PATH_GAMES, Constants.PATH_LANGUAGE)),
            AppLevelVariables.Instance.GetPath(Path.Combine(Constants.PATH_DASHBOARDS, Constants.PATH_LANGUAGE))
        };
            
        NLogManager.init(ToolVersion.TEST);
        _logger.Info("Application started");
        I18NLoader.Instance.Initialize(jsonPaths);
        I18NLoader.Instance.SetCulture(Config.Instance.Language);
        _logger.Info("i18n Loaded.");
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

            services.AddSingleton<UpdateService, UpdateService>();

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
        // For more info see https://docs.microsoft.com/en-us/dotnet/api/system.windows.application.dispatcherunhandledexception?view=windowsdesktop-6.0
        _logger.Error(e.Exception, "Unhandled exception occurred.");
    }

}
