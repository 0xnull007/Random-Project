using System.Collections.Immutable;
using Quill.Core.Editing;
using Quill.Core.Model;
using Quill.Core.Styles;
using Quill.Core.Tests.Support;
using Quill.Core.Text;
using Quill.Core.Units;
using Xunit;
using static Quill.Core.Tests.Support.TestDocuments;

namespace Quill.Core.Tests.Model;

public class ListTests
{
    private static (Document Document, int NumberingId) NumberedDocument(params (string Text, int Level)[] items)
    {
        (ListStore store, int id) = ListStore.Empty.AddList(DefaultLists.NumberedLevels());
        ImmutableList<Block> blocks = items
            .Select(i => (Block)new Paragraph([new Run(i.Text)], properties: new ParagraphProperties { List = new ListFormat(id, i.Level) }))
            .ToImmutableList();
        var document = new Document(ImmutableList.Create(new Section(SectionProperties.Letter, blocks)), DefaultStyleSheet.Create(), lists: store);
        return (document, id);
    }

    private static IReadOnlyDictionary<int, ListMarker> Markers(Document document) =>
        ListNumbering.Compute(document.Sections[0].Body, document.Lists, new StyleResolver(document.Styles, document.Lists));

    [Theory]
    [InlineData(1, NumberFormat.Decimal, "1")]
    [InlineData(27, NumberFormat.LowerLetter, "aa")]
    [InlineData(3, NumberFormat.UpperLetter, "C")]
    [InlineData(4, NumberFormat.LowerRoman, "iv")]
    [InlineData(1994, NumberFormat.UpperRoman, "MCMXCIV")]
    [InlineData(7, NumberFormat.Bullet, "")]
    public void Number_text_formats(int number, NumberFormat format, string expected)
    {
        Assert.Equal(expected, NumberText.Format(number, format));
    }

    [Fact]
    public void Numbering_counts_per_level_and_resets_deeper_levels()
    {
        (Document doc, _) = NumberedDocument(("one", 0), ("one-a", 1), ("one-b", 1), ("one-b-i", 2), ("two", 0), ("two-a", 1));
        IReadOnlyDictionary<int, ListMarker> markers = Markers(doc);
        Assert.Equal(["1.", "a.", "b.", "i.", "2.", "a."], Enumerable.Range(0, 6).Select(i => markers[i].Text));
        Assert.Equal(Alignment.Right, markers[3].Alignment);
    }

    [Fact]
    public void Nested_templates_and_start_overrides()
    {
        var levels = ImmutableArray.Create(
            new ListLevel { Format = NumberFormat.Decimal, Text = "%1." },
            new ListLevel { Format = NumberFormat.Decimal, Text = "%1.%2", Start = 5 });
        (ListStore store, int first) = ListStore.Empty.AddList(levels);
        var restarted = new ListInstance(first + 1, 0, ImmutableDictionary<int, int>.Empty.Add(0, 10));
        store = store.With(restarted);
        Block[] blocks =
        [
            new Paragraph([new Run("a")], properties: new ParagraphProperties { List = new ListFormat(first, 0) }),
            new Paragraph([new Run("b")], properties: new ParagraphProperties { List = new ListFormat(first, 1) }),
            new Paragraph([new Run("c")], properties: new ParagraphProperties { List = new ListFormat(first, 1) }),
            new Paragraph([new Run("plain")]),
            new Paragraph([new Run("d")], properties: new ParagraphProperties { List = new ListFormat(restarted.Id, 0) }),
            new Paragraph([new Run("e")], properties: new ParagraphProperties { List = new ListFormat(restarted.Id, 1) }),
        ];
        var doc = new Document(ImmutableList.Create(new Section(SectionProperties.Letter, blocks.ToImmutableList())), StyleSheet.Empty, lists: store);
        IReadOnlyDictionary<int, ListMarker> markers = Markers(doc);
        Assert.Equal("1.", markers[0].Text);
        Assert.Equal("1.5", markers[1].Text);
        Assert.Equal("1.6", markers[2].Text);
        Assert.False(markers.ContainsKey(3));
        Assert.Equal("10.", markers[4].Text);
        Assert.Equal("10.5", markers[5].Text);
    }

    [Fact]
    public void Bullet_markers_use_the_level_text_and_resolver_applies_level_indents()
    {
        (ListStore store, int id) = ListStore.Empty.AddList(DefaultLists.BulletLevels());
        var paragraph = new Paragraph([new Run("x")], properties: new ParagraphProperties { List = new ListFormat(id, 1) });
        var doc = new Document(ImmutableList.Create(new Section(SectionProperties.Letter, ImmutableList.Create<Block>(paragraph))), StyleSheet.Empty, lists: store);
        var resolver = new StyleResolver(doc.Styles, doc.Lists);
        ResolvedParagraphProperties resolved = resolver.ResolveParagraph(paragraph);
        Assert.True(resolved.IsListItem);
        Assert.Equal(Twips.FromInches(1.0), resolved.LeftIndent);
        Assert.Equal(-Twips.FromInches(0.25), resolved.FirstLineIndent);
        Assert.Equal("o", Markers(doc)[0].Text);

        // Direct indents still win over the level's.
        var custom = paragraph.WithProperties(paragraph.Properties with { LeftIndent = Twips.FromInches(2) });
        Assert.Equal(Twips.FromInches(2), resolver.ResolveParagraph(custom).LeftIndent);

        // ListFormat.None switches a list off.
        var off = paragraph.WithProperties(paragraph.Properties with { List = ListFormat.None });
        Assert.False(resolver.ResolveParagraph(off).IsListItem);
    }

    [Fact]
    public void Toggle_list_creates_joins_and_removes_lists()
    {
        var session = new EditingSession(WithParagraphs("first", "second", "third"), new UndoStack(time: new FakeTime()));
        session.SetSelection(new Selection(Pos(0, 0), Pos(1, 3)));
        session.ToggleList(bulleted: true);
        Assert.True(session.ListKind(session.Document.Para(0)));
        Assert.True(session.ListKind(session.Document.Para(1)));
        Assert.Null(session.ListKind(session.Document.Para(2)));
        int numberingId = session.Document.Para(0).Properties.List!.Value.NumberingId;

        // The next paragraph joins the existing bullet list instead of starting a new one.
        session.MoveCaret(Pos(2, 0), extend: false);
        session.ToggleList(bulleted: true);
        Assert.Equal(numberingId, session.Document.Para(2).Properties.List!.Value.NumberingId);

        // Switching to numbered creates a numbered list; toggling again removes it.
        session.ToggleList(bulleted: false);
        Assert.False(session.ListKind(session.Document.Para(2)));
        session.ToggleList(bulleted: false);
        Assert.Null(session.ListKind(session.Document.Para(2)));
        Assert.True(session.Document.Para(2).Properties.List!.Value.IsNone);

        session.Undo();
        Assert.False(session.ListKind(session.Document.Para(2)));
    }

    [Fact]
    public void Change_indent_moves_list_levels_and_plain_indents()
    {
        var session = new EditingSession(WithParagraphs("item", "plain"), new UndoStack(time: new FakeTime()));
        session.ToggleList(bulleted: false);
        session.ChangeIndent(1);
        Assert.Equal(1, session.Document.Para(0).Properties.List!.Value.Level);
        session.ChangeIndent(-1);
        session.ChangeIndent(-1);
        Assert.Equal(0, session.Document.Para(0).Properties.List!.Value.Level);

        session.MoveCaret(Pos(1, 0), extend: false);
        session.ChangeIndent(1);
        Assert.Equal(Twips.FromInches(0.5), session.CaretParagraphFormat().LeftIndent);
        session.ChangeIndent(-1);
        Assert.Equal(Twips.Zero, session.CaretParagraphFormat().LeftIndent);
    }

    [Fact]
    public void Enter_in_a_list_continues_the_list()
    {
        var session = new EditingSession(WithParagraphs("item"), new UndoStack(time: new FakeTime()));
        session.ToggleList(bulleted: true);
        session.MoveCaret(Pos(0, 4), extend: false);
        session.InsertParagraphBreak();
        Assert.True(session.ListKind(session.Document.Para(1)));
        Assert.Equal(["•", "•"], Markers(session.Document).Values.Select(m => m.Text));
    }
}
