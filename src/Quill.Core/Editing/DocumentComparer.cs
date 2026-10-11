using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Quill.Core.Model;

namespace Quill.Core.Editing;

/// <summary>
/// Word's Compare: produces the revised document with every difference from the original shown as a tracked
/// change. Paragraphs are aligned by content (equal paragraphs first, then similar ones in order); inside an
/// aligned pair the words are diffed, deleted words being copied from the original and marked deleted. Tables
/// and other blocks that differ are shown as the old block deleted followed by the new one inserted. Tracked
/// changes already in either document are accepted first, as Word does. Pictures count as changed when their
/// bytes or size differ.
/// </summary>
public static partial class DocumentComparer
{
    private const double SimilarityThreshold = 0.5;
    private const int MaxDiffCells = 4_000_000;

    public static Document Compare(Document original, Document revised, string author, DateTime date)
    {
        ArgumentNullException.ThrowIfNull(original);
        ArgumentNullException.ThrowIfNull(revised);
        ArgumentException.ThrowIfNullOrWhiteSpace(author);

        original = Accepted(original);
        revised = Accepted(revised);
        var context = new Context(Revision.Insertion(author, date), Revision.Deletion(author, date), original.Images, revised.Images);

        var sections = ImmutableList.CreateBuilder<Section>();
        for (int s = 0; s < revised.Sections.Count; s++)
        {
            ImmutableList<Block> before = s < original.Sections.Count ? original.Sections[s].Body : ImmutableList<Block>.Empty;
            if (s == revised.Sections.Count - 1 && original.Sections.Count > revised.Sections.Count)
            {
                // Sections the revised document no longer has: their content shows as deleted at the end.
                before = before.AddRange(original.Sections.Skip(revised.Sections.Count).SelectMany(x => x.Body));
            }

            sections.Add(revised.Sections[s].WithBody(CompareBlocks(before, revised.Sections[s].Body, context)));
        }

        return revised.WithSections(sections.ToImmutable()).WithImages(context.Images);
    }

    /// <summary>The document with all its tracked changes accepted.</summary>
    private static Document Accepted(Document document)
    {
        if (!DocumentEditor.AllParagraphs(document).Any(p => p.Paragraph.Inlines.Any(i => i.Properties.Revision is not null)))
        {
            return document;
        }

        var session = new EditingSession(document);
        session.AcceptAllChanges();
        return session.Document;
    }

    // ------------------------------------------------------------------ blocks

    private static ImmutableList<Block> CompareBlocks(ImmutableList<Block> before, ImmutableList<Block> after, Context context)
    {
        string[] beforeKeys = before.Select(b => BlockKey(b, context.OriginalImages)).ToArray();
        string[] afterKeys = after.Select(b => BlockKey(b, context.RevisedImages)).ToArray();
        var result = ImmutableList.CreateBuilder<Block>();
        int i = 0;
        int j = 0;
        foreach ((int MatchBefore, int MatchAfter) in Diff.Matches(beforeKeys, afterKeys, MaxDiffCells).Append((before.Count, after.Count)))
        {
            EmitUnmatched(before, after, i, MatchBefore, j, MatchAfter, context, result);
            if (MatchAfter < after.Count)
            {
                result.Add(after[MatchAfter]);
            }

            i = MatchBefore + 1;
            j = MatchAfter + 1;
        }

        return result.ToImmutable();
    }

    /// <summary>
    /// Blocks between two exact matches: a new paragraph pairs with the first remaining old paragraph similar
    /// to it (older ones before it are deleted); a new block without a partner is inserted after the old blocks
    /// that no later new block will claim, so deletions precede the insertions that replace them, as in Word.
    /// </summary>
    private static void EmitUnmatched(ImmutableList<Block> before, ImmutableList<Block> after, int b0, int b1, int a0, int a1, Context context, ImmutableList<Block>.Builder result)
    {
        int b = b0;
        for (int a = a0; a < a1; a++)
        {
            int partner = PartnerFor(before, after[a], b, b1, context);
            if (partner >= 0)
            {
                for (int k = b; k < partner; k++)
                {
                    result.Add(Marked(before[k], context, fromOriginal: true));
                }

                result.Add(CompareParagraphs((Paragraph)before[partner], (Paragraph)after[a], context));
                b = partner + 1;
                continue;
            }

            int claimed = b1;
            for (int k = b; k < b1 && claimed == b1; k++)
            {
                for (int later = a + 1; later < a1; later++)
                {
                    if (PartnerFor(before, after[later], k, k + 1, context) == k)
                    {
                        claimed = k;
                        break;
                    }
                }
            }

            for (int k = b; k < claimed; k++)
            {
                result.Add(Marked(before[k], context, fromOriginal: true));
            }

            b = claimed;
            result.Add(Marked(after[a], context, fromOriginal: false));
        }

        for (int k = b; k < b1; k++)
        {
            result.Add(Marked(before[k], context, fromOriginal: true));
        }
    }

    /// <summary>The first old paragraph in [<paramref name="from"/>, <paramref name="to"/>) similar enough to <paramref name="candidate"/>, or -1.</summary>
    private static int PartnerFor(ImmutableList<Block> before, Block candidate, int from, int to, Context context)
    {
        if (candidate is not Paragraph paragraph)
        {
            return -1;
        }

        for (int k = from; k < to; k++)
        {
            if (before[k] is Paragraph previous && Similarity(previous, paragraph, context) >= SimilarityThreshold)
            {
                return k;
            }
        }

        return -1;
    }

    private static string BlockKey(Block block, ImageStore images) => block switch
    {
        Paragraph paragraph => string.Concat(Keys(paragraph, images, out _)),
        Table table => "\u0001" + string.Join("\u0002", BlockWalker.Paragraphs(ImmutableList.Create<Block>(table)).Select(p => string.Concat(Keys(p.Paragraph, images, out _)))),
        OpaqueBlock opaque => "\u0003" + opaque.OuterXml,
        _ => block.ToString() ?? string.Empty,
    };

    // ------------------------------------------------------------------ paragraphs

    [GeneratedRegex(@"\S+|\s+")]
    private static partial Regex TokenPattern();

    /// <summary>
    /// Comparison keys of a paragraph's tokens (words and whitespace runs). A picture's key carries its size and a
    /// hash of its bytes so a swapped picture counts as a change.
    /// </summary>
    private static string[] Keys(Paragraph paragraph, ImageStore images, out List<(int Start, int End)> tokens)
    {
        string text = paragraph.FlatText;
        tokens = [];
        var keys = new List<string>();
        foreach (Match match in TokenPattern().Matches(text))
        {
            tokens.Add((match.Index, match.Index + match.Length));
            string token = match.Value;
            if (token.Contains(Paragraph.ObjectReplacementChar))
            {
                var builder = new StringBuilder(token.Length + 32);
                for (int i = 0; i < token.Length; i++)
                {
                    builder.Append(token[i]);
                    if (token[i] == Paragraph.ObjectReplacementChar && paragraph.TryGetInlineAt(match.Index + i, out InlineSpan span) && span.Inline is InlineImage image)
                    {
                        builder.Append(CultureInfo.InvariantCulture, $"{image.Width.Value}x{image.Height.Value}:{Fingerprint(images.Get(image.ImageId))}");
                    }
                }

                token = builder.ToString();
            }

            keys.Add(token);
        }

        return [.. keys];
    }

    /// <summary>FNV-1a over the picture bytes; the comparison only needs equal pictures to agree.</summary>
    private static string Fingerprint(ImageData? data)
    {
        if (data is null)
        {
            return "none";
        }

        ulong hash = 14695981039346656037UL;
        foreach (byte b in data.Bytes)
        {
            hash = (hash ^ b) * 1099511628211UL;
        }

        return data.Bytes.Length.ToString(CultureInfo.InvariantCulture) + "-" + hash.ToString("x", CultureInfo.InvariantCulture);
    }

    /// <summary>Share of tokens the two paragraphs have in common (0..1), used to pair changed paragraphs.</summary>
    private static double Similarity(Paragraph before, Paragraph after, Context context)
    {
        string[] a = Keys(before, context.OriginalImages, out _);
        string[] b = Keys(after, context.RevisedImages, out _);
        if (a.Length == 0 && b.Length == 0)
        {
            return 1;
        }

        int common = Diff.Matches(a, b, MaxDiffCells).Count;
        return 2.0 * common / (a.Length + b.Length);
    }

    private static Paragraph CompareParagraphs(Paragraph before, Paragraph after, Context context)
    {
        string[] beforeKeys = Keys(before, context.OriginalImages, out List<(int Start, int End)> beforeTokens);
        string[] afterKeys = Keys(after, context.RevisedImages, out List<(int Start, int End)> afterTokens);
        IReadOnlyList<(int Before, int After)> matches = Diff.Matches(beforeKeys, afterKeys, MaxDiffCells);

        var inlines = ImmutableArray.CreateBuilder<Inline>();
        int i = 0;
        int j = 0;
        foreach ((int MatchBefore, int MatchAfter) in matches.Append((beforeTokens.Count, afterTokens.Count)))
        {
            if (MatchBefore > i)
            {
                inlines.AddRange(context.CopyFromOriginal(InlineOps.Slice(before, beforeTokens[i].Start, beforeTokens[MatchBefore - 1].End)));
            }

            if (MatchAfter > j)
            {
                inlines.AddRange(Mark(InlineOps.Slice(after, afterTokens[j].Start, afterTokens[MatchAfter - 1].End), context.Inserted));
            }

            if (MatchAfter < afterTokens.Count)
            {
                inlines.AddRange(InlineOps.Slice(after, afterTokens[MatchAfter].Start, afterTokens[MatchAfter].End));
            }

            i = MatchBefore + 1;
            j = MatchAfter + 1;
        }

        return after.WithInlines(InlineOps.Normalize(inlines));
    }

    // ------------------------------------------------------------------ marking

    private static Block Marked(Block block, Context context, bool fromOriginal) =>
        BlockWalker.MapParagraphs(ImmutableList.Create(block), paragraph =>
            paragraph.WithInlines(fromOriginal ? context.CopyFromOriginal(paragraph.Inlines) : Mark(paragraph.Inlines, context.Inserted)))[0];

    private static ImmutableArray<Inline> Mark(ImmutableArray<Inline> inlines, Revision revision)
    {
        var result = ImmutableArray.CreateBuilder<Inline>(inlines.Length);
        foreach (Inline inline in inlines)
        {
            result.Add(inline.WithProperties(inline.Properties with { Revision = revision }));
        }

        return result.ToImmutable();
    }

    /// <summary>Per-comparison state: the two revision stamps, both picture stores and the merged one.</summary>
    private sealed class Context(Revision inserted, Revision deleted, ImageStore originalImages, ImageStore revisedImages)
    {
        private readonly Dictionary<string, string> _imageIds = new(StringComparer.Ordinal);

        public Revision Inserted { get; } = inserted;

        public Revision Deleted { get; } = deleted;

        public ImageStore OriginalImages { get; } = originalImages;

        public ImageStore RevisedImages { get; } = revisedImages;

        /// <summary>The revised document's pictures plus any original picture that deleted text still shows.</summary>
        public ImageStore Images { get; private set; } = revisedImages;

        /// <summary>
        /// Inlines copied from the original as deleted text: notes, comments and bookmarks of the other document are
        /// dropped, and pictures are re-registered so ids never collide with the revised document's.
        /// </summary>
        public ImmutableArray<Inline> CopyFromOriginal(ImmutableArray<Inline> inlines)
        {
            var result = ImmutableArray.CreateBuilder<Inline>(inlines.Length);
            foreach (Inline inline in inlines)
            {
                if (inline is FootnoteReference)
                {
                    continue;
                }

                Inline copy = inline is InlineImage image
                    ? new InlineImage(MapImage(image.ImageId), image.Width, image.Height, image.Properties, image.StyleId, image.Anchor)
                    : inline;
                result.Add(copy.WithProperties(copy.Properties with { Revision = Deleted, CommentId = null, Bookmark = null }));
            }

            return result.ToImmutable();
        }

        private string MapImage(string originalId)
        {
            if (_imageIds.TryGetValue(originalId, out string? mapped))
            {
                return mapped;
            }

            ImageData? data = OriginalImages.Get(originalId);
            string id = originalId;
            if (data is not null)
            {
                ImageData? existing = Images.Get(originalId);
                if (existing is null)
                {
                    Images = Images.With(originalId, data);
                }
                else if (!ReferenceEquals(existing, data) && !existing.Bytes.AsSpan().SequenceEqual(data.Bytes))
                {
                    id = Images.NewId();
                    Images = Images.With(id, data);
                }
            }

            _imageIds[originalId] = id;
            return id;
        }
    }
}

/// <summary>Longest-common-subsequence matching over two sequences, after trimming the common prefix and suffix.</summary>
internal static class Diff
{
    /// <summary>Index pairs of matching elements in increasing order; when the middle part is too large to diff, nothing in it matches.</summary>
    public static IReadOnlyList<(int Before, int After)> Matches(IReadOnlyList<string> before, IReadOnlyList<string> after, int maxCells)
    {
        var matches = new List<(int, int)>();
        int prefix = 0;
        while (prefix < before.Count && prefix < after.Count && string.Equals(before[prefix], after[prefix], StringComparison.Ordinal))
        {
            matches.Add((prefix, prefix));
            prefix++;
        }

        int suffix = 0;
        while (suffix < before.Count - prefix && suffix < after.Count - prefix
            && string.Equals(before[before.Count - 1 - suffix], after[after.Count - 1 - suffix], StringComparison.Ordinal))
        {
            suffix++;
        }

        int n = before.Count - prefix - suffix;
        int m = after.Count - prefix - suffix;
        if (n > 0 && m > 0 && (long)n * m <= maxCells)
        {
            // Classic DP table over the middle; backtracking prefers the earliest matches.
            var table = new int[n + 1, m + 1];
            for (int i = n - 1; i >= 0; i--)
            {
                for (int j = m - 1; j >= 0; j--)
                {
                    table[i, j] = string.Equals(before[prefix + i], after[prefix + j], StringComparison.Ordinal)
                        ? table[i + 1, j + 1] + 1
                        : Math.Max(table[i + 1, j], table[i, j + 1]);
                }
            }

            int x = 0;
            int y = 0;
            while (x < n && y < m)
            {
                if (string.Equals(before[prefix + x], after[prefix + y], StringComparison.Ordinal))
                {
                    matches.Add((prefix + x, prefix + y));
                    x++;
                    y++;
                }
                else if (table[x + 1, y] >= table[x, y + 1])
                {
                    x++;
                }
                else
                {
                    y++;
                }
            }
        }

        for (int k = suffix; k > 0; k--)
        {
            matches.Add((before.Count - k, after.Count - k));
        }

        return matches;
    }
}
