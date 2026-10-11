using Quill.Core.Units;

namespace Quill.Core.Model;

/// <summary>A text watermark drawn diagonally behind every page (Word's Design > Watermark), half transparent.</summary>
public sealed record Watermark(string Text, DocColor? Color = null)
{
    public const double Opacity = 0.5;

    public static readonly DocColor DefaultColor = DocColor.Parse("C0C0C0");

    public DocColor EffectiveColor => Color is { IsAuto: false } color ? color : DefaultColor;
}
