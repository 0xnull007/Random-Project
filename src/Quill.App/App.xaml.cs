using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Threading;

namespace Quill.App;

public partial class App : Application
{
    /// <summary>File passed on the command line (Explorer "Open with"), consumed by the main window.</summary>
    public string? StartupFile { get; private set; }

    public static string CrashLogPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Quill", "crash.log");

    protected override void OnStartup(StartupEventArgs e)
    {
        PopupThemeFix.Register();
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) => ReportCrash(args.ExceptionObject as Exception ?? new InvalidOperationException("Unknown fatal error"), fatal: true);
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            WriteCrashLog(args.Exception, "unobserved task exception");
            args.SetObserved();
        };

        if (e.Args.Length > 0 && File.Exists(e.Args[0]))
        {
            StartupFile = Path.GetFullPath(e.Args[0]);
        }

        try
        {
            System.Windows.Shell.JumpList.SetJumpList(this, new System.Windows.Shell.JumpList { ShowRecentCategory = true });
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            // Jump lists are optional.
        }

        base.OnStartup(e);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        // Before the main window exists any exception is fatal; afterwards we try to keep the document alive.
        bool fatal = MainWindow is null || !MainWindow.IsLoaded;
        ReportCrash(e.Exception, fatal);
        e.Handled = !fatal;
        if (fatal)
        {
            Shutdown(1);
        }
    }

    private static void ReportCrash(Exception exception, bool fatal)
    {
        string logPath = WriteCrashLog(exception, fatal ? "fatal error" : "error");
        string headline = fatal ? "Quill hit a fatal error and has to close." : "Quill hit an error. Your document is still open; please save it now.";
        try
        {
            MessageBox.Show(
                $"{headline}\n\n{exception.GetType().Name}: {exception.Message}\n\nDetails were written to:\n{logPath}",
                "Quill",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        catch (Exception)
        {
            // Nothing more we can do if even the message box fails.
        }
    }

    private static string WriteCrashLog(Exception exception, string kind)
    {
        string path = CrashLogPath;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var entry = new StringBuilder();
            entry.Append(DateTimeOffset.Now.ToString("u", System.Globalization.CultureInfo.InvariantCulture))
                .Append(' ').Append(kind).AppendLine()
                .AppendLine(exception.ToString())
                .AppendLine(new string('-', 80));
            File.AppendAllText(path, entry.ToString());
        }
        catch (Exception)
        {
            // Logging must never throw.
        }

        return path;
    }
}
