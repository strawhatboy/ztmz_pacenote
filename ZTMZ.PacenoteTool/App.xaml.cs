using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using ZTMZ.PacenoteTool.Base;

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
            I18NLoader.Instance.Initialize(AppLevelVariables.Instance.GetPath("lang"));
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
                MessageBox.Show(ex.ToString(), "Exception in LogUnhandledException");
            }
            finally
            {
                var exceptionStr = exception.ToString();
                MessageBox.Show(exceptionStr, message);
                GoogleAnalyticsHelper.Instance.TrackExceptionEvent(message, exceptionStr);
            }
        }
    }
}
