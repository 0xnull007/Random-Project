using System.Collections.Immutable;
using Quill.Core.Model;
using Quill.Core.Text;

namespace Quill.Layout;

/// <summary>A block (or part of one) placed on a page. Coordinates are page-relative DIPs.</summary>
public abstract class BlockFragment
{
    protected BlockFragment(StoryId story, BlockPath path, RectD bounds)
    {
        Story = story;
        Path = path;
        Bounds = bounds;
    }

    public StoryId Story { get; }

    public BlockPath Path { get; }

    public RectD Bounds { get; }
}

/// <summary>Lines [FirstLine, LastLine] of a paragraph placed on a page.</summary>
public sealed class ParagraphFragment : BlockFragment
{
    public ParagraphFragment(StoryId story, BlockPath path, ParagraphLayout layout, int firstLine, int lastLine, double top, double left, double spaceBeforeUsed, double spaceAfterUsed)
        : base(story, path, new RectD(left, top, layout.Width, spaceBeforeUsed + layout.HeightOfLines(firstLine, lastLine) + spaceAfterUsed))
    {
        Layout = layout;
        FirstLine = firstLine;
        LastLine = lastLine;
        ContentTop = top + spaceBeforeUsed;
        SpaceBeforeUsed = spaceBeforeUsed;
        SpaceAfterUsed = spaceAfterUsed;
    }

    public ParagraphLayout Layout { get; }

    public Paragraph Paragraph => Layout.Paragraph;

    public int FirstLine { get; }

    public int LastLine { get; }

    public int LineCount => LastLine - FirstLine + 1;

    /// <summary>Page y of the first line's slot.</summary>
    public double ContentTop { get; }

    public double SpaceBeforeUsed { get; }

    public double SpaceAfterUsed { get; }

    public bool IsParagraphStart => FirstLine == 0;

    public bool IsParagraphEnd => LastLine == Layout.LineCount - 1;

    /// <summary>Page y of the top of line <paramref name="lineIndex"/>'s slot.</summary>
    public double LineTop(int lineIndex) => ContentTop + Layout.LineTops[lineIndex] - Layout.LineTops[FirstLine];

    public double LineBottom(int lineIndex) => LineTop(lineIndex) + Layout.LineHeights[lineIndex];

    /// <summary>Page origin (top-left of the natural line box) to pass to <see cref="IFormattedLine.Draw"/>.</summary>
    public PointD LineOrigin(int lineIndex)
    {
        double naturalTop = LineTop(lineIndex) + (Layout.LineBaselines[lineIndex] - Layout.Lines[lineIndex].Baseline);
        return new PointD(Bounds.Left + Layout.LineStarts[lineIndex], naturalTop);
    }

    public bool ContainsOffset(int offset)
    {
        IFormattedLine first = Layout.Lines[FirstLine];
        IFormattedLine last = Layout.Lines[LastLine];
        return offset >= first.Start && (offset < last.End || (IsParagraphEnd && offset == last.End));
    }
}

/// <summary>A placeholder for a block we cannot lay out yet (tables, drawings).</summary>
public sealed class OpaqueFragment : BlockFragment
{
    public const double PlaceholderHeight = 48;

    public OpaqueFragment(StoryId story, BlockPath path, OpaqueBlock block, RectD bounds)
        : base(story, path, bounds)
    {
        Block = block;
    }

    public OpaqueBlock Block { get; }

    public string Label => Block.LocalName switch
    {
        "tbl" => "Table (not editable in this version)",
        _ => $"Unsupported content: {Block.LocalName}",
    };
}

/// <summary>A laid-out header or footer story on one page.</summary>
public sealed class StoryLayout
{
    public StoryLayout(StoryId story, RectD bounds, ImmutableArray<BlockFragment> fragments)
    {
        Story = story;
        Bounds = bounds;
        Fragments = fragments;
    }

    public StoryId Story { get; }

    public RectD Bounds { get; }

    public ImmutableArray<BlockFragment> Fragments { get; }

    public double Height => Fragments.IsEmpty ? 0 : Fragments[^1].Bounds.Bottom - Bounds.Top;
}

public sealed class PageLayout
{
    public PageLayout(
        int index,
        int sectionIndex,
        int pageNumber,
        string pageNumberText,
        SizeD size,
        RectD bodyArea,
        ImmutableArray<BlockFragment> body,
        StoryLayout? header,
        StoryLayout? footer,
        bool isBlankFiller,
        HeaderFooterVariant variant = HeaderFooterVariant.Default)
    {
        Index = index;
        SectionIndex = sectionIndex;
        PageNumber = pageNumber;
        PageNumberText = pageNumberText;
        Size = size;
        BodyArea = bodyArea;
        Body = body;
        Header = header;
        Footer = footer;
        IsBlankFiller = isBlankFiller;
        Variant = variant;
    }

    /// <summary>Which header/footer variant this page shows (first page, even page, or default).</summary>
    public HeaderFooterVariant Variant { get; }

    /// <summary>
    /// The story to edit when the user opens this page's header: the one actually shown (possibly linked from an
    /// earlier section), or the section's own variant story if none exists yet.
    /// </summary>
    public StoryId EditableHeaderStory() => Header?.Story ?? new StoryId(SectionIndex, HeaderKind(Variant));

    public StoryId EditableFooterStory() => Footer?.Story ?? new StoryId(SectionIndex, FooterKind(Variant));

    public static StoryKind HeaderKind(HeaderFooterVariant variant) => variant switch
    {
        HeaderFooterVariant.First => StoryKind.HeaderFirst,
        HeaderFooterVariant.Even => StoryKind.HeaderEven,
        _ => StoryKind.HeaderDefault,
    };

    public static StoryKind FooterKind(HeaderFooterVariant variant) => variant switch
    {
        HeaderFooterVariant.First => StoryKind.FooterFirst,
        HeaderFooterVariant.Even => StoryKind.FooterEven,
        _ => StoryKind.FooterDefault,
    };

    /// <summary>0-based physical page index.</summary>
    public int Index { get; }

    public int SectionIndex { get; }

    /// <summary>Displayed page number (honors section restarts).</summary>
    public int PageNumber { get; }

    public string PageNumberText { get; }

    public SizeD Size { get; }

    public RectD Bounds => new(0, 0, Size.Width, Size.Height);

    public RectD BodyArea { get; }

    public ImmutableArray<BlockFragment> Body { get; }

    public StoryLayout? Header { get; }

    public StoryLayout? Footer { get; }

    /// <summary>A page inserted to satisfy an odd/even section start; it carries header and footer only.</summary>
    public bool IsBlankFiller { get; }

    public IEnumerable<BlockFragment> AllFragments()
    {
        if (Header is not null)
        {
            foreach (BlockFragment fragment in Header.Fragments)
            {
                yield return fragment;
            }
        }

        foreach (BlockFragment fragment in Body)
        {
            yield return fragment;
        }

        if (Footer is not null)
        {
            foreach (BlockFragment fragment in Footer.Fragments)
            {
                yield return fragment;
            }
        }
    }
}

/// <summary>The result of paginating a document.</summary>
public sealed class LayoutDocument
{
    public static readonly LayoutDocument Empty = new(ImmutableArray<PageLayout>.Empty, Core.Model.Document.CreateNew());

    public LayoutDocument(ImmutableArray<PageLayout> pages, Core.Model.Document document)
    {
        Pages = pages;
        Document = document;
    }

    public ImmutableArray<PageLayout> Pages { get; }

    public Core.Model.Document Document { get; }

    public int PageCount => Pages.Length;

    /// <summary>Finds the page and fragment containing a position, searching page by page.</summary>
    public (PageLayout Page, ParagraphFragment Fragment)? Find(TextPosition position)
    {
        foreach (PageLayout page in Pages)
        {
            foreach (BlockFragment fragment in page.AllFragments())
            {
                if (fragment is ParagraphFragment paragraph && paragraph.Story == position.Story && paragraph.Path == position.Block && paragraph.ContainsOffset(position.Offset))
                {
                    return (page, paragraph);
                }
            }
        }

        return null;
    }
}
