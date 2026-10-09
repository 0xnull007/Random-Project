using Quill.Core.Model;
using Quill.Core.Tests.Support;
using Quill.Core.Text;
using Xunit;
using static Quill.Core.Tests.Support.TestDocuments;

namespace Quill.Core.Tests.Text;

public class NavigationTests
{
    [Fact]
    public void Graphemes_step_over_emoji_sequences_and_combining_marks()
    {
        const string text = "a\U0001F468‍\U0001F469‍\U0001F467éb"; // a, family emoji (ZWJ), e + acute, b
        Assert.Equal(1, Graphemes.Next(text, 0));
        Assert.Equal(9, Graphemes.Next(text, 1));
        Assert.Equal(11, Graphemes.Next(text, 9));
        Assert.Equal(9, Graphemes.Previous(text, 11));
        Assert.Equal(1, Graphemes.Previous(text, 9));
        Assert.Equal(1, Graphemes.SnapToBoundary(text, 4));
        Assert.Equal(text.Length, Graphemes.Next(text, text.Length));
        Assert.Equal(0, Graphemes.Previous(text, 0));
    }

    [Fact]
    public void Next_and_previous_character_cross_paragraphs()
    {
        Document doc = WithParagraphs("ab", "cd");
        Assert.Equal(Pos(0, 1), TextNavigation.NextCharacter(doc, Pos(0, 0)));
        Assert.Equal(Pos(1, 0), TextNavigation.NextCharacter(doc, Pos(0, 2)));
        Assert.Equal(Pos(0, 2), TextNavigation.PreviousCharacter(doc, Pos(1, 0)));
        Assert.Null(TextNavigation.NextCharacter(doc, Pos(1, 2)));
        Assert.Null(TextNavigation.PreviousCharacter(doc, Pos(0, 0)));
    }

    [Theory]
    [InlineData("hello world", 0, 6)]
    [InlineData("hello world", 2, 6)]
    [InlineData("hello world", 6, 11)]
    [InlineData("hello, world", 0, 5)]
    [InlineData("hello, world", 5, 7)]
    [InlineData("hello   ", 0, 8)]
    public void Ctrl_right_moves_to_the_next_word_start(string text, int from, int expected)
    {
        Assert.Equal(expected, Words.NextWordStart(text, from));
    }

    [Theory]
    [InlineData("hello world", 11, 6)]
    [InlineData("hello world", 6, 0)]
    [InlineData("hello world", 8, 6)]
    [InlineData("hello, world", 7, 5)]
    [InlineData("hello   ", 8, 0)]
    public void Ctrl_left_moves_to_the_previous_word_start(string text, int from, int expected)
    {
        Assert.Equal(expected, Words.PreviousWordStart(text, from));
    }

    [Theory]
    [InlineData("hello world", 1, 0, 6)]
    [InlineData("hello world", 5, 0, 6)]
    [InlineData("hello world", 8, 6, 11)]
    [InlineData("hello world", 11, 6, 11)]
    [InlineData("a, b", 1, 1, 3)]
    [InlineData("", 0, 0, 0)]
    public void Double_click_selects_word_plus_trailing_spaces(string text, int at, int start, int end)
    {
        Assert.Equal((start, end), Words.WordSpanAt(text, at));
    }

    [Fact]
    public void Word_navigation_crosses_paragraph_boundaries()
    {
        Document doc = WithParagraphs("one two", "three");
        Assert.Equal(Pos(0, 4), TextNavigation.NextWord(doc, Pos(0, 0)));
        Assert.Equal(Pos(0, 7), TextNavigation.NextWord(doc, Pos(0, 4)));
        Assert.Equal(Pos(1, 0), TextNavigation.NextWord(doc, Pos(0, 7)));
        Assert.Equal(Pos(0, 7), TextNavigation.PreviousWord(doc, Pos(1, 0)));
    }

    [Fact]
    public void Paragraph_selection_includes_the_mark()
    {
        Document doc = WithParagraphs("ab", "cd");
        Assert.Equal(Range(0, 0, 1, 0), TextNavigation.ParagraphAt(doc, Pos(0, 1)));
        Assert.Equal(Range(1, 0, 1, 2), TextNavigation.ParagraphAt(doc, Pos(1, 1)));
    }

    [Fact]
    public void Story_bounds_skip_opaque_blocks()
    {
        Document doc = WithBlocks(new OpaqueBlock("tbl", "<w:tbl/>"), Paragraph.FromText("x"), new OpaqueBlock("tbl", "<w:tbl/>"));
        Assert.Equal(Pos(1, 0), TextNavigation.StoryStart(doc, Body));
        Assert.Equal(Pos(1, 1), TextNavigation.StoryEnd(doc, Body));
        Assert.Null(TextNavigation.NextCharacter(doc, Pos(1, 1)));
    }
}
