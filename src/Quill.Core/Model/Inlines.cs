namespace Quill.Core.Model;

/// <summary>
/// A piece of paragraph content. Every inline contributes <see cref="Length"/> UTF-16 code units to the
/// paragraph's flat text; non-text inlines contribute exactly one object replacement character.
/// </summary>
public abstract class Inline
{
    protected Inline(string? styleId, RunProperties? properties)
    {
        StyleId = styleId;
        Properties = properties ?? RunProperties.Empty;
    }

    /// <summary>Character style id, or null.</summary>
    public string? StyleId { get; }

    /// <summary>Direct character formatting.</summary>
    public RunProperties Properties { get; }

    public abstract int Length { get; }

    public abstract Inline WithProperties(RunProperties properties);

    public abstract Inline WithStyle(string? styleId);

    internal abstract void AppendFlatText(System.Text.StringBuilder builder);
}

/// <summary>A run of text with uniform formatting. May contain '\t'; never contains line or paragraph separators.</summary>
public sealed class Run : Inline
{
    public Run(string text, RunProperties? properties = null, string? styleId = null)
        : base(styleId, properties)
    {
        ArgumentNullException.ThrowIfNull(text);
        Text = text;
    }

    public string Text { get; }

    public override int Length => Text.Length;

    public Run WithText(string text) => new(text, Properties, StyleId);

    public override Run WithProperties(RunProperties properties) => new(Text, properties, StyleId);

    public override Run WithStyle(string? styleId) => new(Text, Properties, styleId);

    /// <summary>True when this run can be merged with <paramref name="other"/> without changing formatting.</summary>
    public bool HasSameFormatting(Run other) => StyleId == other.StyleId && Properties == other.Properties;

    internal override void AppendFlatText(System.Text.StringBuilder builder) => builder.Append(Text);

    public override string ToString() => $"Run(\"{Text}\")";
}

/// <summary>A line, page or column break. Occupies one character.</summary>
public sealed class Break : Inline
{
    public Break(BreakKind kind, RunProperties? properties = null, string? styleId = null)
        : base(styleId, properties)
    {
        Kind = kind;
    }

    public BreakKind Kind { get; }

    public override int Length => 1;

    public override Break WithProperties(RunProperties properties) => new(Kind, properties, StyleId);

    public override Break WithStyle(string? styleId) => new(Kind, Properties, styleId);

    internal override void AppendFlatText(System.Text.StringBuilder builder) => builder.Append(Paragraph.ObjectReplacementChar);

    public override string ToString() => $"Break({Kind})";
}

/// <summary>A field such as PAGE or NUMPAGES. Occupies one character; layout substitutes the result text.</summary>
public sealed class Field : Inline
{
    public Field(FieldKind kind, string instruction, string cachedResult, RunProperties? properties = null, string? styleId = null)
        : base(styleId, properties)
    {
        ArgumentNullException.ThrowIfNull(instruction);
        ArgumentNullException.ThrowIfNull(cachedResult);
        Kind = kind;
        Instruction = instruction;
        CachedResult = cachedResult;
    }

    public static Field Page(RunProperties? properties = null) => new(FieldKind.Page, "PAGE", "1", properties);

    public static Field NumPages(RunProperties? properties = null) => new(FieldKind.NumPages, "NUMPAGES", "1", properties);

    public FieldKind Kind { get; }

    /// <summary>The field instruction as stored in the document, e.g. "PAGE \* MERGEFORMAT".</summary>
    public string Instruction { get; }

    /// <summary>Last known result; used for unknown fields and as the initial display text.</summary>
    public string CachedResult { get; }

    public override int Length => 1;

    public Field WithCachedResult(string result) => new(Kind, Instruction, result, Properties, StyleId);

    public override Field WithProperties(RunProperties properties) => new(Kind, Instruction, CachedResult, properties, StyleId);

    public override Field WithStyle(string? styleId) => new(Kind, Instruction, CachedResult, Properties, styleId);

    internal override void AppendFlatText(System.Text.StringBuilder builder) => builder.Append(Paragraph.ObjectReplacementChar);

    public override string ToString() => $"Field({Instruction})";
}

/// <summary>An inline picture, displayed at <see cref="Width"/> by <see cref="Height"/>. Occupies one character; the bytes live in the document's <see cref="ImageStore"/>.</summary>
public sealed class InlineImage : Inline
{
    public InlineImage(string imageId, Units.Twips width, Units.Twips height, RunProperties? properties = null, string? styleId = null)
        : base(styleId, properties)
    {
        ArgumentNullException.ThrowIfNull(imageId);
        ImageId = imageId;
        Width = width;
        Height = height;
    }

    public string ImageId { get; }

    public Units.Twips Width { get; }

    public Units.Twips Height { get; }

    public override int Length => 1;

    public InlineImage WithSize(Units.Twips width, Units.Twips height) => new(ImageId, width, height, Properties, StyleId);

    public override InlineImage WithProperties(RunProperties properties) => new(ImageId, Width, Height, properties, StyleId);

    public override InlineImage WithStyle(string? styleId) => new(ImageId, Width, Height, Properties, styleId);

    internal override void AppendFlatText(System.Text.StringBuilder builder) => builder.Append(Paragraph.ObjectReplacementChar);

    public override string ToString() => $"Image({ImageId} {Width}x{Height})";
}
