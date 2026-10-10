using System.Collections.Immutable;
using System.Text;
using Quill.Core.Model;

namespace Quill.Core.Editing;

/// <summary>
/// A copied or pasted piece of content: paragraphs and tables. A single paragraph is inserted inline; anything
/// longer splits the target paragraph, with tables placed between paragraphs. Opaque blocks are not carried.
/// </summary>
public sealed class DocumentFragment
{
    public static readonly DocumentFragment Empty = new(ImmutableArray<Block>.Empty);

    public DocumentFragment(ImmutableArray<Block> blocks, ImmutableDictionary<string, ImageData>? images = null)
    {
        Blocks = blocks.IsDefault ? ImmutableArray<Block>.Empty : blocks;
        Paragraphs = Blocks.OfType<Paragraph>().ToImmutableArray();
        Images = images ?? ImmutableDictionary<string, ImageData>.Empty;
    }

    public DocumentFragment(ImmutableArray<Paragraph> paragraphs, ImmutableDictionary<string, ImageData>? images = null)
        : this(paragraphs.IsDefault ? ImmutableArray<Block>.Empty : ImmutableArray<Block>.CastUp(paragraphs), images)
    {
    }

    /// <summary>The content in order: paragraphs and tables.</summary>
    public ImmutableArray<Block> Blocks { get; }

    /// <summary>The top-level paragraphs (tables left out); see <see cref="AllParagraphs"/> for the text inside tables too.</summary>
    public ImmutableArray<Paragraph> Paragraphs { get; }

    /// <summary>Bytes of the pictures referenced by the paragraphs, so a paste into another document keeps them.</summary>
    public ImmutableDictionary<string, ImageData> Images { get; }

    public bool IsEmpty => Blocks.IsEmpty || (Blocks.Length == 1 && Blocks[0] is Paragraph { IsEmpty: true });

    public bool IsSingleParagraph => Blocks.Length == 1 && Blocks[0] is Paragraph;

    public bool HasTables => Blocks.Any(b => b is Table);

    /// <summary>Every paragraph, including those inside table cells.</summary>
    public IEnumerable<Paragraph> AllParagraphs()
    {
        foreach (Block block in Blocks)
        {
            switch (block)
            {
                case Paragraph paragraph:
                    yield return paragraph;
                    break;
                case Table table:
                    foreach (Paragraph inner in table.AllParagraphs())
                    {
                        yield return inner;
                    }

                    break;
            }
        }
    }

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

    /// <summary>Plain text with paragraphs separated by LF; line breaks become LF, page breaks form feed; table cells are tab separated, rows LF separated.</summary>
    public string ToPlainText()
    {
        var builder = new StringBuilder();
        for (int i = 0; i < Blocks.Length; i++)
        {
            if (i > 0)
            {
                builder.Append('\n');
            }

            switch (Blocks[i])
            {
                case Paragraph paragraph:
                    AppendText(builder, paragraph);
                    break;
                case Table table:
                    for (int r = 0; r < table.Rows.Length; r++)
                    {
                        if (r > 0)
                        {
                            builder.Append('\n');
                        }

                        for (int c = 0; c < table.Rows[r].Cells.Length; c++)
                        {
                            if (c > 0)
                            {
                                builder.Append('\t');
                            }

                            bool firstParagraph = true;
                            foreach (Paragraph cellParagraph in table.Rows[r].Cells[c].Blocks.OfType<Paragraph>())
                            {
                                if (!firstParagraph)
                                {
                                    builder.Append(' ');
                                }

                                AppendText(builder, cellParagraph);
                                firstParagraph = false;
                            }
                        }
                    }

                    break;
            }
        }

        return builder.ToString();
    }

    private static void AppendText(StringBuilder builder, Paragraph paragraph)
    {
        foreach (Inline inline in paragraph.Inlines)
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
}
