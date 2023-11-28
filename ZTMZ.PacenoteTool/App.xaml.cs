using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using ZTMZ.PacenoteTool.Base;
using ZTMZ.PacenoteTool.Base.UI.Dialog;
using ZTMZ.PacenoteTool.Core;

namespace ZTMZ.PacenoteTool
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {

        private NLog.Logger _logger = NLog.LogManager.GetCurrentClassLogger();

        protected override void OnExit(ExitEventArgs e)
        {
            NLog.LogManager.Shutdown();
            base.OnExit(e);
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            NLogManager.init(ToolUtils.GetToolVersion());
            _logger.Info("Application started");
            SetupExceptionHandling();
            initializeI18N();
        }

        private void initializeI18N()
        {
            // load from I18NPath
            var jsonPaths = new List<string>{
                AppLevelVariables.Instance.GetPath(Constants.PATH_LANGUAGE),
                AppLevelVariables.Instance.GetPath(Path.Combine(Constants.PATH_GAMES, Constants.PATH_LANGUAGE)),
                AppLevelVariables.Instance.GetPath(Path.Combine(Constants.PATH_DASHBOARDS, Constants.PATH_LANGUAGE))
            };
            I18NLoader.Instance.Initialize(jsonPaths);
            I18NLoader.Instance.SetCulture(Config.Instance.Language);
            GoogleAnalyticsHelper.Instance.TrackLaunchEvent("language", Config.Instance.Language);
            var CurrentDict = new ResourceDictionary();
            
            foreach (var key in I18NLoader.Instance.CurrentCulture.Keys)
            {
                CurrentDict.Add(key, I18NLoader.Instance.CurrentCulture[key]);
            }
            this.Resources.MergedDictionaries.Add(CurrentDict);
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
                BaseDialog.Show("exception.unknown.title", ex.ToString(), null, MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                var exceptionStr = exception.ToString();
                _logger.Fatal("Unhandled Exception: {0}", message + exceptionStr);
                BaseDialog.Show("exception.unknown.title", message + exceptionStr, null, MessageBoxButton.OK, MessageBoxImage.Error);
                GoogleAnalyticsHelper.Instance.TrackExceptionEvent(message, exceptionStr);
            }
        }
    }
}
