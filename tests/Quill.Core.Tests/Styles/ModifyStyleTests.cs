using Quill.Core.Editing;
using Quill.Core.Model;
using Quill.Core.Styles;
using Quill.Core.Tests.Support;
using Quill.Core.Units;
using Xunit;
using static Quill.Core.Tests.Support.TestDocuments;

namespace Quill.Core.Tests.Styles;

public class ModifyStyleTests
{
    [Fact]
    public void Modifying_a_style_changes_every_paragraph_in_it_and_is_undoable()
    {
        var session = new EditingSession(WithParagraphs("Heading", "Body"), new UndoStack(time: new FakeTime()));
        session.SetParagraphStyle(DefaultStyleSheet.Heading1Id);
        Style heading = session.Document.Styles.Get(DefaultStyleSheet.Heading1Id)!;
        session.ModifyStyle(heading with { RunProperties = heading.RunProperties with { Bold = true, FontSize = HalfPoints.FromPoints(20) } });

        Assert.True(session.ResolveStyleRun(DefaultStyleSheet.Heading1Id).Bold);
        Assert.True(session.Resolver.ResolveRun(session.Document.Para(0), session.Document.Para(0).Inlines[0]).Bold);
        Assert.False(session.Resolver.ResolveRun(session.Document.Para(1), session.Document.Para(1).Inlines[0]).Bold);
        session.Undo();
        Assert.False(session.ResolveStyleRun(DefaultStyleSheet.Heading1Id).Bold);
    }

    [Fact]
    public void Update_to_match_selection_moves_direct_formatting_into_the_style()
    {
        var paragraph = new Paragraph([new Run("x", new RunProperties { Italic = true })], null, new ParagraphProperties { Alignment = Alignment.Center });
        var session = new EditingSession(WithBlocks(paragraph, Paragraph.FromText("y")), new UndoStack(time: new FakeTime()));
        session.MoveCaret(Pos(0, 1), extend: false);
        session.UpdateStyleFromSelection();

        Style normal = session.Document.Styles.Get(StyleSheet.NormalStyleId)!;
        Assert.True(normal.RunProperties.Italic);
        Assert.Equal(Alignment.Center, normal.ParagraphProperties.Alignment);
        Assert.True(session.Document.Para(0).Properties.IsEmpty);
        Assert.Equal(Alignment.Center, session.ResolveStyleParagraph(StyleSheet.NormalStyleId).Alignment);
        Assert.True(session.Resolver.ResolveRun(session.Document.Para(1), session.Document.Para(1).Inlines[0]).Italic);
    }
}
