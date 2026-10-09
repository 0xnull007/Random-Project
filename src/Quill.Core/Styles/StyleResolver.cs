using System.Collections.Concurrent;
using Quill.Core.Model;
using Quill.Core.Units;

namespace Quill.Core.Styles;

/// <summary>
/// Resolves effective formatting using WordprocessingML precedence:
/// hard defaults → document defaults → paragraph style chain → character style chain → direct formatting.
/// One resolver per <see cref="StyleSheet"/>; results are cached and safe to share across threads.
/// </summary>
public sealed class StyleResolver
{
    /// <summary>What Word assumes when a document specifies nothing at all.</summary>
    public static readonly RunProperties HardRunDefaults = new()
    {
        FontFamily = "Times New Roman",
        FontSize = HalfPoints.FromPoints(10),
        Bold = false,
        Italic = false,
        Underline = UnderlineStyle.None,
        Strikethrough = false,
        DoubleStrikethrough = false,
        Color = DocColor.Auto,
        Highlight = HighlightColor.None,
        VerticalAlignment = VerticalTextAlignment.Baseline,
        SmallCaps = false,
        AllCaps = false,
        Hidden = false,
        Language = "en-US",
    };

    public static readonly ParagraphProperties HardParagraphDefaults = new()
    {
        Alignment = Alignment.Left,
        LeftIndent = Twips.Zero,
        RightIndent = Twips.Zero,
        FirstLineIndent = Twips.Zero,
        SpaceBefore = Twips.Zero,
        SpaceAfter = Twips.Zero,
        LineSpacing = LineSpacing.Single,
        KeepWithNext = false,
        KeepLinesTogether = false,
        PageBreakBefore = false,
        WidowControl = true,
        ContextualSpacing = false,
        Tabs = TabStops.Empty,
        OutlineLevel = null,
    };

    private readonly ConcurrentDictionary<string, RunProperties> _runChains = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, ParagraphProperties> _paragraphChains = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<RunKey, ResolvedRunProperties> _runCache = new();
    private readonly ConcurrentDictionary<ParagraphKey, ResolvedParagraphProperties> _paragraphCache = new();
    private readonly RunProperties _runBase;
    private readonly ParagraphProperties _paragraphBase;

    public StyleResolver(StyleSheet sheet, ListStore? lists = null)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        Sheet = sheet;
        Lists = lists ?? ListStore.Empty;
        _runBase = HardRunDefaults.Merge(sheet.Defaults.Run);
        _paragraphBase = HardParagraphDefaults.Merge(sheet.Defaults.Paragraph);
    }

    public StyleSheet Sheet { get; }

    public ListStore Lists { get; }

    public ResolvedParagraphProperties ResolveParagraph(Paragraph paragraph)
    {
        ArgumentNullException.ThrowIfNull(paragraph);
        return ResolveParagraph(paragraph.StyleId, paragraph.Properties);
    }

    public ResolvedParagraphProperties ResolveParagraph(string? paragraphStyleId, ParagraphProperties direct)
    {
        ArgumentNullException.ThrowIfNull(direct);
        var key = new ParagraphKey(EffectiveParagraphStyle(paragraphStyleId), direct);
        return _paragraphCache.GetOrAdd(key, static (k, self) => self.BuildParagraph(k), this);
    }

    /// <summary>Formatting of the given inline inside its paragraph.</summary>
    public ResolvedRunProperties ResolveRun(Paragraph paragraph, Inline inline)
    {
        ArgumentNullException.ThrowIfNull(paragraph);
        ArgumentNullException.ThrowIfNull(inline);
        return ResolveRun(paragraph.StyleId, inline.StyleId, inline.Properties);
    }

    /// <summary>Formatting of the paragraph mark: governs empty-paragraph height and text typed into an empty paragraph.</summary>
    public ResolvedRunProperties ResolveParagraphMark(Paragraph paragraph)
    {
        ArgumentNullException.ThrowIfNull(paragraph);
        return ResolveRun(paragraph.StyleId, null, paragraph.MarkProperties);
    }

    public ResolvedRunProperties ResolveRun(string? paragraphStyleId, string? characterStyleId, RunProperties direct)
    {
        ArgumentNullException.ThrowIfNull(direct);
        var key = new RunKey(EffectiveParagraphStyle(paragraphStyleId), EffectiveCharacterStyle(characterStyleId), direct);
        return _runCache.GetOrAdd(key, static (k, self) => self.BuildRun(k), this);
    }

    /// <summary>The style to apply to a paragraph created after one in <paramref name="paragraphStyleId"/>.</summary>
    public string? NextStyleAfter(string? paragraphStyleId)
    {
        Style? style = Sheet.Get(EffectiveParagraphStyle(paragraphStyleId));
        string? next = style?.Next;
        return next is not null && Sheet.Contains(next) ? next : paragraphStyleId;
    }

    private string EffectiveParagraphStyle(string? id) =>
        id is not null && Sheet.Contains(id) ? id : Sheet.DefaultParagraphStyleId;

    private string? EffectiveCharacterStyle(string? id) => id is not null && Sheet.Contains(id) ? id : null;

    private ResolvedParagraphProperties BuildParagraph(ParagraphKey key)
    {
        ParagraphProperties chain = ParagraphChain(key.ParagraphStyleId);
        ParagraphProperties merged = _paragraphBase.Merge(chain);

        // Word applies the list level's indents between the style and direct formatting.
        ListFormat? list = key.Direct.List ?? chain.List;
        if (list is { IsNone: false } format && Lists.GetLevel(format.NumberingId, format.Level) is { } level)
        {
            merged = merged.Merge(new ParagraphProperties { LeftIndent = level.LeftIndent, FirstLineIndent = -level.Hanging });
        }

        merged = merged.Merge(key.Direct);
        return new ResolvedParagraphProperties(
            merged.Alignment!.Value,
            merged.LeftIndent!.Value,
            merged.RightIndent!.Value,
            merged.FirstLineIndent!.Value,
            merged.SpaceBefore!.Value,
            merged.SpaceAfter!.Value,
            merged.LineSpacing!.Value,
            merged.KeepWithNext!.Value,
            merged.KeepLinesTogether!.Value,
            merged.PageBreakBefore!.Value,
            merged.WidowControl!.Value,
            merged.ContextualSpacing!.Value,
            (merged.Tabs ?? TabStops.Empty).WithoutCleared(),
            merged.OutlineLevel,
            list is { IsNone: false } ? list : null);
    }

    private ResolvedRunProperties BuildRun(RunKey key)
    {
        RunProperties merged = _runBase.Merge(RunChain(key.ParagraphStyleId));
        if (key.CharacterStyleId is not null)
        {
            merged = merged.Merge(RunChain(key.CharacterStyleId));
        }

        merged = merged.Merge(key.Direct);
        return new ResolvedRunProperties(
            merged.FontFamily!,
            merged.FontSize!.Value,
            merged.Bold!.Value,
            merged.Italic!.Value,
            merged.Underline!.Value,
            merged.Strikethrough!.Value,
            merged.DoubleStrikethrough!.Value,
            merged.Color!.Value,
            merged.Highlight!.Value,
            merged.VerticalAlignment!.Value,
            merged.SmallCaps!.Value,
            merged.AllCaps!.Value,
            merged.Hidden!.Value,
            merged.Language!);
    }

    /// <summary>Run properties accumulated from the root of the BasedOn chain down to <paramref name="styleId"/>.</summary>
    private RunProperties RunChain(string styleId) =>
        _runChains.GetOrAdd(styleId, static (id, self) => self.Chain(id).Aggregate(RunProperties.Empty, (acc, s) => acc.Merge(s.RunProperties)), this);

    private ParagraphProperties ParagraphChain(string styleId) =>
        _paragraphChains.GetOrAdd(styleId, static (id, self) => self.Chain(id).Aggregate(ParagraphProperties.Empty, (acc, s) => acc.Merge(s.ParagraphProperties)), this);

    /// <summary>Styles from the root ancestor down to <paramref name="styleId"/>, cycle-safe.</summary>
    private List<Style> Chain(string styleId)
    {
        var chain = new List<Style>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        string? current = styleId;
        while (current is not null && seen.Add(current) && Sheet.Get(current) is { } style)
        {
            chain.Add(style);
            current = style.BasedOn;
        }

        chain.Reverse();
        return chain;
    }

    private readonly record struct RunKey(string ParagraphStyleId, string? CharacterStyleId, RunProperties Direct);

    private readonly record struct ParagraphKey(string ParagraphStyleId, ParagraphProperties Direct);
}
