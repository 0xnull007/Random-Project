using System.Collections.Immutable;
using Quill.Core.Editing;
using Quill.Core.Model;
using Quill.Core.Styles;
using Quill.Core.Text;
using Xunit;
using static Quill.Core.Tests.Support.TestDocuments;

namespace Quill.Core.Tests.Editing;

public class TrackChangesTests
{
    private static readonly DateTime Clock = new(2026, 10, 11, 9, 30, 45);

    private static EditingSession Tracking(params string[] paragraphs)
    {
        var session = new EditingSession(WithParagraphs(paragraphs)) { Author = "Ada", Now = () => Clock };
        session.SetTrackChanges(true);
        return session;
    }

    private static IEnumerable<(string Text, RevisionKind? Kind, string? Author)> Runs(Document document, int index) =>
        document.Para(index).Inlines.Select(i => (((Run)i).Text, i.Properties.Revision?.Kind, i.Properties.Revision?.Author));

    [Fact]
    public void Typing_is_marked_inserted_with_author_and_minute_date_and_merges_into_one_run()
    {
        EditingSession session = Tracking("Hello world");
        session.MoveCaret(Pos(0, 5), extend: false);
        session.InsertText(" big");
        session.InsertText(" wide");

        Assert.Equal("Hello big wide world", session.Document.Text(0));
        Assert.Equal(
            [("Hello", null, null), (" big wide", RevisionKind.Insertion, "Ada"), (" world", null, null)],
            Runs(session.Document, 0));
        Assert.Equal(new DateTime(2026, 10, 11, 9, 30, 0), session.Document.Para(0).Inlines[1].Properties.Revision!.Date);
        Assert.True(session.TrackChanges);
        Assert.Equal(1, session.TrackedChangeCount);
    }

    [Fact]
    public void Typing_with_tracking_off_next_to_an_insertion_is_not_marked()
    {
        EditingSession session = Tracking("ab");
        session.MoveCaret(Pos(0, 2), extend: false);
        session.InsertText("X");
        session.SetTrackChanges(false);
        session.InsertText("Y");
        Assert.Equal([("ab", null, null), ("X", RevisionKind.Insertion, "Ada"), ("Y", null, null)], Runs(session.Document, 0));
    }

    [Fact]
    public void Deleting_marks_text_instead_of_removing_it_and_removes_own_insertions_outright()
    {
        EditingSession session = Tracking("Hello world");
        session.MoveCaret(Pos(0, 0), extend: false);
        session.MoveCaret(Pos(0, 5), extend: true);
        session.Delete();
        Assert.Equal("Hello world", session.Document.Text(0));
        Assert.Equal([("Hello", RevisionKind.Deletion, "Ada"), (" world", null, null)], Runs(session.Document, 0));
        Assert.Equal(Pos(0, 0), session.Selection.Active);

        session.MoveCaret(Pos(0, 11), extend: false);
        session.InsertText("!!");
        session.Backspace();
        Assert.Equal("Hello world!", session.Document.Text(0));
        Assert.Equal(Pos(0, 12), session.Selection.Active);

        // Backspace over someone else's deletion just steps over it.
        session.MoveCaret(Pos(0, 5), extend: false);
        session.Backspace();
        Assert.Equal("Hello world!", session.Document.Text(0));
        Assert.Equal(Pos(0, 4), session.Selection.Active);
        Assert.Equal(RevisionKind.Deletion, session.Document.Para(0).Inlines[0].Properties.Revision!.Kind);
    }

    [Fact]
    public void Typing_over_a_selection_leaves_the_old_text_struck_before_the_new()
    {
        EditingSession session = Tracking("red car");
        session.MoveCaret(Pos(0, 0), extend: false);
        session.MoveCaret(Pos(0, 3), extend: true);
        session.InsertText("blue");
        Assert.Equal("redblue car", session.Document.Text(0));
        Assert.Equal([("red", RevisionKind.Deletion, "Ada"), ("blue", RevisionKind.Insertion, "Ada"), (" car", null, null)], Runs(session.Document, 0));
        Assert.Equal(Pos(0, 7), session.Selection.Active);
    }

    [Fact]
    public void Joining_paragraphs_still_joins_and_cross_paragraph_deletions_keep_the_paragraphs()
    {
        EditingSession session = Tracking("One", "Two", "Three");
        session.MoveCaret(Pos(1, 0), extend: false);
        session.Backspace();
        Assert.Equal(["OneTwo", "Three"], session.Document.BodyTexts());

        session.MoveCaret(Pos(0, 3), extend: false);
        session.MoveCaret(Pos(1, 2), extend: true);
        session.Delete();
        Assert.Equal(["OneTwo", "Three"], session.Document.BodyTexts());
        Assert.Equal([("One", null, null), ("Two", RevisionKind.Deletion, "Ada")], Runs(session.Document, 0));
        Assert.Equal([("Th", RevisionKind.Deletion, "Ada"), ("ree", null, null)], Runs(session.Document, 1));
    }

    [Fact]
    public void Accept_all_and_reject_all_resolve_every_mark()
    {
        EditingSession session = Tracking("red car");
        session.MoveCaret(Pos(0, 0), extend: false);
        session.MoveCaret(Pos(0, 3), extend: true);
        session.InsertText("blue");

        EditingSession accepted = session;
        accepted.AcceptAllChanges();
        Assert.Equal("blue car", accepted.Document.Text(0));
        Assert.Equal([("blue car", null, null)], Runs(accepted.Document, 0));
        Assert.Equal(0, accepted.TrackedChangeCount);

        accepted.Undo();
        Assert.Equal("redblue car", accepted.Document.Text(0));
        accepted.RejectAllChanges();
        Assert.Equal("red car", accepted.Document.Text(0));
        Assert.Equal([("red car", null, null)], Runs(accepted.Document, 0));
    }

    [Fact]
    public void Accepting_at_the_caret_resolves_that_change_and_selects_the_next()
    {
        EditingSession session = Tracking("aaa bbb ccc");
        session.MoveCaret(Pos(0, 0), extend: false);
        session.MoveCaret(Pos(0, 3), extend: true);
        session.Delete();
        session.MoveCaret(Pos(0, 11), extend: false);
        session.InsertText(" ddd");
        Assert.Equal(2, session.TrackedChangeCount);

        session.MoveCaret(Pos(0, 1), extend: false);
        Assert.Equal(Range(0, 0, 0, 3), session.TrackedChangeAt(Pos(0, 1)));
        session.RejectChange();
        Assert.Equal("aaa bbb ccc ddd", session.Document.Text(0));
        Assert.Equal(1, session.TrackedChangeCount);
        Assert.Equal(Range(0, 11, 0, 15), session.Selection.Range);

        session.AcceptChange();
        Assert.Equal("aaa bbb ccc ddd", session.Document.Text(0));
        Assert.Equal(0, session.TrackedChangeCount);
    }

    [Fact]
    public void Next_and_previous_change_walk_the_document_and_wrap()
    {
        EditingSession session = Tracking("one", "two", "three");
        session.MoveCaret(Pos(0, 3), extend: false);
        session.InsertText("X");
        session.MoveCaret(Pos(2, 5), extend: false);
        session.InsertText("Y");
        session.MoveCaret(Pos(1, 0), extend: false);

        Assert.True(session.SelectNextChange());
        Assert.Equal(Range(2, 5, 2, 6), session.Selection.Range);
        Assert.True(session.SelectNextChange());
        Assert.Equal(Range(0, 3, 0, 4), session.Selection.Range);
        Assert.True(session.SelectPreviousChange());
        Assert.Equal(Range(2, 5, 2, 6), session.Selection.Range);

        var quiet = new EditingSession(WithParagraphs("plain"));
        Assert.False(quiet.SelectNextChange());
    }

    [Fact]
    public void Markup_shows_insertions_underlined_and_deletions_struck_in_the_author_colour()
    {
        EditingSession session = Tracking("red car");
        session.MoveCaret(Pos(0, 0), extend: false);
        session.MoveCaret(Pos(0, 3), extend: true);
        session.InsertText("blue");
        Paragraph paragraph = session.Document.Para(0);
        ResolvedRunProperties deleted = session.Resolver.ResolveRun(paragraph, paragraph.Inlines[0]);
        ResolvedRunProperties inserted = session.Resolver.ResolveRun(paragraph, paragraph.Inlines[1]);
        ResolvedRunProperties plain = session.Resolver.ResolveRun(paragraph, paragraph.Inlines[2]);

        Assert.True(deleted.Strikethrough);
        Assert.Equal(UnderlineStyle.Single, inserted.Underline);
        Assert.Equal(Revision.ColorFor("Ada"), inserted.Color);
        Assert.Equal(Revision.ColorFor("Ada"), deleted.Color);
        Assert.False(plain.Strikethrough);
        Assert.Equal(UnderlineStyle.None, plain.Underline);
        Assert.NotEqual(Revision.ColorFor("Ada"), Revision.ColorFor("Bob"));
    }

    [Fact]
    public void Pasting_while_tracking_marks_the_text_inserted_and_tracking_is_saved_in_the_settings()
    {
        EditingSession session = Tracking("");
        session.Paste(DocumentFragment.FromPlainText("pasted"));
        Assert.Equal([("pasted", RevisionKind.Insertion, "Ada")], Runs(session.Document, 0));
        Assert.True(session.Document.Settings.TrackChanges);
        session.SetTrackChanges(false);
        Assert.False(session.Document.Settings.TrackChanges);
    }
}
