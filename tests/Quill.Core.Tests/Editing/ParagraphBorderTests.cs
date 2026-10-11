using Quill.Core.Editing;
using Quill.Core.Model;
using Quill.Core.Styles;
using Quill.Core.Units;
using Xunit;
using static Quill.Core.Tests.Support.TestDocuments;

namespace Quill.Core.Tests.Editing;

public class ParagraphBorderTests
{
    [Fact]
    public void Shading_and_borders_merge_and_resolve_with_explicit_none_values()
    {
        var styleSide = new ParagraphProperties { Borders = ParagraphBorders.Box(), Shading = DocColor.Parse("FFF2CC") };
        ParagraphProperties cleared = styleSide.Merge(new ParagraphProperties { Borders = ParagraphBorders.None, Shading = DocColor.Auto });
        Assert.True(cleared.Borders!.IsNone);
        Assert.True(cleared.Shading!.Value.IsAuto);

        var sheet = DefaultStyleSheet.Create();
        var resolver = new StyleResolver(sheet);
        ResolvedParagraphProperties shaded = resolver.ResolveParagraph(new Paragraph([new Run("x")], properties: styleSide));
        Assert.Equal(DocColor.Parse("FFF2CC"), shaded.Shading);
        Assert.NotNull(shaded.Borders!.Top);
        ResolvedParagraphProperties plain = resolver.ResolveParagraph(new Paragraph([new Run("x")], properties: cleared));
        Assert.Null(plain.Shading);
        Assert.Null(plain.Borders);
    }

    [Fact]
    public void Borders_button_toggles_sides_boxes_and_clears()
    {
        var session = new EditingSession(WithParagraphs("one", "two"));
        session.ToggleParagraphBorder("Bottom");
        ParagraphBorders? borders = session.Document.Para(0).Properties.Borders;
        Assert.NotNull(borders?.Bottom);
        Assert.Null(borders!.Top);
        Assert.Equal(4, borders.Bottom!.Width);
        Assert.Null(session.Document.Para(1).Properties.Borders);

        session.ToggleParagraphBorder("Bottom");
        Assert.True(session.Document.Para(0).Properties.Borders!.IsNone);
        Assert.Null(session.CaretParagraphFormat().Borders);

        session.ToggleParagraphBorder("Box");
        Assert.Equal(ParagraphBorders.Box(), session.Document.Para(0).Properties.Borders);
        session.ToggleParagraphBorder("None");
        Assert.True(session.Document.Para(0).Properties.Borders!.IsNone);

        session.SetParagraphShading(DocColor.Parse("D9E2F3"));
        Assert.Equal(DocColor.Parse("D9E2F3"), session.CaretParagraphFormat().Shading);
        session.SetParagraphShading(null);
        Assert.Null(session.CaretParagraphFormat().Shading);
        Assert.True(session.Document.Para(0).Properties.Shading!.Value.IsAuto);
        session.Undo();
        Assert.Equal(DocColor.Parse("D9E2F3"), session.CaretParagraphFormat().Shading);
    }
}
