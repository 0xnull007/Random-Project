using System.IO;
using System.Text.Json;

namespace Quill.App.Settings;

/// <summary>User preferences persisted as JSON under %LOCALAPPDATA%\Quill. Loading never throws; a bad file yields defaults.</summary>
public sealed class AppSettings
{
    public const int MaxRecentFiles = 10;

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public List<string> RecentFiles { get; set; } = [];

    public double Zoom { get; set; } = 1.0;

    public double? WindowLeft { get; set; }

    public double? WindowTop { get; set; }

    public double? WindowWidth { get; set; }

    public double? WindowHeight { get; set; }

    public bool WindowMaximized { get; set; }

    /// <summary>Minutes between autosaves of unsaved changes; 0 disables.</summary>
    public int AutosaveMinutes { get; set; } = 2;

    public bool ShowFormattingMarks { get; set; }

    /// <summary>"System", "Light" or "Dark".</summary>
    public string Theme { get; set; } = "System";

    public static string Directory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Quill");

    public static string FilePath => Path.Combine(Directory, "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), Options) ?? new AppSettings();
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // Corrupt or unreadable settings: start fresh.
        }

        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            System.IO.Directory.CreateDirectory(Directory);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, Options));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Settings are a convenience; never fail the app over them.
        }
    }

    public void AddRecent(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        RecentFiles.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
        RecentFiles.Insert(0, path);
        if (RecentFiles.Count > MaxRecentFiles)
        {
            RecentFiles.RemoveRange(MaxRecentFiles, RecentFiles.Count - MaxRecentFiles);
        }
    }

    public void RemoveRecent(string path) => RecentFiles.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
}
