using Quill.Core.Units;

namespace Quill.Core.Model;

/// <summary>
/// Sparse paragraph formatting. Every member is nullable: <c>null</c> means "not specified here",
/// so the value is inherited from the style chain. See <see cref="Merge"/>.
/// </summary>
public sealed record ParagraphProperties
{
    public static readonly ParagraphProperties Empty = new();

    public Alignment? Alignment { get; init; }

    public Twips? LeftIndent { get; init; }

    public Twips? RightIndent { get; init; }

    /// <summary>Positive = first-line indent, negative = hanging indent (relative to <see cref="LeftIndent"/>).</summary>
    public Twips? FirstLineIndent { get; init; }

    public Twips? SpaceBefore { get; init; }

    public Twips? SpaceAfter { get; init; }

    public LineSpacing? LineSpacing { get; init; }

    public bool? KeepWithNext { get; init; }

    public bool? KeepLinesTogether { get; init; }

    public bool? PageBreakBefore { get; init; }

    public bool? WidowControl { get; init; }

    /// <summary>Suppress space before/after between paragraphs of the same style.</summary>
    public bool? ContextualSpacing { get; init; }

    public TabStops? Tabs { get; init; }

    /// <summary>Outline level 0-8 for headings; null = body text.</summary>
    public int? OutlineLevel { get; init; }

    public bool IsEmpty => Equals(Empty);

    /// <summary>Returns a copy where every value specified in <paramref name="overrides"/> replaces this one.</summary>
    public ParagraphProperties Merge(ParagraphProperties? overrides)
    {
        if (overrides is null || overrides.IsEmpty)
        {
            return this;
        }

        if (IsEmpty)
        {
            return overrides;
        }

        return this with
        {
            Alignment = overrides.Alignment ?? Alignment,
            LeftIndent = overrides.LeftIndent ?? LeftIndent,
            RightIndent = overrides.RightIndent ?? RightIndent,
            FirstLineIndent = overrides.FirstLineIndent ?? FirstLineIndent,
            SpaceBefore = overrides.SpaceBefore ?? SpaceBefore,
            SpaceAfter = overrides.SpaceAfter ?? SpaceAfter,
            LineSpacing = overrides.LineSpacing ?? LineSpacing,
            KeepWithNext = overrides.KeepWithNext ?? KeepWithNext,
            KeepLinesTogether = overrides.KeepLinesTogether ?? KeepLinesTogether,
            PageBreakBefore = overrides.PageBreakBefore ?? PageBreakBefore,
            WidowControl = overrides.WidowControl ?? WidowControl,
            ContextualSpacing = overrides.ContextualSpacing ?? ContextualSpacing,
            Tabs = Tabs is null ? overrides.Tabs : Tabs.Merge(overrides.Tabs),
            OutlineLevel = overrides.OutlineLevel ?? OutlineLevel,
        };
    }
}
