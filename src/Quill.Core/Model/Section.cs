using System.Collections.Immutable;
using Quill.Core.Units;

namespace Quill.Core.Model;

/// <summary>
/// Page geometry and numbering for a section. <see cref="PageWidth"/>/<see cref="PageHeight"/> are stored
/// as laid out (a landscape page has width &gt; height) and <see cref="Orientation"/> is informational,
/// exactly like WordprocessingML's <c>w:pgSz</c>.
/// </summary>
public sealed record SectionProperties
{
    public static readonly SectionProperties Letter = new();

    public static readonly SectionProperties A4 = new()
    {
        PageWidth = Twips.FromCentimeters(21.0),
        PageHeight = Twips.FromCentimeters(29.7),
        MarginTop = Twips.FromCentimeters(2.54),
        MarginBottom = Twips.FromCentimeters(2.54),
        MarginLeft = Twips.FromCentimeters(2.54),
        MarginRight = Twips.FromCentimeters(2.54),
    };

    public Twips PageWidth { get; init; } = Twips.FromInches(8.5);

    public Twips PageHeight { get; init; } = Twips.FromInches(11);

    public Orientation Orientation { get; init; } = Orientation.Portrait;

    public Twips MarginTop { get; init; } = Twips.FromInches(1);

    public Twips MarginBottom { get; init; } = Twips.FromInches(1);

    public Twips MarginLeft { get; init; } = Twips.FromInches(1);

    public Twips MarginRight { get; init; } = Twips.FromInches(1);

    /// <summary>Distance from the top edge of the page to the header.</summary>
    public Twips HeaderDistance { get; init; } = Twips.FromInches(0.5);

    /// <summary>Distance from the bottom edge of the page to the footer.</summary>
    public Twips FooterDistance { get; init; } = Twips.FromInches(0.5);

    public Twips Gutter { get; init; } = Twips.Zero;

    public SectionStart Start { get; init; } = SectionStart.NextPage;

    /// <summary>Use a different header/footer on the first page of the section.</summary>
    public bool TitlePage { get; init; }

    /// <summary>Restart page numbering at this value; null continues from the previous section.</summary>
    public int? PageNumberStart { get; init; }

    public PageNumberFormat PageNumberFormat { get; init; } = PageNumberFormat.Decimal;

    public int ColumnCount { get; init; } = 1;

    /// <summary>Width available to body text.</summary>
    public Twips BodyWidth => PageWidth - MarginLeft - MarginRight - Gutter;

    /// <summary>Height available to body text when headers and footers fit inside the margins.</summary>
    public Twips BodyHeight => PageHeight - MarginTop - MarginBottom;

    /// <summary>Swaps width and height and flips <see cref="Orientation"/>.</summary>
    public SectionProperties WithOrientation(Orientation orientation)
    {
        if (orientation == Orientation)
        {
            return this;
        }

        return this with { Orientation = orientation, PageWidth = PageHeight, PageHeight = PageWidth };
    }
}

/// <summary>
/// The header (or footer) stories of a section. A null variant means "linked to previous section":
/// layout walks back to the nearest section that defines it.
/// </summary>
public sealed class HeaderFooterSet
{
    public static readonly HeaderFooterSet Empty = new(null, null, null);

    public HeaderFooterSet(ImmutableList<Block>? @default, ImmutableList<Block>? first, ImmutableList<Block>? even)
    {
        Default = @default;
        First = first;
        Even = even;
    }

    public ImmutableList<Block>? Default { get; }

    public ImmutableList<Block>? First { get; }

    public ImmutableList<Block>? Even { get; }

    public bool IsEmpty => Default is null && First is null && Even is null;

    public ImmutableList<Block>? Get(HeaderFooterVariant variant) => variant switch
    {
        HeaderFooterVariant.Default => Default,
        HeaderFooterVariant.First => First,
        HeaderFooterVariant.Even => Even,
        _ => throw new ArgumentOutOfRangeException(nameof(variant)),
    };

    public HeaderFooterSet With(HeaderFooterVariant variant, ImmutableList<Block>? blocks) => variant switch
    {
        HeaderFooterVariant.Default => new HeaderFooterSet(blocks, First, Even),
        HeaderFooterVariant.First => new HeaderFooterSet(Default, blocks, Even),
        HeaderFooterVariant.Even => new HeaderFooterSet(Default, First, blocks),
        _ => throw new ArgumentOutOfRangeException(nameof(variant)),
    };
}

/// <summary>A section: page setup plus the body story and header/footer stories.</summary>
public sealed class Section
{
    public Section(SectionProperties properties, ImmutableList<Block> body, HeaderFooterSet? headers = null, HeaderFooterSet? footers = null)
    {
        ArgumentNullException.ThrowIfNull(properties);
        ArgumentNullException.ThrowIfNull(body);
        Properties = properties;
        Body = body;
        Headers = headers ?? HeaderFooterSet.Empty;
        Footers = footers ?? HeaderFooterSet.Empty;
    }

    public static Section CreateEmpty(SectionProperties properties) =>
        new(properties, ImmutableList.Create<Block>(Paragraph.Empty()));

    public SectionProperties Properties { get; }

    public ImmutableList<Block> Body { get; }

    public HeaderFooterSet Headers { get; }

    public HeaderFooterSet Footers { get; }

    public Section WithProperties(SectionProperties properties) => new(properties, Body, Headers, Footers);

    public Section WithBody(ImmutableList<Block> body) => new(Properties, body, Headers, Footers);

    public Section WithHeaders(HeaderFooterSet headers) => new(Properties, Body, headers, Footers);

    public Section WithFooters(HeaderFooterSet footers) => new(Properties, Body, Headers, footers);
}
