using System.Collections.Immutable;
using System.Text;
using Quill.Core.Model;

namespace Quill.Core.Editing;

/// <summary>
/// A copied or pasted piece of content: one or more paragraphs. A single paragraph is inserted inline;
/// several paragraphs split the target paragraph. Opaque blocks are not carried.
/// </summary>
public sealed class DocumentFragment
{
    public static readonly DocumentFragment Empty = new(ImmutableArray<Paragraph>.Empty);

    public DocumentFragment(ImmutableArray<Paragraph> paragraphs)
    {
        Paragraphs = paragraphs.IsDefault ? ImmutableArray<Paragraph>.Empty : paragraphs;
    }

    public ImmutableArray<Paragraph> Paragraphs { get; }

    public bool IsEmpty => Paragraphs.IsEmpty || (Paragraphs.Length == 1 && Paragraphs[0].IsEmpty);

    public bool IsSingleParagraph => Paragraphs.Length == 1;

    /// <summary>Builds a fragment from plain text; CR, LF and CRLF separate paragraphs.</summary>
    public static DocumentFragment FromPlainText(string text, RunProperties? runProperties = null, string? paragraphStyleId = null)
    {
        ArgumentNullException.ThrowIfNull(text);
        string[] lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n', '\r');
        var paragraphs = ImmutableArray.CreateBuilder<Paragraph>(lines.Length);
        foreach (string line in lines)
        {
            paragraphs.Add(Paragraph.FromText(line, paragraphStyleId, runProperties));
        }

        return new DocumentFragment(paragraphs.MoveToImmutable());
    }

    /// <summary>Plain text with paragraphs separated by LF; line breaks become LF, page breaks form feed.</summary>
    public string ToPlainText()
    {
        var builder = new StringBuilder();
        for (int i = 0; i < Paragraphs.Length; i++)
        {
            if (i > 0)
            {
                builder.Append('\n');
            }

            foreach (Inline inline in Paragraphs[i].Inlines)
            {
                switch (inline)
                {
                    case Run run:
                        builder.Append(run.Text);
                        break;
                    case Break { Kind: BreakKind.Page }:
                        builder.Append('\f');
                        break;
                    case Break:
                        builder.Append('\n');
                        break;
                    case Field field:
                        builder.Append(field.CachedResult);
                        break;
                }
            }
        }

        return builder.ToString();
    }
}
