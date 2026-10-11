using Quill.Core.Units;
namespace Quill.Core.Model;

public enum RevisionKind
{
    Insertion,
    Deletion,
}

/// <summary>How tracked changes are shown (Word's Display for Review). A view setting, not part of the document.</summary>
public enum RevisionView
{
    /// <summary>Insertions underlined and deletions struck through in the author's colour, with change bars.</summary>
    AllMarkup,

    /// <summary>The text as it would read with every change accepted, with a change bar beside changed lines.</summary>
    SimpleMarkup,

    /// <summary>The text with every change accepted and no markup.</summary>
    NoMarkup,

    /// <summary>The text as it was before any change: insertions hidden, deletions shown as plain text.</summary>
    Original,
}

/// <summary>
/// A tracked change on a run: who inserted or deleted it and when. Dates are kept to the minute so continuous
/// typing stays one run.
/// </summary>
public sealed record Revision(RevisionKind Kind, string Author, DateTime Date)
{
    private static readonly string[] Palette = ["C00000", "0070C0", "00B050", "7030A0", "ED7D31", "008080", "C71585", "806000"];

    public bool IsInsertion => Kind == RevisionKind.Insertion;

    public bool IsDeletion => Kind == RevisionKind.Deletion;

    public static Revision Insertion(string author, DateTime now) => new(RevisionKind.Insertion, author, Truncate(now));

    public static Revision Deletion(string author, DateTime now) => new(RevisionKind.Deletion, author, Truncate(now));

    /// <summary>A stable colour per author for markup, like Word's by-author colouring.</summary>
    public static DocColor ColorFor(string author)
    {
        ArgumentNullException.ThrowIfNull(author);
        int hash = 17;
        foreach (char c in author)
        {
            hash = unchecked(hash * 31 + c);
        }

        return DocColor.Parse(Palette[Math.Abs(hash % Palette.Length)]);
    }

    private static DateTime Truncate(DateTime time) => new(time.Year, time.Month, time.Day, time.Hour, time.Minute, 0, time.Kind);
}
