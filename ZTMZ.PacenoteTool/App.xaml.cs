using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using ZTMZ.PacenoteTool.Base;
using ZTMZ.PacenoteTool.Base.Dialog;

namespace ZTMZ.PacenoteTool
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            SetupExceptionHandling();
            initializeI18N();
        }

        private void initializeI18N()
        {
            I18NLoader.Instance.Initialize(AppLevelVariables.Instance.GetPath(Constants.PATH_LANGUAGE));
            I18NLoader.Instance.SetCulture(Config.Instance.Language);
            GoogleAnalyticsHelper.Instance.TrackLaunchEvent("language", Config.Instance.Language);
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
                BaseDialog.Show("exception.unknown", ex.ToString(), null, MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                var exceptionStr = exception.ToString();
                BaseDialog.Show("exception.unknown", message + exceptionStr, null, MessageBoxButton.OK, MessageBoxImage.Error);
                GoogleAnalyticsHelper.Instance.TrackExceptionEvent(message, exceptionStr);
            }
        }
    }
}
