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
        /// KUDUBROWSE.exe --register         tell Windows it's a web browser (used by the installer)
        /// KUDUBROWSE.exe --unregister       remove that (used when uninstalling)
        /// KUDUBROWSE.exe --self-test [--chrome folder]   automated checks for the build (with a made-up Chrome profile)
        /// </summary>
        [STAThread]
        public static int Main(string[] args)
        {
            var app = new App();
            bool Has(string name) => args.Any(a => string.Equals(a, name, StringComparison.OrdinalIgnoreCase));
            if (Has("--register"))
            {
                ShellIntegration.Register();
                return 0;
            }
            if (Has("--unregister"))
            {
                ShellIntegration.Unregister();
                return 0;
            }
            if (Has("--self-test"))
            {
                var i = Array.FindIndex(args, a => string.Equals(a, "--chrome", StringComparison.OrdinalIgnoreCase));
                return app.RunSelfTest(i >= 0 && i < args.Length - 1 ? args[i + 1] : null);
            }
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
                // Keep Windows' note of where the browser is up to date (it may have been moved), then say hello once.
                System.Threading.Tasks.Task.Run(() => ShellIntegration.Register());
                if (!Settings.WelcomeShown) main.Dispatcher.BeginInvoke(new Action(() => WelcomeWindow.ShowOnce(main)), DispatcherPriority.ApplicationIdle);
            };
            return Run();
        }

        public int RunSelfTest(string chrome)
        {
            var code = 1;
            Startup += async (s, e) =>
            {
                ShutdownMode = ShutdownMode.OnExplicitShutdown;
                DispatcherUnhandledException += OnUnhandledException;
                code = await SelfTest.Run(chrome);
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
