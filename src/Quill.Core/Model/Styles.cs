using System.Collections.Immutable;

namespace Quill.Core.Model;

/// <summary>A named paragraph or character style, as in WordprocessingML <c>w:style</c>.</summary>
public sealed record Style
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    public StyleType Type { get; init; } = StyleType.Paragraph;

    /// <summary>Id of the parent style whose formatting this one refines.</summary>
    public string? BasedOn { get; init; }

    /// <summary>Id of the style applied to the paragraph created by pressing Enter at the end of a paragraph in this style.</summary>
    public string? Next { get; init; }

    public bool IsDefault { get; init; }

    /// <summary>Shown in the quick style gallery.</summary>
    public bool QuickFormat { get; init; }

    public int Priority { get; init; } = 99;

    public ParagraphProperties ParagraphProperties { get; init; } = ParagraphProperties.Empty;

    public RunProperties RunProperties { get; init; } = RunProperties.Empty;
}

/// <summary>Formatting applied when neither a style nor direct formatting specifies a value (<c>w:docDefaults</c>).</summary>
public sealed record DocumentDefaults
{
    public static readonly DocumentDefaults Empty = new();

    public RunProperties Run { get; init; } = RunProperties.Empty;

    public ParagraphProperties Paragraph { get; init; } = ParagraphProperties.Empty;
}

/// <summary>The styles of a document plus its document defaults. Immutable; identity is used as a cache key.</summary>
public sealed class StyleSheet
{
    public const string NormalStyleId = "Normal";
    public const string DefaultParagraphFontStyleId = "DefaultParagraphFont";

    public static readonly StyleSheet Empty = new(
        ImmutableDictionary<string, Style>.Empty,
        DocumentDefaults.Empty,
        NormalStyleId,
        DefaultParagraphFontStyleId);

    public StyleSheet(
        ImmutableDictionary<string, Style> styles,
        DocumentDefaults defaults,
        string defaultParagraphStyleId,
        string defaultCharacterStyleId)
    {
        ArgumentNullException.ThrowIfNull(styles);
        ArgumentNullException.ThrowIfNull(defaults);
        Styles = styles;
        Defaults = defaults;
        DefaultParagraphStyleId = defaultParagraphStyleId;
        DefaultCharacterStyleId = defaultCharacterStyleId;
    }

    public ImmutableDictionary<string, Style> Styles { get; }

    public DocumentDefaults Defaults { get; }

    public string DefaultParagraphStyleId { get; }

    public string DefaultCharacterStyleId { get; }

    public Style? Get(string? id) => id is not null && Styles.TryGetValue(id, out Style? style) ? style : null;

    public bool Contains(string id) => Styles.ContainsKey(id);

    public IEnumerable<Style> ParagraphStyles => Styles.Values.Where(s => s.Type == StyleType.Paragraph);

    public IEnumerable<Style> CharacterStyles => Styles.Values.Where(s => s.Type == StyleType.Character);

    /// <summary>Adds or replaces a style.</summary>
    public StyleSheet With(Style style)
    {
        ArgumentNullException.ThrowIfNull(style);
        return new StyleSheet(Styles.SetItem(style.Id, style), Defaults, DefaultParagraphStyleId, DefaultCharacterStyleId);
    }

    public StyleSheet WithDefaults(DocumentDefaults defaults) =>
        new(Styles, defaults, DefaultParagraphStyleId, DefaultCharacterStyleId);
}
