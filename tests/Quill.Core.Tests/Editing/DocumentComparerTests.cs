using System.Collections.Immutable;
using Quill.Core.Editing;
using Quill.Core.Model;
using Quill.Core.Styles;
using Quill.Core.Units;
using Xunit;
using static Quill.Core.Tests.Support.TestDocuments;

namespace Quill.Core.Tests.Editing;

/// <summary>Word's Compare: the revised document with the differences from the original as tracked changes.</summary>
public class DocumentComparerTests
{
    private static readonly DateTime Clock = new(2026, 10, 11, 10, 0, 0);
    private static readonly byte[] Png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==");

    private static Document Compare(Document original, Document revised) => DocumentComparer.Compare(original, revised, "Reviewer", Clock);

    private static IEnumerable<(string Text, RevisionKind? Kind)> Runs(Document document, int index) =>
        document.Para(index).Inlines.Select(i => (i is Run run ? run.Text : "￼", i.Properties.Revision?.Kind));

    [Fact]
    public void Identical_documents_compare_without_changes()
    {
        Document result = Compare(WithParagraphs("One", "Two"), WithParagraphs("One", "Two"));
        Assert.Equal(2, result.Sections[0].Body.Count);
        Assert.All(DocumentEditor.AllParagraphs(result), p => Assert.DoesNotContain(p.Paragraph.Inlines, i => i.Properties.Revision is not null));
    }

    [Fact]
    public void Changed_words_show_the_old_word_deleted_and_the_new_word_inserted()
    {
        Document result = Compare(WithParagraphs("The quick brown fox"), WithParagraphs("The quick red fox jumps"));
        Assert.Equal("The quick brownred fox jumps", result.Text(0));
        Assert.Equal(
            [("The quick ", null), ("brown", RevisionKind.Deletion), ("red", RevisionKind.Insertion), (" fox", null), (" jumps", RevisionKind.Insertion)],
            Runs(result, 0));
        Revision revision = result.Para(0).Inlines[1].Properties.Revision!;
        Assert.Equal("Reviewer", revision.Author);
        Assert.Equal(Clock, revision.Date);
    }

    [Fact]
    public void Added_and_removed_paragraphs_are_marked_whole_and_keep_their_place()
    {
        Document result = Compare(WithParagraphs("A", "B", "C"), WithParagraphs("A", "C", "D"));
        Assert.Equal(["A", "B", "C", "D"], Enumerable.Range(0, 4).Select(result.Text));
        Assert.Equal([("B", RevisionKind.Deletion)], Runs(result, 1));
        Assert.Equal([("D", RevisionKind.Insertion)], Runs(result, 3));
        Assert.Equal([("A", null)], Runs(result, 0));
    }

    [Fact]
    public void A_changed_paragraph_is_paired_with_its_similar_original_even_after_a_deleted_one()
    {
        Document result = Compare(WithParagraphs("Gone entirely", "Keep most of these words here"), WithParagraphs("Keep most of those words here"));
        Assert.Equal(2, result.Sections[0].Body.Count);
        Assert.Equal([("Gone entirely", RevisionKind.Deletion)], Runs(result, 0));
        Assert.Equal(
            [("Keep most of ", null), ("these", RevisionKind.Deletion), ("those", RevisionKind.Insertion), (" words here", null)],
            Runs(result, 1));
    }

    [Fact]
    public void Revised_formatting_wins_for_unchanged_text_and_original_marks_are_dropped_from_deleted_text()
    {
        var original = new Document(ImmutableList.Create(new Section(SectionProperties.Letter, ImmutableList.Create<Block>(
            new Paragraph([new Run("Same "), new Run("old", new RunProperties { CommentId = 7, Bookmark = "b" }), new FootnoteReference(3)])))), DefaultStyleSheet.Create());
        var revised = new Document(ImmutableList.Create(new Section(SectionProperties.Letter, ImmutableList.Create<Block>(
            new Paragraph([new Run("Same ", new RunProperties { Bold = true })])))), DefaultStyleSheet.Create());

        Document result = Compare(original, revised);
        Assert.Equal([("Same ", null), ("old", RevisionKind.Deletion)], Runs(result, 0));
        Assert.True(result.Para(0).Inlines[0].Properties.Bold);
        RunProperties deleted = result.Para(0).Inlines[1].Properties;
        Assert.Null(deleted.CommentId);
        Assert.Null(deleted.Bookmark);
    }

    [Fact]
    public void Tracked_changes_already_in_the_documents_are_accepted_before_comparing()
    {
        var session = new EditingSession(WithParagraphs("Hello world")) { Author = "Ada", Now = () => Clock };
        session.SetTrackChanges(true);
        session.MoveCaret(Pos(0, 5), extend: false);
        session.MoveCaret(Pos(0, 11), extend: true);
        session.DeleteSelection();
        Assert.Equal("Hello world", session.Document.Text(0)); // still there, struck through

        Document result = Compare(session.Document, WithParagraphs("Hello"));
        Assert.Equal([("Hello", null)], Runs(result, 0));
    }

    [Fact]
    public void A_changed_table_shows_the_old_table_deleted_and_the_new_one_inserted()
    {
        static Table Grid(string text) => new([new TableRow([new TableCell(ImmutableList.Create<Block>(Paragraph.FromText(text)))])], [Twips.FromInches(2)], hasBorders: true, Twips.Zero);
        static Document With(Table table) => new(ImmutableList.Create(new Section(SectionProperties.Letter, ImmutableList.Create<Block>(Paragraph.FromText("Intro"), table))), DefaultStyleSheet.Create());

        Document result = Compare(With(Grid("one")), With(Grid("two")));
        Assert.Equal(3, result.Sections[0].Body.Count);
        var deleted = Assert.IsType<Table>(result.Sections[0].Body[1]);
        var inserted = Assert.IsType<Table>(result.Sections[0].Body[2]);
        Assert.Equal(RevisionKind.Deletion, ((Paragraph)deleted.Rows[0].Cells[0].Blocks[0]).Inlines[0].Properties.Revision!.Kind);
        Assert.Equal(RevisionKind.Insertion, ((Paragraph)inserted.Rows[0].Cells[0].Blocks[0]).Inlines[0].Properties.Revision!.Kind);

        Document same = Compare(With(Grid("one")), With(Grid("one")));
        Assert.Equal(2, same.Sections[0].Body.Count);
    }

    [Fact]
    public void Pictures_in_deleted_text_are_copied_under_an_id_that_does_not_collide()
    {
        var originalData = new ImageData(Png, "image/png", 1, 1);
        var revisedData = new ImageData([.. Png, 0], "image/png", 1, 1);
        var original = new Document(ImmutableList.Create(new Section(SectionProperties.Letter, ImmutableList.Create<Block>(
            new Paragraph([new Run("Pic "), new InlineImage("image1", new Twips(720), new Twips(720))])))), DefaultStyleSheet.Create(), images: ImageStore.Empty.With("image1", originalData));
        var revised = new Document(ImmutableList.Create(new Section(SectionProperties.Letter, ImmutableList.Create<Block>(
            new Paragraph([new Run("Pic "), new InlineImage("image1", new Twips(360), new Twips(360))])))), DefaultStyleSheet.Create(), images: ImageStore.Empty.With("image1", revisedData));

        Document result = Compare(original, revised);
        List<InlineImage> images = result.Para(0).Inlines.OfType<InlineImage>().ToList();
        Assert.Equal(2, images.Count);
        InlineImage deleted = images.Single(i => i.Properties.Revision is { IsDeletion: true });
        InlineImage kept = images.Single(i => i.Properties.Revision is { IsInsertion: true }); // a swapped picture is a deletion plus an insertion
        Assert.NotEqual("image1", deleted.ImageId);
        Assert.Equal("image1", kept.ImageId);
        Assert.Same(originalData, result.Images.Get(deleted.ImageId));
        Assert.Same(revisedData, result.Images.Get("image1"));
    }

    [Fact]
    public void Extra_original_sections_show_as_deleted_at_the_end()
    {
        var original = new Document(ImmutableList.Create(
            new Section(SectionProperties.Letter, ImmutableList.Create<Block>(Paragraph.FromText("First"))),
            new Section(SectionProperties.Letter, ImmutableList.Create<Block>(Paragraph.FromText("Second")))), DefaultStyleSheet.Create());
        Document result = Compare(original, WithParagraphs("First"));
        Assert.Single(result.Sections);
        Assert.Equal(["First", "Second"], Enumerable.Range(0, 2).Select(result.Text));
        Assert.Equal([("Second", RevisionKind.Deletion)], Runs(result, 1));
    }
}
