using System.Windows;
using System.Runtime.InteropServices;

namespace VideoStudio;

public partial class App : Application
{
    string crashLog = Path.Combine(AppContext.BaseDirectory, "data", "last-crash.log");
    void LogCrash(object? error)
    {
        try { Directory.CreateDirectory(Path.GetDirectoryName(crashLog)!); File.AppendAllText(crashLog, DateTimeOffset.Now.ToString("O") + "\n" + error + "\n\n"); } catch { }
    }
    [DllImport("kernel32.dll")] static extern uint SetErrorMode(uint mode);
    protected override async void OnStartup(StartupEventArgs e)
    {
        var validation = e.Args.Length > 1 && e.Args[0].EndsWith("test", StringComparison.Ordinal);
        if (validation) { crashLog = Path.Combine(Path.GetFullPath(e.Args[1]), "last-crash.log"); SetErrorMode(0x0002 | 0x8000); }
        AppDomain.CurrentDomain.UnhandledException += (_, args) => { LogCrash(args.ExceptionObject); if (validation) Environment.Exit(1); };
        DispatcherUnhandledException += (_, args) => LogCrash(args.Exception);
        base.OnStartup(e);
        ThemeManager.Initialize();
        try
        {
            if (e.Args.Length > 0 && e.Args[0] is "--self-test" or "--integration-test" or "--preview" or "--online-test" or "--network-test" or "--history-test" or "--access-test" or "--lifecycle-test")
            {
                ShutdownMode = ShutdownMode.OnExplicitShutdown;
                var output = e.Args.Length > 1 ? Path.GetFullPath(e.Args[1]) : Path.Combine(AppContext.BaseDirectory, "validation");
                Directory.CreateDirectory(output);
                var exit = await Validation.RunAsync(e.Args[0], output, e.Args.Length > 2 ? e.Args[2] : null);
                Shutdown(exit); return;
            }
            var window = new MainWindow(); MainWindow = window;
            if (e.Args.Length == 3 && e.Args[0] == "--diagnostic") window.ShowInitialFailure(e.Args[1], File.ReadAllText(e.Args[2]));
            window.Show();
        }
        catch (Exception ex)
        {
            LogCrash(ex);
            MessageBox.Show(ex.ToString(), "Video Studio başlatılamadı", MessageBoxButton.OK, MessageBoxImage.Error); Shutdown(1);
        }
    }
}
