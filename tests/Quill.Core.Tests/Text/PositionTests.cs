using Quill.Core.Text;
using Xunit;

namespace Quill.Core.Tests.Text;

public class PositionTests
{
    private static readonly StoryId Body = StoryId.Body(0);

    [Fact]
    public void BlockPath_has_structural_equality_and_ordering()
    {
        Assert.Equal(BlockPath.Of(3), BlockPath.Of(3));
        Assert.Equal(BlockPath.Of(3).GetHashCode(), BlockPath.Of(3).GetHashCode());
        Assert.True(BlockPath.Of(2) < BlockPath.Of(3));
        Assert.Equal(BlockPath.Of(4), BlockPath.Of(3).Next());
        Assert.Equal("[3]", BlockPath.Of(3).ToString());
    }

    [Fact]
    public void Positions_order_by_story_block_then_offset()
    {
        var a = new TextPosition(Body, BlockPath.Of(0), 5);
        var b = new TextPosition(Body, BlockPath.Of(1), 0);
        var c = new TextPosition(new StoryId(0, StoryKind.HeaderDefault), BlockPath.Of(0), 0);
        Assert.True(a < b);
        Assert.True(b < c);
        Assert.True(a <= new TextPosition(Body, BlockPath.Of(0), 5));
        Assert.Equal(a, a.WithOffset(5));
    }

    [Fact]
    public void Selection_normalizes_start_and_end_but_keeps_direction()
    {
        var anchor = new TextPosition(Body, BlockPath.Of(1), 2);
        var active = new TextPosition(Body, BlockPath.Of(0), 7);
        var selection = new Selection(anchor, active);
        Assert.False(selection.IsForward);
        Assert.Equal(active, selection.Start);
        Assert.Equal(anchor, selection.End);
        Assert.False(selection.IsCollapsed);
        Assert.Equal(Selection.Caret(active), selection.CollapseToStart());
    }

    [Fact]
    public void Ranges_and_selections_cannot_span_stories()
    {
        var body = new TextPosition(Body, BlockPath.Of(0), 0);
        var header = new TextPosition(new StoryId(0, StoryKind.HeaderDefault), BlockPath.Of(0), 0);
        Assert.Throws<ArgumentException>(() => new TextRange(body, header));
        Assert.Throws<ArgumentException>(() => new Selection(body, header));
    }

    [Fact]
    public void Range_contains_is_half_open()
    {
        var range = new TextRange(new TextPosition(Body, BlockPath.Of(0), 2), new TextPosition(Body, BlockPath.Of(0), 5));
        Assert.True(range.Contains(new TextPosition(Body, BlockPath.Of(0), 2)));
        Assert.True(range.Contains(new TextPosition(Body, BlockPath.Of(0), 4)));
        Assert.False(range.Contains(new TextPosition(Body, BlockPath.Of(0), 5)));
    }
}
