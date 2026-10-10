using Quill.Core.Editing;
using Quill.Core.Model;
using Quill.Core.Tests.Support;
using Quill.Core.Text;
using Quill.Core.Units;
using Xunit;
using static Quill.Core.Tests.Support.TestDocuments;

namespace Quill.Core.Tests.Text;

public class TextRangesTests
{
    [Fact]
    public void Whole_text_selection_formats_the_paragraph_marks_too()
    {
        var session = new EditingSession(WithParagraphs("Tea", "Sugar"), new UndoStack(time: new FakeTime()));
        session.ToggleList(bulleted: true);
        session.MoveCaret(Pos(0, 0), extend: false);
        session.MoveCaret(Pos(1, 5), extend: true);
        session.ApplyRunFormat(new RunProperties { FontSize = HalfPoints.FromPoints(28) });

        Assert.Equal(HalfPoints.FromPoints(28), session.Document.Para(0).MarkProperties.FontSize);
        Assert.Equal(HalfPoints.FromPoints(28), session.Document.Para(1).MarkProperties.FontSize);
        Assert.All(session.SelectionFormats(), f => Assert.Equal(HalfPoints.FromPoints(28), f.FontSize));
    }

    [Fact]
    public void Partial_selection_leaves_the_last_mark_alone()
    {
        var session = new EditingSession(WithParagraphs("Tea", "Sugar"), new UndoStack(time: new FakeTime()));
        session.MoveCaret(Pos(0, 0), extend: false);
        session.MoveCaret(Pos(1, 3), extend: true);
        session.ApplyRunFormat(new RunProperties { Bold = true });
        Assert.True(session.Document.Para(0).MarkProperties.Bold);
        Assert.Null(session.Document.Para(1).MarkProperties.Bold);
    }

    [Fact]
    public void Triple_click_range_does_not_touch_the_next_paragraph()
    {
        var session = new EditingSession(WithParagraphs("Topics:", "Body text"), new UndoStack(time: new FakeTime()));
        session.SelectParagraphAt(Pos(0, 2));
        Assert.Equal(Range(0, 0, 1, 0), session.Selection.Range);

        session.ToggleBold();
        session.ApplyParagraphFormat(new ParagraphProperties { Alignment = Alignment.Center });
        session.ToggleList(bulleted: true);

        Paragraph first = session.Document.Para(0);
        Paragraph second = session.Document.Para(1);
        Assert.True(first.Inlines[0].Properties.Bold);
        Assert.True(first.MarkProperties.Bold);
        Assert.Equal(Alignment.Center, first.Properties.Alignment);
        Assert.NotNull(first.Properties.List);
        Assert.Null(second.Inlines[0].Properties.Bold);
        Assert.Null(second.Properties.Alignment);
        Assert.Null(second.Properties.List);
        Assert.All(session.SelectionFormats(), f => Assert.True(f.Bold));

        session.ToggleBold();
        Assert.False(session.Document.Para(0).Inlines[0].Properties.Bold);
    }

    [Fact]
    public void Spans_describe_offsets_and_marks()
    {
        Document document = WithParagraphs("ab", "cd", "ef");
        List<ParagraphSpan> spans = TextRanges.Paragraphs(document, Range(0, 1, 2, 1)).ToList();
        Assert.Equal([0, 1, 2], spans.Select(s => s.Index));
        Assert.Equal((1, 2, true), (spans[0].Start, spans[0].End, spans[0].IncludesMark));
        Assert.Equal((0, 2, true), (spans[1].Start, spans[1].End, spans[1].IncludesMark));
        Assert.Equal((0, 1, false), (spans[2].Start, spans[2].End, spans[2].IncludesMark));

        spans = TextRanges.Paragraphs(document, Range(0, 0, 1, 0)).ToList();
        Assert.Single(spans);
        Assert.True(spans[0].IncludesMark);
        Assert.Equal(0, TextRanges.LastBlockIndex(Range(0, 0, 1, 0)));
    }
}
