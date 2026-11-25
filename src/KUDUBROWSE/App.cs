// Where KUDUBROWSE starts: one running copy per user (later starts hand their address over), the self-test, and the
// first window.
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
            // One running copy: a second start (a link clicked in another app) hands its address to the first.
            var one = new SingleInstance();
            if (!one.IsFirst)
            {
                AllowSetForegroundWindow(-1); // so the running copy may come to the front
                if (SingleInstance.Send(one.PipeName, args.Length == 0 ? new[] { "--new-window" } : args)) return 0;
            }
            using (one) return app.Run(args.FirstOrDefault(a => !a.StartsWith("--")), one);
        }

        /// <summary>
        /// Lets another program's window come to the front (so the running copy can show the page asked for).
        /// </summary>
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool AllowSetForegroundWindow(int processId);
    }

    public sealed class App : Application
    {
        /// <summary>Starts the browser with this address (or the tabs from last time).</summary>
        public int Run(string address, SingleInstance one)
        {
            Startup += async (s, e) =>
            {
                Init();
                ShutdownMode = ShutdownMode.OnExplicitShutdown; // the browser closes when its last window does (MainWindow.Closed)
                if (one.IsFirst) one.Listen(a => Dispatcher.BeginInvoke(new Action(() => OpenFromOutside(a))));
                Blocker.Start();
                var main = new MainWindow();
                MainWindow = main;
                main.Show();
                // Clear when closing didn't finish last time (Windows was shutting down): finished now, before any page opens.
                if (Settings.ForgetPending)
                {
                    try { await Forgetting.Run(); }
                    catch (Exception ex) when (ex is InvalidOperationException || ex is System.Runtime.InteropServices.COMException) { }
                }
                main.Start(address);
                // Keep Windows' note of where the browser is up to date (it may have been moved), then say hello once.
                _ = System.Threading.Tasks.Task.Run(() => ShellIntegration.Register());
                if (!Settings.WelcomeShown) _ = main.Dispatcher.BeginInvoke(new Action(() => WelcomeWindow.ShowOnce(main)), DispatcherPriority.ApplicationIdle);
                // Updating by itself: "Updated to ..." once after an update, and the daily look for a new version.
                var updated = Updater.UpdatedTo();
                if (updated != null) _ = main.Dispatcher.BeginInvoke(new Action(() => main.ShowUpdatedNote(updated)), DispatcherPriority.ApplicationIdle);
                Updater.Start();
            };
            var code = Run();
            // A password or sign-in code copied a moment ago doesn't stay on the clipboard after the browser.
            SecretClipboard.ClearIfStillOurs();
            // The browser has closed: a new version waiting is installed now, quietly.
            Updater.InstallIfReady();
            return code;
        }

        /// <summary>An address (or a request for a window) handed over by a second start of the browser.</summary>
        public static void OpenFromOutside(string[] args)
        {
            var address = args.FirstOrDefault(a => !a.StartsWith("--"));
            var w = Views.MainWindow.LastActive;
            if (w == null)
            {
                w = new Views.MainWindow();
                w.Show();
                if (address == null) w.NewTab(select: true);
            }
            else if (address == null) w.NewWindow();
            if (address != null)
            {
                var url = AddressBar.Resolve(address, SearchEngines.Find(Settings.SearchEngine));
                if (url != null) w.NewTab(url, true);
            }
            if (w.WindowState == WindowState.Minimized) w.WindowState = WindowState.Normal;
            w.Activate();
        }

        /// <summary>
        /// Runs the self-test (with the made-up Chrome folder given) and returns its result as the exit code.
        /// </summary>
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

        /// <summary>
        /// What both ways of starting need: closing with the last window, the theme, the language and the error
        /// handler.
        /// </summary>
        internal void Init()
        {
            ShutdownMode = ShutdownMode.OnMainWindowClose;
            DispatcherUnhandledException += OnUnhandledException;
            SafetyNet.Install(); // says so if an error behind the window closes the app
            LangUi.Start(); // the app in the chosen language
            ThemeManager.Init(Settings.Theme);
            ThemeManager.WatchSystem();
        }

        /// <summary>
        /// Shows an error that wasn't expected instead of closing the browser (the self-test keeps it as a failure).
        /// </summary>
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
