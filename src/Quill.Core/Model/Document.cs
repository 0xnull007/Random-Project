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
    public Document(ImmutableList<Section> sections, StyleSheet styles, DocumentSettings? settings = null, DocumentMetadata? metadata = null, ListStore? lists = null, ImageStore? images = null)
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

    public Document WithSections(ImmutableList<Section> sections) => new(sections, Styles, Settings, Metadata, Lists, Images);

    public Document WithLists(ListStore lists) => new(Sections, Styles, Settings, Metadata, lists, Images);

    public Document WithImages(ImageStore images) => new(Sections, Styles, Settings, Metadata, Lists, images);

    public Document WithSection(int index, Section section) => WithSections(Sections.SetItem(index, section));

    public Document WithStyles(StyleSheet styles) => new(Sections, styles, Settings, Metadata, Lists, Images);

    public Document WithSettings(DocumentSettings settings) => new(Sections, Styles, settings, Metadata, Lists, Images);

    public Document WithMetadata(DocumentMetadata metadata) => new(Sections, Styles, Settings, metadata, Lists, Images);

    /// <summary>The blocks of a story, or null when the section or header/footer variant does not exist.</summary>
    public ImmutableList<Block>? TryGetStory(StoryId story)
    {
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
        if (!path.IsTopLevel)
        {
            throw new NotSupportedException("Nested block paths are not supported yet.");
        }

        ImmutableList<Block>? blocks = TryGetStory(story);
        return blocks is not null && path.TopIndex >= 0 && path.TopIndex < blocks.Count ? blocks[path.TopIndex] : null;
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
