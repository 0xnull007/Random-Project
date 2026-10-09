using System.IO;
using System.Windows;

namespace Quill.App;

public partial class App : Application
{
    /// <summary>File passed on the command line (Explorer "Open with"), consumed by the main window.</summary>
    public string? StartupFile { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        if (e.Args.Length > 0 && File.Exists(e.Args[0]))
        {
            StartupFile = Path.GetFullPath(e.Args[0]);
        }

        base.OnStartup(e);
    }
}
