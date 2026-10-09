using Quill.Core.Model;

namespace Quill.Docx;

/// <summary>Something in the file that Quill could not represent. Shown to the user before they overwrite the original.</summary>
public sealed record LoadWarning(string Message);

public sealed class LoadResult
{
    public LoadResult(Document document, IReadOnlyList<LoadWarning> warnings)
    {
        Document = document;
        Warnings = warnings;
    }

    public Document Document { get; }

    public IReadOnlyList<LoadWarning> Warnings { get; }

    /// <summary>True when saving over the original would drop content.</summary>
    public bool HasLossyContent => Warnings.Count > 0;
}
