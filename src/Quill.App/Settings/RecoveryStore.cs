using System.IO;
using System.Text.Json;
using Quill.Core.Model;
using Quill.Docx;

namespace Quill.App.Settings;

/// <summary>
/// Autosave copies for crash recovery: each editing session writes its unsaved document to
/// %LOCALAPPDATA%\Quill\Recovery\{id}.docx with a small manifest beside it, and deletes both when the
/// document is saved or discarded. Whatever is left at the next start is offered to the user.
/// </summary>
public static class RecoveryStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public sealed record Entry(string Id, string File, string? OriginalPath, string Name, DateTimeOffset SavedAt);

    private sealed record Manifest(string? OriginalPath, string Name, DateTimeOffset SavedAt);

    public static string Directory => Path.Combine(AppSettings.Directory, "Recovery");

    public static string NewId() => Guid.NewGuid().ToString("N");

    public static void Write(string id, Document document, string? originalPath, string name)
    {
        ArgumentNullException.ThrowIfNull(document);
        System.IO.Directory.CreateDirectory(Directory);
        DocxWriter.WriteFile(document, DocumentFile(id));
        File.WriteAllText(ManifestFile(id), JsonSerializer.Serialize(new Manifest(originalPath, name, DateTimeOffset.Now), Options));
    }

    public static void Delete(string id)
    {
        try
        {
            File.Delete(DocumentFile(id));
            File.Delete(ManifestFile(id));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Leave it; it will be offered again next start.
        }
    }

    public static IReadOnlyList<Entry> Pending()
    {
        var entries = new List<Entry>();
        if (!System.IO.Directory.Exists(Directory))
        {
            return entries;
        }

        foreach (string manifestPath in System.IO.Directory.EnumerateFiles(Directory, "*.json"))
        {
            string id = Path.GetFileNameWithoutExtension(manifestPath);
            string documentPath = DocumentFile(id);
            if (!File.Exists(documentPath))
            {
                continue;
            }

            try
            {
                Manifest? manifest = JsonSerializer.Deserialize<Manifest>(File.ReadAllText(manifestPath), Options);
                if (manifest is not null)
                {
                    entries.Add(new Entry(id, documentPath, manifest.OriginalPath, manifest.Name, manifest.SavedAt));
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                // Skip unreadable manifests.
            }
        }

        return entries.OrderByDescending(e => e.SavedAt).ToList();
    }

    private static string DocumentFile(string id) => Path.Combine(Directory, id + ".docx");

    private static string ManifestFile(string id) => Path.Combine(Directory, id + ".json");
}
