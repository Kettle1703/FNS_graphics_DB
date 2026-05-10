using System;
using System.Windows;
using FNS_graphics.Data;
using FNS_graphics.Tests;

namespace FNS_graphics
{
    public partial class App : Application
    {
        private static bool _exitStarted;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            DispatcherUnhandledException += App_DispatcherUnhandledException;
            ShutdownMode = ShutdownMode.OnExplicitShutdown;

            if (Console_test_mode.Is_requested(e.Args))
            {
                int exitCode = Console_test_mode.Run(e.Args);
                Shutdown(exitCode);
                return;
            }

            Fns_database database = new(Database_config.LoadConnectionString());
            AuthWindow authWindow = new(database);
            MainWindow = authWindow;
            authWindow.Show();
        }

        internal static void ForceProcessExit()
        {
            if (_exitStarted)
                return;

            _exitStarted = true;

            try
            {
                Current?.Shutdown();
            }
            finally
            {
                Environment.Exit(0);
            }
        }

        private void App_DispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
        {
            MessageBox.Show(
                e.Exception.Message,
                "Ошибка приложения",
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            e.Handled = true;
        }
    }
}
