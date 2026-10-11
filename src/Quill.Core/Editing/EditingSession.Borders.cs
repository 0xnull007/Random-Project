using Quill.Core.Model;
using Quill.Core.Units;

namespace Quill.Core.Editing;

/// <summary>Home > Paragraph > Borders and Shading.</summary>
public sealed partial class EditingSession
{
    /// <summary>Shades the selected paragraphs (null clears the shading); one undo step.</summary>
    public void SetParagraphShading(DocColor? color) =>
        ApplyParagraphFormat(new ParagraphProperties { Shading = color is { IsAuto: false } ? color : DocColor.Auto });

    /// <summary>
    /// Word's Borders button: "Top", "Bottom", "Left" or "Right" toggles that side of the caret paragraph's border,
    /// "Box" draws all four, "None" removes them; the selected paragraphs get the result.
    /// </summary>
    public void ToggleParagraphBorder(string side)
    {
        ArgumentNullException.ThrowIfNull(side);
        ParagraphBorders current = CaretParagraphFormat().Borders ?? ParagraphBorders.None;
        ParagraphBorders updated = side switch
        {
            "Box" => ParagraphBorders.Box(),
            "None" => ParagraphBorders.None,
            _ => current.With(side, current.Get(side) is null ? BorderLine.Default : null),
        };
        ApplyParagraphFormat(new ParagraphProperties { Borders = updated });
    }
}
