using System.Collections.Immutable;
using Quill.Core.Model;
using Quill.Core.Text;

namespace Quill.Core.Editing;

public sealed record SearchOptions(bool MatchCase = false, bool WholeWord = false)
{
    public static readonly SearchOptions Default = new();

    public StringComparison Comparison => MatchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
}

/// <summary>
/// Plain-text search over a document. Stories are searched in Word's order: every section body first, then
/// headers and footers. A match never crosses a paragraph boundary.
/// </summary>
public static class TextSearch
{
    public static IReadOnlyList<TextRange> FindAll(Document document, string query, SearchOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        options ??= SearchOptions.Default;
        var results = new List<TextRange>();
        if (string.IsNullOrEmpty(query))
        {
            return results;
        }

        foreach ((StoryId story, ImmutableList<Block> blocks) in OrderedStories(document))
        {
            for (int i = 0; i < blocks.Count; i++)
            {
                if (blocks[i] is Paragraph paragraph)
                {
                    results.AddRange(FindInParagraph(story, BlockPath.Of(i), paragraph, query, options));
                }
            }
        }

        return results;
    }

    public static IEnumerable<TextRange> FindInParagraph(StoryId story, BlockPath path, Paragraph paragraph, string query, SearchOptions options)
    {
        ArgumentNullException.ThrowIfNull(paragraph);
        ArgumentNullException.ThrowIfNull(options);
        if (string.IsNullOrEmpty(query))
        {
            yield break;
        }

        string text = paragraph.FlatText;
        int start = 0;
        while (start <= text.Length - query.Length)
        {
            int index = text.IndexOf(query, start, options.Comparison);
            if (index < 0)
            {
                yield break;
            }

            if (!options.WholeWord || IsWholeWord(text, index, query.Length))
            {
                yield return new TextRange(new TextPosition(story, path, index), new TextPosition(story, path, index + query.Length));
            }

            start = index + 1;
        }
    }

    /// <summary>
    /// The first match starting at or after <paramref name="from"/> (or the last one starting before it when
    /// <paramref name="backwards"/>), wrapping around the document. <paramref name="wrapped"/> reports a wrap.
    /// </summary>
    public static TextRange? FindNext(Document document, TextPosition from, string query, SearchOptions? options, bool backwards, out bool wrapped)
    {
        ArgumentNullException.ThrowIfNull(document);
        wrapped = false;
        IReadOnlyList<TextRange> all = FindAll(document, query, options);
        if (all.Count == 0)
        {
            return null;
        }

        List<StoryId> order = OrderedStories(document).Select(s => s.Story).ToList();
        int fromRank = Math.Max(0, order.IndexOf(from.Story));

        int CompareToFrom(TextRange range)
        {
            int rank = order.IndexOf(range.Story);
            return rank != fromRank ? rank.CompareTo(fromRank) : range.Start.CompareTo(from);
        }

        if (!backwards)
        {
            foreach (TextRange range in all)
            {
                if (CompareToFrom(range) >= 0)
                {
                    return range;
                }
            }

            wrapped = true;
            return all[0];
        }

        for (int i = all.Count - 1; i >= 0; i--)
        {
            if (CompareToFrom(all[i]) < 0)
            {
                return all[i];
            }
        }

        wrapped = true;
        return all[^1];
    }

    /// <summary>Position of <paramref name="match"/> in the full result list (0-based), or -1.</summary>
    public static int IndexOf(IReadOnlyList<TextRange> matches, TextRange match)
    {
        ArgumentNullException.ThrowIfNull(matches);
        for (int i = 0; i < matches.Count; i++)
        {
            if (matches[i] == match)
            {
                return i;
            }
        }

        return -1;
    }

    private static IEnumerable<(StoryId Story, ImmutableList<Block> Blocks)> OrderedStories(Document document)
    {
        List<(StoryId Id, ImmutableList<Block> Blocks)> stories = document.Stories().ToList();
        foreach ((StoryId id, ImmutableList<Block> blocks) in stories.Where(s => s.Id.IsBody))
        {
            yield return (id, blocks);
        }

        foreach ((StoryId id, ImmutableList<Block> blocks) in stories.Where(s => !s.Id.IsBody))
        {
            yield return (id, blocks);
        }
    }

    private static bool IsWholeWord(string text, int index, int length) =>
        (index == 0 || !Words.IsWordCharacter(text[index - 1]))
        && (index + length >= text.Length || !Words.IsWordCharacter(text[index + length]));
}
