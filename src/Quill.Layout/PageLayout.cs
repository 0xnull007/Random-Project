using System.Collections.Immutable;
using Quill.Core.Model;
using Quill.Core.Units;
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
    public double LineTop(int lineIndex) => ContentTop + Layout.LineTops[lineIndex] - Layout.StartOfLines(FirstLine);

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
    public StoryLayout(StoryId story, RectD bounds, ImmutableArray<BlockFragment> fragments, ImmutableArray<FloatingImage> floats = default)
    {
        Story = story;
        Bounds = bounds;
        Fragments = fragments;
        Floats = floats.IsDefault ? ImmutableArray<FloatingImage>.Empty : floats;
    }

    public StoryId Story { get; }

    public RectD Bounds { get; }

    public ImmutableArray<BlockFragment> Fragments { get; }

    /// <summary>Floating pictures anchored in this story, in page coordinates.</summary>
    public ImmutableArray<FloatingImage> Floats { get; }

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
        HeaderFooterVariant variant = HeaderFooterVariant.Default,
        ImmutableArray<StoryLayout> footnotes = default,
        double? footnoteSeparatorY = null,
        double? endnoteSeparatorY = null,
        PageBorders? borders = null,
        DocColor? pageColor = null,
        Watermark? watermark = null,
        ImmutableArray<LineNumber> lineNumbers = default,
        double lineNumberX = 0,
        ImmutableArray<FloatingImage> floats = default)
    {
        Floats = floats.IsDefault ? ImmutableArray<FloatingImage>.Empty : floats;
        Borders = borders;
        PageColor = pageColor;
        Watermark = watermark;
        LineNumbers = lineNumbers.IsDefault ? ImmutableArray<LineNumber>.Empty : lineNumbers;
        LineNumberX = lineNumberX;
        EndnoteSeparatorY = endnoteSeparatorY;
        Footnotes = footnotes.IsDefault ? ImmutableArray<StoryLayout>.Empty : footnotes;
        FootnoteSeparatorY = footnoteSeparatorY;
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

    /// <summary>Every floating picture on the page (body, header and footer), in page coordinates, in document order.</summary>
    public ImmutableArray<FloatingImage> Floats { get; }

    /// <summary>The topmost floating picture under a page point, or null; pictures drawn in front win over those behind the text.</summary>
    public FloatingImage? FloatAt(PointD pagePoint)
    {
        FloatingImage? hit = null;
        foreach (FloatingImage image in Floats)
        {
            if (image.Bounds.Contains(pagePoint) && (hit is null || hit.Anchor.Wrap == ImageWrap.BehindText || image.Anchor.Wrap != ImageWrap.BehindText))
            {
                hit = image;
            }
        }

        return hit;
    }

    /// <summary>The section's page border, or null.</summary>
    public PageBorders? Borders { get; }

    /// <summary>The document's page colour, or null for white.</summary>
    public DocColor? PageColor { get; }

    /// <summary>The document's watermark, or null.</summary>
    public Watermark? Watermark { get; }

    /// <summary>Line numbers to draw beside body lines (empty when the section has none).</summary>
    public ImmutableArray<LineNumber> LineNumbers { get; }

    /// <summary>Right edge of the line numbers (page x).</summary>
    public double LineNumberX { get; }

    /// <summary>Where the page border is drawn: inside the page edge or around the text area, by the border's distance.</summary>
    public RectD? BorderRect()
    {
        if (Borders is not { } borders)
        {
            return null;
        }

        double space = borders.SpaceDips;
        return borders.FromText
            ? RectD.FromEdges(BodyArea.Left - space, Math.Min(BodyArea.Top, Header?.Bounds.Top ?? BodyArea.Top) - space, BodyArea.Right + space, Math.Max(BodyArea.Bottom, Footer?.Bounds.Bottom ?? BodyArea.Bottom) + space)
            : RectD.FromEdges(space, space, Size.Width - space, Size.Height - space);
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

    /// <summary>Footnotes shown at the bottom of this page's body, in order.</summary>
    public ImmutableArray<StoryLayout> Footnotes { get; }

    /// <summary>y of the short rule above the footnotes, or null when the page has none.</summary>
    public double? FootnoteSeparatorY { get; }

    /// <summary>y of the rule above the endnotes, when they begin on this page.</summary>
    public double? EndnoteSeparatorY { get; }


    public IEnumerable<BlockFragment> AllFragments()
    {
        foreach (BlockFragment fragment in AllFragmentsCore())
        {
            yield return fragment;
            if (fragment is TableFragment table)
            {
                foreach (ParagraphFragment cellParagraph in table.AllParagraphs())
                {
                    yield return cellParagraph;
                }
            }
        }
    }

    private IEnumerable<BlockFragment> AllFragmentsCore()
    {
        foreach (StoryLayout note in Footnotes)
        {
            foreach (BlockFragment fragment in note.Fragments)
            {
                yield return fragment;
            }
        }

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

/// <summary>One line number on a page: the text baseline and the number to show.</summary>
public readonly record struct LineNumber(double Baseline, int Number, double FontSize);
