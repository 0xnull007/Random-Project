using Quill.Core.Editing;
using Quill.Core.Model;
using Quill.Core.Styles;
using Quill.Core.Tests.Support;
using Quill.Core.Text;
using Xunit;
using static Quill.Core.Tests.Support.TestDocuments;

namespace Quill.Core.Tests.Editing;

public class EditingSessionTests
{
    private static EditingSession NewSession(params string[] paragraphs) =>
        new(WithParagraphs(paragraphs), new UndoStack(time: new FakeTime()));

    [Fact]
    public void Typing_then_undo_and_redo()
    {
        EditingSession session = NewSession("");
        int changes = 0;
        session.DocumentChanged += (_, _) => changes++;
        session.InsertText("h");
        session.InsertText("i");
        Assert.Equal(["hi"], session.Document.BodyTexts());
        Assert.Equal(Pos(0, 2), session.Selection.Active);
        Assert.True(session.IsDirty);
        Assert.Equal(2, changes);

        Assert.True(session.Undo());
        Assert.Equal([""], session.Document.BodyTexts());
        Assert.Equal(Pos(0, 0), session.Selection.Active);
        Assert.False(session.IsDirty);

        Assert.True(session.Redo());
        Assert.Equal(["hi"], session.Document.BodyTexts());
        Assert.Equal(Pos(0, 2), session.Selection.Active);
        Assert.False(session.Redo());
    }

    [Fact]
    public void Undo_by_word_groups_typing_at_word_starts()
    {
        EditingSession session = NewSession("");
        foreach (char c in "one two")
        {
            session.InsertText(c.ToString());
        }

        session.Undo();
        Assert.Equal(["one "], session.Document.BodyTexts());
        session.Undo();
        Assert.Equal([""], session.Document.BodyTexts());
    }

    [Fact]
    public void Pending_bold_applies_to_the_next_typed_text_only()
    {
        EditingSession session = NewSession("ab");
        session.MoveCaret(Pos(0, 2), extend: false);
        session.ToggleBold();
        Assert.NotNull(session.PendingFormat);
        Assert.True(session.CaretFormat().Bold);
        session.InsertText("c");
        session.InsertText("d");
        Paragraph paragraph = session.Document.Para(0);
        Assert.Equal(["ab", "cd"], paragraph.Inlines.Cast<Run>().Select(r => r.Text).ToArray());
        Assert.True(paragraph.Inlines[1].Properties.Bold);
        Assert.Null(session.PendingFormat);
    }

    [Fact]
    public void Moving_the_caret_discards_pending_format()
    {
        EditingSession session = NewSession("ab");
        session.MoveCaret(Pos(0, 2), extend: false);
        session.ToggleBold();
        session.MoveCaret(Pos(0, 1), extend: false);
        Assert.Null(session.PendingFormat);
    }

    [Fact]
    public void Toggle_bold_on_a_selection_is_a_real_toggle()
    {
        EditingSession session = NewSession("abcd");
        session.SetSelection(new Selection(Pos(0, 1), Pos(0, 3)));
        session.ToggleBold();
        Assert.True(session.Document.Para(0).Inlines[1].Properties.Bold);
        Assert.Equal(new Selection(Pos(0, 1), Pos(0, 3)), session.Selection);
        Assert.All(session.SelectionFormats(), f => Assert.True(f.Bold));
        session.ToggleBold();
        Assert.All(session.SelectionFormats(), f => Assert.False(f.Bold));
    }

    [Fact]
    public void Bold_in_an_empty_paragraph_formats_the_mark()
    {
        EditingSession session = NewSession("");
        session.ToggleBold();
        Assert.True(session.Document.Para(0).MarkProperties.Bold);
        Assert.Null(session.PendingFormat);
        session.InsertText("x");
        Assert.True(session.Document.Para(0).Inlines[0].Properties.Bold);
    }

    [Fact]
    public void Typing_over_a_selection_replaces_it_with_the_first_selected_chars_format()
    {
        EditingSession session = new(WithBlocks(new Paragraph([new Run("ab", new RunProperties { Italic = true }), new Run("cd")])));
        session.SetSelection(new Selection(Pos(0, 1), Pos(0, 3)));
        session.InsertText("X");
        Paragraph paragraph = session.Document.Para(0);
        Assert.Equal("aXd", paragraph.FlatText);
        Assert.Equal("aX", ((Run)paragraph.Inlines[0]).Text);
        Assert.True(paragraph.Inlines[0].Properties.Italic);
    }

    [Fact]
    public void Backspace_and_delete_join_paragraphs_and_stop_at_story_bounds()
    {
        EditingSession session = NewSession("ab", "cd");
        session.MoveCaret(Pos(1, 0), extend: false);
        session.Backspace();
        Assert.Equal(["abcd"], session.Document.BodyTexts());
        Assert.Equal(Pos(0, 2), session.Selection.Active);
        session.MoveCaret(Pos(0, 0), extend: false);
        session.Backspace();
        Assert.Equal(["abcd"], session.Document.BodyTexts());
        session.MoveCaret(Pos(0, 4), extend: false);
        session.Delete();
        Assert.Equal(["abcd"], session.Document.BodyTexts());
        session.MoveCaret(Pos(0, 1), extend: false);
        session.Delete();
        Assert.Equal(["acd"], session.Document.BodyTexts());
    }

    [Fact]
    public void Enter_splits_and_select_all_plus_paste_replaces_everything()
    {
        EditingSession session = NewSession("hello world");
        session.MoveCaret(Pos(0, 5), extend: false);
        session.InsertParagraphBreak();
        Assert.Equal(["hello", " world"], session.Document.BodyTexts());
        Assert.Equal(Pos(1, 0), session.Selection.Active);

        session.SelectAll();
        Assert.Equal(Pos(0, 0), session.Selection.Start);
        Assert.Equal(Pos(1, 6), session.Selection.End);
        session.Paste(DocumentFragment.FromPlainText("x\ny\nz"));
        Assert.Equal(["x", "y", "z"], session.Document.BodyTexts());
        Assert.Equal(Pos(2, 1), session.Selection.Active);

        session.Undo();
        Assert.Equal(["hello", " world"], session.Document.BodyTexts());
    }

    [Fact]
    public void Copy_and_cut()
    {
        EditingSession session = NewSession("abc", "def");
        session.SetSelection(new Selection(Pos(0, 1), Pos(1, 1)));
        Assert.Equal("bc\nd", session.Copy().ToPlainText());
        DocumentFragment cut = session.Cut();
        Assert.Equal("bc\nd", cut.ToPlainText());
        Assert.Equal(["aef"], session.Document.BodyTexts());
    }

    [Fact]
    public void Line_breaks_and_word_deletion()
    {
        EditingSession session = NewSession("one two");
        session.MoveCaret(Pos(0, 3), extend: false);
        session.InsertBreak(BreakKind.Line);
        Assert.Equal("one￼ two", session.Document.Text(0));
        session.MoveCaret(Pos(0, 8), extend: false);
        session.DeleteWordBackward();
        Assert.Equal("one￼ ", session.Document.Text(0));
    }

    [Fact]
    public void Paragraph_style_and_format_apply_at_the_caret()
    {
        EditingSession session = NewSession("title");
        session.SetParagraphStyle(DefaultStyleSheet.Heading1Id);
        Assert.Equal(DefaultStyleSheet.Heading1Id, session.Document.Para(0).StyleId);
        Assert.True(session.CaretParagraphFormat().KeepWithNext);
        session.ApplyParagraphFormat(new ParagraphProperties { Alignment = Alignment.Center });
        Assert.Equal(Alignment.Center, session.CaretParagraphFormat().Alignment);
        session.Undo();
        Assert.Equal(Alignment.Left, session.CaretParagraphFormat().Alignment);
    }

    [Fact]
    public void Headers_can_be_created_and_edited_as_separate_stories()
    {
        EditingSession session = NewSession("body");
        var header = new StoryId(0, StoryKind.HeaderDefault);
        session.EnsureStory(header);
        session.MoveCaret(TextPosition.StartOf(header), extend: false);
        session.InsertText("Page header");
        Assert.Equal("Page header", ((Paragraph)session.Document.GetStory(header)[0]).FlatText);
        Assert.Equal(DefaultStyleSheet.HeaderId, ((Paragraph)session.Document.GetStory(header)[0]).StyleId);
        Assert.Equal(["body"], session.Document.BodyTexts());
    }

    [Fact]
    public void Section_breaks_and_page_setup_through_the_session()
    {
        EditingSession session = NewSession("one", "two");
        session.MoveCaret(Pos(1, 0), extend: false);
        session.InsertSectionBreak(SectionStart.NextPage);
        Assert.Equal(2, session.Document.Sections.Count);
        Assert.Equal(StoryId.Body(1), session.Selection.Story);
        session.SetSectionProperties(1, SectionProperties.A4);
        Assert.Equal(SectionProperties.A4, session.Document.Sections[1].Properties);
        session.Undo();
        Assert.Equal(SectionProperties.Letter, session.Document.Sections[1].Properties);
        session.Undo();
        Assert.Single(session.Document.Sections);
    }

    [Fact]
    public void Invalid_selections_are_rejected()
    {
        EditingSession session = NewSession("ab");
        Assert.Throws<ArgumentException>(() => session.SetSelection(Selection.Caret(Pos(0, 3))));
        Assert.Throws<ArgumentException>(() => session.SetSelection(Selection.Caret(Pos(1, 0))));
    }

    [Fact]
    public void Load_document_resets_history_and_dirty_state()
    {
        EditingSession session = NewSession("ab");
        session.InsertText("x");
        session.LoadDocument(WithParagraphs("fresh"));
        Assert.False(session.IsDirty);
        Assert.False(session.CanUndo);
        Assert.Equal(Pos(0, 0), session.Selection.Active);
    }
}
