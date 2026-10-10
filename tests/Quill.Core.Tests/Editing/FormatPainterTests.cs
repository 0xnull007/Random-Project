using Quill.Core.Editing;
using Quill.Core.Model;
using Quill.Core.Styles;
using Quill.Core.Tests.Support;
using Quill.Core.Units;
using Xunit;
using static Quill.Core.Tests.Support.TestDocuments;

namespace Quill.Core.Tests.Editing;

public class FormatPainterTests
{
    private static readonly RunProperties BoldRed = new() { Bold = true, Color = DocColor.Parse("FF0000") };

    [Fact]
    public void Caret_inside_a_word_formats_the_whole_word()
    {
        var session = new EditingSession(WithParagraphs("hello world"), new UndoStack(time: new FakeTime()));
        session.MoveCaret(Pos(0, 2), extend: false);
        session.ToggleBold();
        Paragraph paragraph = session.Document.Para(0);
        Assert.Equal("hello", ((Run)paragraph.Inlines[0]).Text);
        Assert.True(paragraph.Inlines[0].Properties.Bold);
        Assert.Null(paragraph.Inlines[1].Properties.Bold);
        Assert.Null(session.PendingFormat);
        Assert.Equal(Pos(0, 2), session.Selection.Active);

        // At the end of a word the format is sticky instead.
        session.MoveCaret(Pos(0, 11), extend: false);
        session.ToggleItalic();
        Assert.NotNull(session.PendingFormat);
    }

    [Fact]
    public void Clear_formatting_strips_direct_run_formatting_and_character_styles()
    {
        var paragraph = new Paragraph([new Run("plain "), new Run("fancy", BoldRed, "Strong")], DefaultStyleSheet.Heading1Id);
        var session = new EditingSession(WithBlocks(paragraph), new UndoStack(time: new FakeTime()));
        session.MoveCaret(Pos(0, 8), extend: false);
        session.ClearFormatting();
        Paragraph result = session.Document.Para(0);
        Assert.Single(result.Inlines);
        Assert.True(result.Inlines[0].Properties.IsEmpty);
        Assert.Null(result.Inlines[0].StyleId);
        Assert.Equal(DefaultStyleSheet.Heading1Id, result.StyleId);
    }

    [Fact]
    public void Painter_copies_character_and_paragraph_formatting_and_replaces_the_target()
    {
        var source = new Paragraph([new Run("Source", BoldRed)], DefaultStyleSheet.Heading1Id, new ParagraphProperties { Alignment = Alignment.Center });
        var target = new Paragraph([new Run("target ", new RunProperties { Italic = true }), new Run("text")]);
        var session = new EditingSession(WithBlocks(source, target), new UndoStack(time: new FakeTime()));

        session.MoveCaret(Pos(0, 3), extend: false);
        FormatSample sample = session.CopyFormat();
        Assert.True(sample.HasParagraphFormat);
        Assert.Equal(DefaultStyleSheet.Heading1Id, sample.ParagraphStyleId);

        session.MoveCaret(Pos(1, 0), extend: false);
        session.MoveCaret(Pos(1, 6), extend: true);
        session.PasteFormat(sample);

        Paragraph painted = session.Document.Para(1);
        Assert.Equal(DefaultStyleSheet.Heading1Id, painted.StyleId);
        Assert.Equal(Alignment.Center, painted.Properties.Alignment);
        Assert.True(painted.Inlines[0].Properties.Bold);
        Assert.Null(painted.Inlines[0].Properties.Italic);
        Assert.Equal("target", ((Run)painted.Inlines[0]).Text);
        Assert.Equal(Pos(1, 6), session.Selection.Active);

        session.Undo();
        Assert.Null(session.Document.Para(1).StyleId);
        Assert.True(session.Document.Para(1).Inlines[0].Properties.Italic);
    }

    [Fact]
    public void Painter_from_a_partial_selection_carries_only_character_formatting()
    {
        var source = new Paragraph([new Run("ab", BoldRed), new Run("cd")], DefaultStyleSheet.Heading1Id);
        var session = new EditingSession(WithBlocks(source, Paragraph.FromText("target")), new UndoStack(time: new FakeTime()));
        session.MoveCaret(Pos(0, 0), extend: false);
        session.MoveCaret(Pos(0, 1), extend: true);
        FormatSample sample = session.CopyFormat();
        Assert.False(sample.HasParagraphFormat);
        Assert.True(sample.Run.Bold);

        session.MoveCaret(Pos(1, 2), extend: false);
        session.PasteFormat(sample);
        Assert.Null(session.Document.Para(1).StyleId);
        Assert.True(session.Document.Para(1).Inlines[0].Properties.Bold);
        Assert.Equal("target", ((Run)session.Document.Para(1).Inlines[0]).Text);
    }
}
