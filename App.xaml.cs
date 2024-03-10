using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace WindowsConsoleOS
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>


    public partial class App : Application
    {
        private static Mutex mutex = null;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            string mutexName = @"Global\WindowSelector-EFA78C99-1FA0-4FDB-918D-3145454E9EFD";
            bool createdNew;

            mutex = new Mutex(true, mutexName, out createdNew);

            if (!createdNew)
            {
                // If the mutex already exists, it means another instance of the application is already running.
                //MessageBox.Show("An instance of the application is already running.");
                Application.Current.Shutdown();
                return;
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            if (mutex != null)
            {
                mutex.ReleaseMutex();
            }
            base.OnExit(e);
        }
    }
}
