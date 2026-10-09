using System.Collections.Immutable;
using System.Text;

namespace Quill.Core.Model;

/// <summary>A block-level element of a story: a paragraph, or (later) a table.</summary>
public abstract class Block
{
}

/// <summary>Position of an inline within its paragraph's flat text.</summary>
public readonly record struct InlineSpan(int Index, Inline Inline, int Start, int Length)
{
    public int End => Start + Length;
}

/// <summary>
/// A paragraph: an immutable sequence of inlines plus direct paragraph formatting and the run
/// properties of the paragraph mark. Instances are reference-identity keys for layout caches, so
/// never mutate; use the <c>With*</c> methods.
/// </summary>
public sealed class Paragraph : Block
{
    /// <summary>Placeholder character that non-text inlines occupy in <see cref="FlatText"/>.</summary>
    public const char ObjectReplacementChar = '￼';

    private string? _flatText;

    public Paragraph(
        ImmutableArray<Inline> inlines,
        string? styleId = null,
        ParagraphProperties? properties = null,
        RunProperties? markProperties = null)
    {
        Inlines = inlines.IsDefault ? ImmutableArray<Inline>.Empty : inlines;
        StyleId = styleId;
        Properties = properties ?? ParagraphProperties.Empty;
        MarkProperties = markProperties ?? RunProperties.Empty;
    }

    public static Paragraph Empty(string? styleId = null) => new(ImmutableArray<Inline>.Empty, styleId);

    public static Paragraph FromText(string text, string? styleId = null, RunProperties? runProperties = null)
    {
        ArgumentNullException.ThrowIfNull(text);
        ImmutableArray<Inline> inlines = text.Length == 0
            ? ImmutableArray<Inline>.Empty
            : ImmutableArray.Create<Inline>(new Run(text, runProperties));
        return new Paragraph(inlines, styleId);
    }

    /// <summary>Paragraph style id, or null for the document's default paragraph style.</summary>
    public string? StyleId { get; }

    /// <summary>Direct paragraph formatting.</summary>
    public ParagraphProperties Properties { get; }

    /// <summary>Run properties of the paragraph mark (font of an empty paragraph and of newly typed text at its end).</summary>
    public RunProperties MarkProperties { get; }

    public ImmutableArray<Inline> Inlines { get; }

    /// <summary>All inline text concatenated, with U+FFFC for non-text inlines. Cached.</summary>
    public string FlatText => _flatText ??= BuildFlatText();

    /// <summary>Number of UTF-16 code units; the paragraph mark sits at this offset.</summary>
    public int Length
    {
        get
        {
            int length = 0;
            foreach (Inline inline in Inlines)
            {
                length += inline.Length;
            }

            return length;
        }
    }

    public bool IsEmpty => Inlines.IsEmpty;

    public Paragraph WithInlines(ImmutableArray<Inline> inlines) => new(inlines, StyleId, Properties, MarkProperties);

    public Paragraph WithStyle(string? styleId) => new(Inlines, styleId, Properties, MarkProperties);

    public Paragraph WithProperties(ParagraphProperties properties) => new(Inlines, StyleId, properties, MarkProperties);

    public Paragraph WithMarkProperties(RunProperties markProperties) => new(Inlines, StyleId, Properties, markProperties);

    /// <summary>Enumerates inlines with their flat-text spans.</summary>
    public IEnumerable<InlineSpan> Spans()
    {
        int start = 0;
        for (int i = 0; i < Inlines.Length; i++)
        {
            Inline inline = Inlines[i];
            yield return new InlineSpan(i, inline, start, inline.Length);
            start += inline.Length;
        }
    }

    /// <summary>
    /// Finds the inline that contains <paramref name="offset"/>. An offset on a boundary belongs to the
    /// inline that starts there; the paragraph-mark offset returns false.
    /// </summary>
    public bool TryGetInlineAt(int offset, out InlineSpan span)
    {
        foreach (InlineSpan candidate in Spans())
        {
            if (offset >= candidate.Start && offset < candidate.End)
            {
                span = candidate;
                return true;
            }
        }

        span = default;
        return false;
    }

    /// <summary>The run properties that newly typed text at <paramref name="offset"/> should take.</summary>
    public (string? StyleId, RunProperties Properties) GetTypingFormatAt(int offset)
    {
        // Typing takes the formatting of the character before the caret, like Word; at the start of the
        // paragraph it takes the first inline's, and in an empty paragraph the paragraph mark's.
        if (Inlines.IsEmpty)
        {
            return (null, MarkProperties);
        }

        int probe = offset > 0 ? offset - 1 : 0;
        if (TryGetInlineAt(probe, out InlineSpan span))
        {
            return (span.Inline.StyleId, span.Inline.Properties);
        }

        Inline last = Inlines[^1];
        return (last.StyleId, last.Properties);
    }

    private string BuildFlatText()
    {
        var builder = new StringBuilder();
        foreach (Inline inline in Inlines)
        {
            inline.AppendFlatText(builder);
        }

        return builder.ToString();
    }

    public override string ToString() => $"Paragraph[{StyleId ?? "default"}](\"{FlatText}\")";
}

/// <summary>
/// A block whose XML we do not understand yet (tables, anchored drawings...). Preserved verbatim so
/// saving never destroys content; rendered as a placeholder.
/// </summary>
public sealed class OpaqueBlock : Block
{
    public OpaqueBlock(string localName, string outerXml)
    {
        ArgumentNullException.ThrowIfNull(localName);
        ArgumentNullException.ThrowIfNull(outerXml);
        LocalName = localName;
        OuterXml = outerXml;
    }

    /// <summary>Local XML element name, e.g. "tbl".</summary>
    public string LocalName { get; }

    public string OuterXml { get; }

    public override string ToString() => $"OpaqueBlock({LocalName})";
}
