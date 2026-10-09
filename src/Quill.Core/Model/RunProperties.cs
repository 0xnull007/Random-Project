using Quill.Core.Units;

namespace Quill.Core.Model;

/// <summary>
/// Sparse character formatting. Every member is nullable: <c>null</c> means "not specified here",
/// so the value is inherited from the style chain. See <see cref="Merge"/>.
/// </summary>
public sealed record RunProperties
{
    public static readonly RunProperties Empty = new();

    public string? FontFamily { get; init; }

    public HalfPoints? FontSize { get; init; }

    public bool? Bold { get; init; }

    public bool? Italic { get; init; }

    public UnderlineStyle? Underline { get; init; }

    public bool? Strikethrough { get; init; }

    public bool? DoubleStrikethrough { get; init; }

    public DocColor? Color { get; init; }

    public HighlightColor? Highlight { get; init; }

    public VerticalTextAlignment? VerticalAlignment { get; init; }

    public bool? SmallCaps { get; init; }

    public bool? AllCaps { get; init; }

    public bool? Hidden { get; init; }

    /// <summary>BCP-47 language tag used for line breaking and spell checking (e.g. "en-US").</summary>
    public string? Language { get; init; }

    public bool IsEmpty => Equals(Empty);

    /// <summary>Returns a copy where every value specified in <paramref name="overrides"/> replaces this one.</summary>
    public RunProperties Merge(RunProperties? overrides)
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
            FontFamily = overrides.FontFamily ?? FontFamily,
            FontSize = overrides.FontSize ?? FontSize,
            Bold = overrides.Bold ?? Bold,
            Italic = overrides.Italic ?? Italic,
            Underline = overrides.Underline ?? Underline,
            Strikethrough = overrides.Strikethrough ?? Strikethrough,
            DoubleStrikethrough = overrides.DoubleStrikethrough ?? DoubleStrikethrough,
            Color = overrides.Color ?? Color,
            Highlight = overrides.Highlight ?? Highlight,
            VerticalAlignment = overrides.VerticalAlignment ?? VerticalAlignment,
            SmallCaps = overrides.SmallCaps ?? SmallCaps,
            AllCaps = overrides.AllCaps ?? AllCaps,
            Hidden = overrides.Hidden ?? Hidden,
            Language = overrides.Language ?? Language,
        };
    }
}
