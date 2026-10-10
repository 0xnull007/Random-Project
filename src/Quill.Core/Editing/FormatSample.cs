using Quill.Core.Model;

namespace Quill.Core.Editing;

/// <summary>
/// What the Format Painter carries: character formatting always, and paragraph formatting when the source was a
/// caret or a selection that covered a whole paragraph (Word's rule).
/// </summary>
public sealed record FormatSample(RunProperties Run, string? CharacterStyleId, ParagraphProperties? Paragraph, string? ParagraphStyleId)
{
    public bool HasParagraphFormat => Paragraph is not null;
}
