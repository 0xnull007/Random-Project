using System.Collections.Immutable;
using System.Text;
using System.Text.RegularExpressions;
using Quill.Core.Text;

namespace Quill.Core.Model;

/// <summary>Bookmarks are named ranges carried on runs (<see cref="RunProperties.Bookmark"/>); REF and PAGEREF fields point at them.</summary>
public static partial class BookmarkSupport
{
    /// <summary>Word's rule: a letter, then letters, digits or underscores, at most 40 characters.</summary>
    public static bool IsValidName(string? name) => name is { Length: > 0 and <= 40 } && NamePattern().IsMatch(name);

    /// <summary>Names Word hides (its own _Toc, _Ref, _GoBack bookmarks).</summary>
    public static bool IsHidden(string name) => name.StartsWith('_');

    /// <summary>Every bookmark name in document order.</summary>
    public static IReadOnlyList<string> Names(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var names = new List<string>();
        for (int s = 0; s < document.Sections.Count; s++)
        {
            foreach ((BlockPath _, Paragraph paragraph) in BlockWalker.Paragraphs(document.Sections[s].Body))
            {
                foreach (Inline inline in paragraph.Inlines)
                {
                    if (inline.Properties.Bookmark is { } name && !names.Contains(name, StringComparer.OrdinalIgnoreCase))
                    {
                        names.Add(name);
                    }
                }
            }
        }

        return names;
    }

    /// <summary>The range of a bookmark: from its first run to its last within the same block list; null when the name is unknown.</summary>
    public static TextRange? Find(Document document, string name)
    {
        ArgumentNullException.ThrowIfNull(document);
        TextPosition? start = null;
        TextPosition? end = null;
        for (int s = 0; s < document.Sections.Count; s++)
        {
            var story = StoryId.Body(s);
            foreach ((BlockPath path, Paragraph paragraph) in BlockWalker.Paragraphs(document.Sections[s].Body))
            {
                if (start is { } found && !found.Block.SameContainer(path))
                {
                    break;
                }

                foreach (InlineSpan span in paragraph.Spans())
                {
                    if (!string.Equals(span.Inline.Properties.Bookmark, name, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    start ??= new TextPosition(story, path, span.Start);
                    end = new TextPosition(story, path, span.End);
                }
            }

            if (start is not null)
            {
                break;
            }
        }

        return start is { } a && end is { } b ? new TextRange(a, b) : null;
    }

    /// <summary>The plain text inside a bookmark (paragraphs joined with a space), or null.</summary>
    public static string? TextOf(Document document, string name)
    {
        if (Find(document, name) is not { } range)
        {
            return null;
        }

        var text = new StringBuilder();
        foreach (ParagraphSpan span in TextRanges.Paragraphs(document, range))
        {
            if (text.Length > 0)
            {
                text.Append(' ');
            }

            int end = Math.Min(span.End, span.Paragraph.Length);
            foreach (InlineSpan inline in span.Paragraph.Spans())
            {
                int from = Math.Max(inline.Start, span.Start);
                int to = Math.Min(inline.End, end);
                if (from >= to)
                {
                    continue;
                }

                switch (inline.Inline)
                {
                    case Run run:
                        text.Append(run.Text, from - inline.Start, to - from);
                        break;
                    case Field field:
                        text.Append(field.CachedResult);
                        break;
                }
            }
        }

        return text.ToString().Trim();
    }

    /// <summary>The bookmark name a REF or PAGEREF instruction points at ("REF MyMark \h" gives "MyMark"), or null.</summary>
    public static string? Target(Field field)
    {
        ArgumentNullException.ThrowIfNull(field);
        if (field.Kind is not (FieldKind.Ref or FieldKind.PageRef))
        {
            return null;
        }

        string[] parts = field.Instruction.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 2 ? parts[1] : null;
    }

    [GeneratedRegex("^[A-Za-z][A-Za-z0-9_]*$")]
    private static partial Regex NamePattern();
}
