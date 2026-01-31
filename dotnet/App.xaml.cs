using System;
using System.IO;
using System.Windows;

namespace SynapMc
{
    public partial class App : Application
    {
        public App()
        {
            // Catch exceptions in the main thread
            this.DispatcherUnhandledException += App_DispatcherUnhandledException;
            // Catch exceptions in other threads or during startup
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
        }

        private void App_DispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
        {
            LogAndShow(e.Exception);
            e.Handled = true; // Prevent crash if possible
        }

        private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            LogAndShow(e.ExceptionObject as Exception);
        }

        private void LogAndShow(Exception? ex)
        {
            if (ex == null) return;
            string msg = $"Error: {ex.Message}\nStack: {ex.StackTrace}";
            try
            {
                string folder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "crash_reports");
                if (!Directory.Exists(folder))
                {
                    Directory.CreateDirectory(folder);
                }

                string fileName = $"crash_{DateTime.Now:yyyyMMdd_HHmmss}.log";
                string fullPath = Path.Combine(folder, fileName);
                
                File.WriteAllText(fullPath, msg);
                System.Windows.MessageBox.Show($"A crash occurred. Report saved to:\n{fullPath}", "SynapMc Crash Report");
            }
            catch
            {
                // Last resort if file write fails
            }
        }
    }
}