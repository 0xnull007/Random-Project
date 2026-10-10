using System.Collections.Immutable;
using System.Text;
using Quill.Core.Units;

namespace Quill.Core.Model;

/// <summary>A reviewer's comment attached to the runs that carry its id.</summary>
public sealed record Comment(int Id, string Author, string? Initials, DateTimeOffset? Date, ImmutableList<Block> Blocks)
{
    /// <summary>The comment text with paragraphs separated by line feeds.</summary>
    public string Text
    {
        get
        {
            var text = new StringBuilder();
            foreach (Block block in Blocks)
            {
                if (block is not Paragraph paragraph)
                {
                    continue;
                }

                if (text.Length > 0)
                {
                    text.Append('\n');
                }

                foreach (Inline inline in paragraph.Inlines)
                {
                    if (inline is Run run)
                    {
                        text.Append(run.Text);
                    }
                }
            }

            return text.ToString();
        }
    }
}

/// <summary>Styles comments use.</summary>
public static class CommentSupport
{
    public const string TextStyleId = "CommentText";
    public const string ReferenceStyleId = "CommentReference";

    public static IEnumerable<Style> RequiredStyles(StyleSheet sheet)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        yield return new Style
        {
            Id = TextStyleId,
            Name = "annotation text",
            BasedOn = sheet.DefaultParagraphStyleId,
            Priority = 99,
            ParagraphProperties = new ParagraphProperties { SpaceAfter = Twips.Zero, LineSpacing = LineSpacing.Single },
            RunProperties = new RunProperties { FontSize = HalfPoints.FromPoints(10) },
        };
        yield return new Style
        {
            Id = ReferenceStyleId,
            Name = "annotation reference",
            Type = StyleType.Character,
            BasedOn = sheet.DefaultCharacterStyleId,
            Priority = 99,
            RunProperties = new RunProperties { FontSize = HalfPoints.FromPoints(8) },
        };
    }
}
