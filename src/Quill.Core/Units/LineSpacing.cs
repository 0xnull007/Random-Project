namespace Quill.Core.Units;

public enum LineSpacingRule
{
    /// <summary>Value is in 240ths of a single line (240 = single, 276 = 1.15, 480 = double).</summary>
    Auto,

    /// <summary>Value is an exact line height in twips.</summary>
    Exact,

    /// <summary>Value is a minimum line height in twips.</summary>
    AtLeast,
}

/// <summary>Paragraph line spacing, matching the WordprocessingML <c>w:spacing/@w:line</c> + <c>@w:lineRule</c> pair.</summary>
public readonly record struct LineSpacing(LineSpacingRule Rule, int Value)
{
    public const int AutoUnitsPerLine = 240;

    public static readonly LineSpacing Single = new(LineSpacingRule.Auto, AutoUnitsPerLine);
    public static readonly LineSpacing OneAndHalf = new(LineSpacingRule.Auto, 360);
    public static readonly LineSpacing Double = new(LineSpacingRule.Auto, 480);

    public static LineSpacing Multiple(double factor) => new(LineSpacingRule.Auto, (int)Math.Round(factor * AutoUnitsPerLine));

    public static LineSpacing Exactly(Twips height) => new(LineSpacingRule.Exact, height.Value);

    public static LineSpacing AtLeast(Twips height) => new(LineSpacingRule.AtLeast, height.Value);

    /// <summary>The multiplier for <see cref="LineSpacingRule.Auto"/>; NaN otherwise.</summary>
    public double Factor => Rule == LineSpacingRule.Auto ? Value / (double)AutoUnitsPerLine : double.NaN;

    /// <summary>The height in twips for <see cref="LineSpacingRule.Exact"/> and <see cref="LineSpacingRule.AtLeast"/>.</summary>
    public Twips Height => new(Value);
}
