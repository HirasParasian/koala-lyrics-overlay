using System.IO;
using System.Windows;

namespace SpotifyLyricsOverlay;

public partial class App : Application
{
    private static readonly string CrashLogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "SpotifyLyricsOverlay", "crash.log");

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Catch all UI thread unhandled exceptions to prevent sudden closing
        DispatcherUnhandledException += (s, args) =>
        {
            LogCrash("DispatcherUnhandledException", args.Exception);
            args.Handled = true; // Prevents the application from crashing
        };

        // Catch non-UI thread exceptions
        AppDomain.CurrentDomain.UnhandledException += (s, args) =>
        {
            if (args.ExceptionObject is Exception ex)
            {
                LogCrash("AppDomainUnhandledException", ex);
            }
        };

        // Catch async Task unobserved exceptions
        TaskScheduler.UnobservedTaskException += (s, args) =>
        {
            LogCrash("UnobservedTaskException", args.Exception);
            args.SetObserved(); // Prevents process termination
        };

        if (InstallerService.HandleInstallArguments(e.Args))
        {
            Shutdown();
            return;
        }

        var window = new MainWindow();
        MainWindow = window;
        window.Show();
    }

    private static void LogCrash(string source, Exception ex)
    {
        try
        {
            var dir = Path.GetDirectoryName(CrashLogPath);
            if (dir is not null) Directory.CreateDirectory(dir);
            File.AppendAllText(CrashLogPath,
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{source}] {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}\n\n");
        }
        catch { }
    }
}
