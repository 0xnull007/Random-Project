using Quill.Core.Editing;
using Quill.Core.Model;
using Quill.Core.Tests.Support;
using Quill.Core.Text;
using Xunit;
using static Quill.Core.Tests.Support.TestDocuments;

namespace Quill.Core.Tests.Editing;

public class UndoStackTests
{
    private static EditRecord Record(Document before, Document after, EditKind kind, long timestamp, bool newGroup = false) =>
        new(before, Selection.Caret(Pos(0, 0)), after, Selection.Caret(Pos(0, 1)), ChangeSet.From(Body, 0), kind, timestamp, newGroup);

    [Fact]
    public void Consecutive_typing_coalesces_into_one_step()
    {
        var time = new FakeTime();
        var stack = new UndoStack(time: time);
        Document d0 = WithParagraphs("");
        Document d1 = WithParagraphs("a");
        Document d2 = WithParagraphs("ab");
        stack.Push(Record(d0, d1, EditKind.Typing, time.GetTimestamp()));
        time.Advance(TimeSpan.FromMilliseconds(200));
        stack.Push(Record(d1, d2, EditKind.Typing, time.GetTimestamp()));
        Assert.Equal(1, stack.UndoCount);
        EditRecord? undone = stack.Undo();
        Assert.Same(d0, undone!.Before);
        Assert.Same(d2, undone.After);
    }

    [Fact]
    public void Typing_after_a_pause_starts_a_new_step()
    {
        var time = new FakeTime();
        var stack = new UndoStack(time: time);
        Document d0 = WithParagraphs("");
        Document d1 = WithParagraphs("a");
        Document d2 = WithParagraphs("ab");
        stack.Push(Record(d0, d1, EditKind.Typing, time.GetTimestamp()));
        time.Advance(TimeSpan.FromSeconds(3));
        stack.Push(Record(d1, d2, EditKind.Typing, time.GetTimestamp()));
        Assert.Equal(2, stack.UndoCount);
    }

    [Fact]
    public void New_group_flag_kind_change_and_caret_moves_break_coalescing()
    {
        var time = new FakeTime();
        var stack = new UndoStack(time: time);
        Document d0 = WithParagraphs("");
        Document d1 = WithParagraphs("a");
        Document d2 = WithParagraphs("ab");
        Document d3 = WithParagraphs("a");
        Document d4 = WithParagraphs("ab");
        stack.Push(Record(d0, d1, EditKind.Typing, 0));
        stack.Push(Record(d1, d2, EditKind.Typing, 0, newGroup: true));
        Assert.Equal(2, stack.UndoCount);
        stack.Push(Record(d2, d3, EditKind.Backspace, 0));
        Assert.Equal(3, stack.UndoCount);
        stack.BreakCoalescing();
        stack.Push(Record(d3, d4, EditKind.Backspace, 0));
        Assert.Equal(4, stack.UndoCount);
    }

    [Fact]
    public void Non_consecutive_documents_never_coalesce()
    {
        var stack = new UndoStack(time: new FakeTime());
        Document d0 = WithParagraphs("");
        Document d1 = WithParagraphs("a");
        Document other = WithParagraphs("zzz");
        stack.Push(Record(d0, d1, EditKind.Typing, 0));
        stack.Push(Record(other, d1, EditKind.Typing, 0));
        Assert.Equal(2, stack.UndoCount);
    }

    [Fact]
    public void Redo_is_cleared_by_a_new_edit_and_capacity_is_bounded()
    {
        var stack = new UndoStack(capacity: 2, time: new FakeTime());
        Document d0 = WithParagraphs("0");
        Document d1 = WithParagraphs("1");
        Document d2 = WithParagraphs("2");
        Document d3 = WithParagraphs("3");
        stack.Push(Record(d0, d1, EditKind.Other, 0));
        stack.Push(Record(d1, d2, EditKind.Other, 0));
        stack.Push(Record(d2, d3, EditKind.Other, 0));
        Assert.Equal(2, stack.UndoCount);
        Assert.NotNull(stack.Undo());
        Assert.True(stack.CanRedo);
        stack.Push(Record(d2, d3, EditKind.Other, 0));
        Assert.False(stack.CanRedo);
        Assert.NotNull(stack.Undo());
        Assert.NotNull(stack.Redo());
        Assert.Null(stack.Redo());
    }
}
