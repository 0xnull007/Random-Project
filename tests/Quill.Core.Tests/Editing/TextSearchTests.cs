using System.Collections.Immutable;
using Quill.Core.Editing;
using Quill.Core.Model;
using Quill.Core.Tests.Support;
using Quill.Core.Text;
using Xunit;
using static Quill.Core.Tests.Support.TestDocuments;

namespace Quill.Core.Tests.Editing;

public class TextSearchTests
{
    private static readonly StoryId Header = new(0, StoryKind.HeaderDefault);

    private static Document WithHeader(Document document, params string[] headerParagraphs) =>
        document.WithStory(Header, headerParagraphs.Select(t => (Block)Paragraph.FromText(t)).ToImmutableList());

    [Fact]
    public void FindAll_is_case_insensitive_by_default_and_ordered()
    {
        Document doc = WithParagraphs("The cat sat.", "Cat, CAT, concatenate");
        IReadOnlyList<TextRange> matches = TextSearch.FindAll(doc, "cat");
        Assert.Equal(4, matches.Count);
        Assert.Equal(Range(0, 4, 0, 7), matches[0]);
        Assert.Equal(Range(1, 0, 1, 3), matches[1]);
        Assert.Equal(Range(1, 5, 1, 8), matches[2]);
        Assert.Equal(Range(1, 13, 1, 16), matches[3]);
    }

    [Fact]
    public void Match_case_and_whole_word_filter_results()
    {
        // An apostrophe joins a word, so "cat" is not a whole word inside "cat's" (as in Word); punctuation like "," ends one.
        Document doc = WithParagraphs("Cat cat, concatenate cat's scat");
        Assert.Single(TextSearch.FindAll(doc, "cat", new SearchOptions(MatchCase: true, WholeWord: true)));
        Assert.Equal([0, 4], TextSearch.FindAll(doc, "cat", new SearchOptions(WholeWord: true)).Select(r => r.Start.Offset));
        Assert.Single(TextSearch.FindAll(doc, "Cat", new SearchOptions(MatchCase: true)));
        Assert.Equal(5, TextSearch.FindAll(doc, "cat").Count);
    }

    [Fact]
    public void Matches_never_cross_paragraphs_and_empty_queries_find_nothing()
    {
        Document doc = WithParagraphs("ab", "cd");
        Assert.Empty(TextSearch.FindAll(doc, "bc"));
        Assert.Empty(TextSearch.FindAll(doc, string.Empty));
    }

    [Fact]
    public void Bodies_are_searched_before_headers()
    {
        Document doc = WithHeader(WithParagraphs("body hit"), "header hit");
        IReadOnlyList<TextRange> matches = TextSearch.FindAll(doc, "hit");
        Assert.Equal(2, matches.Count);
        Assert.True(matches[0].Story.IsBody);
        Assert.Equal(Header, matches[1].Story);
    }

    [Fact]
    public void FindNext_moves_forward_and_wraps()
    {
        Document doc = WithParagraphs("x one x", "two x");
        TextRange? first = TextSearch.FindNext(doc, Pos(0, 0), "x", null, backwards: false, out bool wrapped);
        Assert.Equal(Range(0, 0, 0, 1), first);
        Assert.False(wrapped);

        TextRange? second = TextSearch.FindNext(doc, first!.Value.End, "x", null, backwards: false, out wrapped);
        Assert.Equal(Range(0, 6, 0, 7), second);

        TextRange? third = TextSearch.FindNext(doc, second!.Value.End, "x", null, backwards: false, out wrapped);
        Assert.Equal(Range(1, 4, 1, 5), third);
        Assert.False(wrapped);

        TextRange? again = TextSearch.FindNext(doc, third!.Value.End, "x", null, backwards: false, out wrapped);
        Assert.Equal(first, again);
        Assert.True(wrapped);
    }

    [Fact]
    public void FindNext_backwards_wraps_to_the_last_match()
    {
        Document doc = WithParagraphs("x one x", "two x");
        TextRange? previous = TextSearch.FindNext(doc, Pos(0, 6), "x", null, backwards: true, out bool wrapped);
        Assert.Equal(Range(0, 0, 0, 1), previous);
        Assert.False(wrapped);

        TextRange? wrappedMatch = TextSearch.FindNext(doc, Pos(0, 0), "x", null, backwards: true, out wrapped);
        Assert.Equal(Range(1, 4, 1, 5), wrappedMatch);
        Assert.True(wrapped);
    }

    [Fact]
    public void FindNext_crosses_from_body_into_headers_and_back()
    {
        Document doc = WithHeader(WithParagraphs("body hit"), "header hit");
        TextRange? inHeader = TextSearch.FindNext(doc, Pos(0, 8), "hit", null, backwards: false, out bool wrapped);
        Assert.Equal(Header, inHeader!.Value.Story);
        Assert.False(wrapped);
        TextRange? backToBody = TextSearch.FindNext(doc, inHeader.Value.End, "hit", null, backwards: false, out wrapped);
        Assert.True(backToBody!.Value.Story.IsBody);
        Assert.True(wrapped);
        Assert.Null(TextSearch.FindNext(doc, Pos(0, 0), "zzz", null, backwards: false, out _));
    }

    [Fact]
    public void ReplaceRange_keeps_formatting_and_selects_the_replacement()
    {
        var session = new EditingSession(WithBlocks(new Paragraph([new Run("say "), new Run("hello", new RunProperties { Bold = true }), new Run(" there")])), new UndoStack(time: new FakeTime()));
        session.ReplaceRange(Range(0, 4, 0, 9), "goodbye");
        Assert.Equal("say goodbye there", session.Document.Text(0));
        Assert.True(session.Document.Para(0).Inlines[1].Properties.Bold);
        Assert.Equal("goodbye", ((Run)session.Document.Para(0).Inlines[1]).Text);
        Assert.Equal(new Selection(Pos(0, 4), Pos(0, 11)), session.Selection);
        session.ReplaceRange(session.Selection.Range, string.Empty);
        Assert.Equal("say  there", session.Document.Text(0));
        Assert.Equal(Selection.Caret(Pos(0, 4)), session.Selection);
    }

    [Fact]
    public void ReplaceAll_replaces_every_match_as_one_undo_step()
    {
        Document doc = WithHeader(WithParagraphs("cat cat", "a Cat"), "cat header");
        var session = new EditingSession(doc, new UndoStack(time: new FakeTime()));
        session.MoveCaret(Pos(1, 0), extend: false);
        int count = session.ReplaceAll("cat", "dog");
        Assert.Equal(4, count);
        Assert.Equal(["dog dog", "a dog"], session.Document.BodyTexts());
        Assert.Equal("dog header", ((Paragraph)session.Document.GetStory(Header)[0]).FlatText);
        Assert.Equal(Pos(1, 0), session.Selection.Active);

        Assert.True(session.Undo());
        Assert.Equal(["cat cat", "a Cat"], session.Document.BodyTexts());
        Assert.Equal(0, session.ReplaceAll("missing", "x"));
    }

    [Fact]
    public void ReplaceAll_with_a_longer_replacement_keeps_later_offsets_correct()
    {
        var session = new EditingSession(WithParagraphs("a-a-a"), new UndoStack(time: new FakeTime()));
        Assert.Equal(3, session.ReplaceAll("a", "bbb"));
        Assert.Equal("bbb-bbb-bbb", session.Document.Text(0));
    }
}
