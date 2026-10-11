using System.Collections.Immutable;
using Quill.Core.Styles;
using Quill.Core.Text;
using Quill.Core.Units;

namespace Quill.Core.Model;

/// <summary>Document-wide settings (<c>w:settings</c>).</summary>
public sealed record DocumentSettings
{
    public static readonly DocumentSettings Default = new();

    /// <summary>Interval of the implicit tab stops used when no custom stop applies.</summary>
    public Twips DefaultTabStop { get; init; } = Twips.FromInches(0.5);

    /// <summary>Use the Even header/footer variants on even pages.</summary>
    public bool EvenAndOddHeaders { get; init; }

    /// <summary>Record insertions and deletions as tracked changes (<c>w:trackRevisions</c>).</summary>
    public bool TrackChanges { get; init; }

    /// <summary>Background colour of every page (<c>w:background</c>); shown on screen, not printed, like Word.</summary>
    public DocColor? PageColor { get; init; }
}

public sealed record DocumentMetadata
{
    public static readonly DocumentMetadata Empty = new();

    public string? Title { get; init; }

    public string? Author { get; init; }

    public string? Subject { get; init; }

    public string? Description { get; init; }

    public DateTimeOffset? Created { get; init; }

    public DateTimeOffset? Modified { get; init; }
}

/// <summary>
/// The root of the immutable document tree. Every edit returns a new <see cref="Document"/> that shares
/// unchanged subtrees with the old one; <c>ReferenceEquals</c> on paragraphs therefore means "unchanged".
/// </summary>
public sealed class Document
{
    public Document(ImmutableList<Section> sections, StyleSheet styles, DocumentSettings? settings = null, DocumentMetadata? metadata = null, ListStore? lists = null, ImageStore? images = null, ImmutableDictionary<int, ImmutableList<Block>>? footnotes = null, ImmutableDictionary<int, Comment>? comments = null, ImmutableDictionary<int, ImmutableList<Block>>? endnotes = null)
    {
        ArgumentNullException.ThrowIfNull(sections);
        ArgumentNullException.ThrowIfNull(styles);
        if (sections.Count == 0)
        {
            throw new ArgumentException("A document needs at least one section.", nameof(sections));
        }

        Sections = sections;
        Styles = styles;
        Settings = settings ?? DocumentSettings.Default;
        Metadata = metadata ?? DocumentMetadata.Empty;
        Lists = lists ?? ListStore.Empty;
        Images = images ?? ImageStore.Empty;
        Footnotes = footnotes ?? ImmutableDictionary<int, ImmutableList<Block>>.Empty;
        Comments = comments ?? ImmutableDictionary<int, Comment>.Empty;
        Endnotes = endnotes ?? ImmutableDictionary<int, ImmutableList<Block>>.Empty;
    }

    /// <summary>A new blank document with one empty paragraph.</summary>
    public static Document CreateNew(bool metricPaper = false)
    {
        SectionProperties page = metricPaper ? SectionProperties.A4 : SectionProperties.Letter;
        return new Document(ImmutableList.Create(Section.CreateEmpty(page)), DefaultStyleSheet.Create());
    }

    public ImmutableList<Section> Sections { get; }

    public StyleSheet Styles { get; }

    public DocumentSettings Settings { get; }

    public DocumentMetadata Metadata { get; }

    /// <summary>List definitions and instances referenced by paragraphs.</summary>
    public ListStore Lists { get; }

    /// <summary>Picture bytes referenced by <see cref="InlineImage"/> inlines.</summary>
    public ImageStore Images { get; }

    public Document WithSections(ImmutableList<Section> sections) => new(sections, Styles, Settings, Metadata, Lists, Images, Footnotes, Comments, Endnotes);

    public Document WithLists(ListStore lists) => new(Sections, Styles, Settings, Metadata, lists, Images, Footnotes, Comments, Endnotes);

    /// <summary>Footnote text by note id; a body paragraph refers to a note through a <see cref="FootnoteReference"/>.</summary>
    public ImmutableDictionary<int, ImmutableList<Block>> Footnotes { get; }

    /// <summary>Comments by id; runs carry the id of the comment they belong to.</summary>
    public ImmutableDictionary<int, Comment> Comments { get; }

    /// <summary>Endnote text by note id, shown after the last section's text.</summary>
    public ImmutableDictionary<int, ImmutableList<Block>> Endnotes { get; }

    public Document WithEndnotes(ImmutableDictionary<int, ImmutableList<Block>> endnotes) => new(Sections, Styles, Settings, Metadata, Lists, Images, Footnotes, Comments, endnotes);

    public Document WithComments(ImmutableDictionary<int, Comment> comments) => new(Sections, Styles, Settings, Metadata, Lists, Images, Footnotes, comments, Endnotes);


    public Document WithFootnotes(ImmutableDictionary<int, ImmutableList<Block>> footnotes) => new(Sections, Styles, Settings, Metadata, Lists, Images, footnotes, Comments, Endnotes);


    public Document WithImages(ImageStore images) => new(Sections, Styles, Settings, Metadata, Lists, images, Footnotes, Comments, Endnotes);


    public Document WithSection(int index, Section section) => WithSections(Sections.SetItem(index, section));

    public Document WithStyles(StyleSheet styles) => new(Sections, styles, Settings, Metadata, Lists, Images, Footnotes, Comments, Endnotes);

    public Document WithSettings(DocumentSettings settings) => new(Sections, Styles, settings, Metadata, Lists, Images, Footnotes, Comments, Endnotes);

    public Document WithMetadata(DocumentMetadata metadata) => new(Sections, Styles, Settings, metadata, Lists, Images, Footnotes, Comments, Endnotes);

    /// <summary>The blocks of a story, or null when the section or header/footer variant does not exist.</summary>
    public ImmutableList<Block>? TryGetStory(StoryId story)
    {
        if (story.Kind == StoryKind.Footnote)
        {
            return Footnotes.TryGetValue(story.Note, out ImmutableList<Block>? note) ? note : null;
        }
        if (story.Kind == StoryKind.Endnote)
        {
            return Endnotes.TryGetValue(story.Note, out ImmutableList<Block>? endnote) ? endnote : null;
        }


        if (story.SectionIndex < 0 || story.SectionIndex >= Sections.Count)
        {
            return null;
        }

        Section section = Sections[story.SectionIndex];
        return story.Kind switch
        {
            StoryKind.Body => section.Body,
            StoryKind.HeaderDefault => section.Headers.Default,
            StoryKind.HeaderFirst => section.Headers.First,
            StoryKind.HeaderEven => section.Headers.Even,
            StoryKind.FooterDefault => section.Footers.Default,
            StoryKind.FooterFirst => section.Footers.First,
            StoryKind.FooterEven => section.Footers.Even,
            _ => throw new ArgumentOutOfRangeException(nameof(story)),
        };
    }

    public ImmutableList<Block> GetStory(StoryId story) =>
        TryGetStory(story) ?? throw new ArgumentException($"Story {story} does not exist.", nameof(story));

    /// <summary>Replaces the blocks of a story, creating the header/footer variant if needed.</summary>
    public Document WithStory(StoryId story, ImmutableList<Block> blocks)
    {
        ArgumentNullException.ThrowIfNull(blocks);
        if (story.Kind == StoryKind.Footnote)
        {
            return WithFootnotes(Footnotes.SetItem(story.Note, blocks));
        }
        if (story.Kind == StoryKind.Endnote)
        {
            return WithEndnotes(Endnotes.SetItem(story.Note, blocks));
        }


        Section section = Sections[story.SectionIndex];
        Section updated = story.Kind switch
        {
            StoryKind.Body => section.WithBody(blocks),
            StoryKind.HeaderDefault => section.WithHeaders(section.Headers.With(HeaderFooterVariant.Default, blocks)),
            StoryKind.HeaderFirst => section.WithHeaders(section.Headers.With(HeaderFooterVariant.First, blocks)),
            StoryKind.HeaderEven => section.WithHeaders(section.Headers.With(HeaderFooterVariant.Even, blocks)),
            StoryKind.FooterDefault => section.WithFooters(section.Footers.With(HeaderFooterVariant.Default, blocks)),
            StoryKind.FooterFirst => section.WithFooters(section.Footers.With(HeaderFooterVariant.First, blocks)),
            StoryKind.FooterEven => section.WithFooters(section.Footers.With(HeaderFooterVariant.Even, blocks)),
            _ => throw new ArgumentOutOfRangeException(nameof(story)),
        };
        return WithSection(story.SectionIndex, updated);
    }

    public Block? TryGetBlock(StoryId story, BlockPath path)
    {
        ImmutableList<Block>? blocks = TryGetContainer(story, path);
        return blocks is not null && path.Last >= 0 && path.Last < blocks.Count ? blocks[path.Last] : null;
    }

    /// <summary>The block list that directly holds the block at <paramref name="path"/>: the story for a top-level block, the table cell for a nested one.</summary>
    public ImmutableList<Block>? TryGetContainer(StoryId story, BlockPath path)
    {
        ImmutableList<Block>? blocks = TryGetStory(story);
        ImmutableArray<int> indices = path.Indices;
        for (int i = 0; blocks is not null && i + 3 < indices.Length; i += 3)
        {
            if (indices[i] < 0 || indices[i] >= blocks.Count || blocks[indices[i]] is not Table table)
            {
                return null;
            }

            int row = indices[i + 1];
            int cell = indices[i + 2];
            if (row < 0 || row >= table.Rows.Length || cell < 0 || cell >= table.Rows[row].Cells.Length)
            {
                return null;
            }

            blocks = table.Rows[row].Cells[cell].Blocks;
        }

        return blocks;
    }

    public ImmutableList<Block> GetContainer(StoryId story, BlockPath path) =>
        TryGetContainer(story, path) ?? throw new ArgumentException($"No block list at {path} in {story}.", nameof(path));

    /// <summary>Replaces the block list that holds the block at <paramref name="path"/>, rebuilding the enclosing tables.</summary>
    public Document WithContainer(StoryId story, BlockPath path, ImmutableList<Block> blocks)
    {
        ArgumentNullException.ThrowIfNull(blocks);
        return WithStory(story, ReplaceContainer(GetStory(story), path.Indices, 0, blocks));
    }

    private static ImmutableList<Block> ReplaceContainer(ImmutableList<Block> blocks, ImmutableArray<int> indices, int offset, ImmutableList<Block> replacement)
    {
        if (offset + 1 >= indices.Length)
        {
            return replacement;
        }

        int tableIndex = indices[offset];
        int row = indices[offset + 1];
        int cell = indices[offset + 2];
        if (blocks[tableIndex] is not Table table)
        {
            throw new ArgumentException("The path does not point into a table.", nameof(indices));
        }

        ImmutableList<Block> inner = ReplaceContainer(table.Rows[row].Cells[cell].Blocks, indices, offset + 3, replacement);
        return blocks.SetItem(tableIndex, table.WithCellBlocks(row, cell, inner));
    }


    public Paragraph? TryGetParagraph(TextPosition position) => TryGetBlock(position.Story, position.Block) as Paragraph;

    public Paragraph GetParagraph(TextPosition position) =>
        TryGetParagraph(position) ?? throw new ArgumentException($"No paragraph at {position}.", nameof(position));

    /// <summary>True when <paramref name="position"/> addresses an existing paragraph and a valid offset in it.</summary>
    public bool IsValid(TextPosition position)
    {
        Paragraph? paragraph = TryGetParagraph(position);
        return paragraph is not null && position.Offset >= 0 && position.Offset <= paragraph.Length;
    }

    /// <summary>Enumerates all stories that exist, body first.</summary>
    public IEnumerable<(StoryId Id, ImmutableList<Block> Blocks)> Stories()
    {
        for (int i = 0; i < Sections.Count; i++)
        {
            foreach (StoryKind kind in Enum.GetValues<StoryKind>())
            {
                var id = new StoryId(i, kind);
                ImmutableList<Block>? blocks = TryGetStory(id);
                if (blocks is not null)
                {
                    yield return (id, blocks);
                }
            }
        }
    }
}
