using System;
using System.Linq;
using System.Windows;
using System.Windows.Threading;
using Kudubrowse.Core;
using Kudubrowse.Views;

namespace Kudubrowse
{
    public static class Program
    {
        /// <summary>
        /// KUDUBROWSE.exe                    open (with the tabs from last time)
        /// KUDUBROWSE.exe address            open a web address or file in a new tab
        /// KUDUBROWSE.exe --self-test        automated checks for the build
        /// </summary>
        [STAThread]
        public static int Main(string[] args)
        {
            var app = new App();
            if (args.Any(a => string.Equals(a, "--self-test", StringComparison.OrdinalIgnoreCase))) return app.RunSelfTest();
            return app.Run(args.FirstOrDefault(a => !a.StartsWith("--")));
        }
    }

    public sealed class App : Application
    {
        public int Run(string address)
        {
            Startup += (s, e) =>
            {
                Init();
                Blocker.Start();
                var main = new MainWindow();
                MainWindow = main;
                main.Show();
                main.Start(address);
            };
            return Run();
        }

        public int RunSelfTest()
        {
            var code = 1;
            Startup += async (s, e) =>
            {
                ShutdownMode = ShutdownMode.OnExplicitShutdown;
                DispatcherUnhandledException += OnUnhandledException;
                code = await SelfTest.Run();
                Shutdown(code);
            };
            Run();
            return code;
        }

        internal void Init()
        {
            ShutdownMode = ShutdownMode.OnMainWindowClose;
            DispatcherUnhandledException += OnUnhandledException;
            ThemeManager.Init(Settings.Theme);
            ThemeManager.WatchSystem();
        }

        private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            if (SelfTest.Running)
            {
                SelfTest.Errors.Add(e.Exception.ToString());
                e.Handled = true;
                return;
            }
            Dialogs.Error(MainWindow?.IsVisible == true ? MainWindow : null, "Something went wrong:\n\n" + e.Exception.Message);
            e.Handled = true;
        }
    }
}
