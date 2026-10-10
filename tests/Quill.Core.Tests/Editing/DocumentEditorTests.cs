using System.Collections.Immutable;
using Quill.Core.Editing;
using Quill.Core.Model;
using Quill.Core.Styles;
using Quill.Core.Text;
using Quill.Core.Units;
using Xunit;
using static Quill.Core.Tests.Support.TestDocuments;

namespace Quill.Core.Tests.Editing;

public class DocumentEditorTests
{
    private static readonly RunProperties Bold = new() { Bold = true };
    private static readonly StyleResolver Resolver = new(DefaultStyleSheet.Create());

    [Fact]
    public void InsertText_extends_the_run_before_the_caret()
    {
        Document doc = WithBlocks(new Paragraph([new Run("ab", Bold), new Run("cd")]));
        EditResult result = DocumentEditor.InsertText(doc, Pos(0, 2), "X");
        Paragraph paragraph = result.Document.Para(0);
        Assert.Equal("abXcd", paragraph.FlatText);
        Assert.Equal(2, paragraph.Inlines.Length);
        Assert.Equal("abX", ((Run)paragraph.Inlines[0]).Text);
        Assert.Equal(Pos(0, 3), result.Selection.Active);
        Assert.True(result.Selection.IsCollapsed);
        Assert.Same(doc.Styles, result.Document.Styles);
    }

    [Fact]
    public void InsertText_with_explicit_format_creates_a_run()
    {
        Document doc = WithParagraphs("abcd");
        EditResult result = DocumentEditor.InsertText(doc, Pos(0, 2), "X", Bold);
        Assert.Equal(["ab", "X", "cd"], result.Document.Para(0).Inlines.Cast<Run>().Select(r => r.Text).ToArray());
        Assert.Equal(Bold, result.Document.Para(0).Inlines[1].Properties);
    }

    [Fact]
    public void InsertText_in_empty_paragraph_uses_mark_properties()
    {
        Document doc = WithBlocks(new Paragraph(ImmutableArray<Inline>.Empty, markProperties: Bold));
        EditResult result = DocumentEditor.InsertText(doc, Pos(0, 0), "hi");
        Assert.Equal(Bold, result.Document.Para(0).Inlines[0].Properties);
    }

    [Fact]
    public void InsertText_rejects_line_separators()
    {
        Document doc = WithParagraphs("a");
        Assert.Throws<ArgumentException>(() => DocumentEditor.InsertText(doc, Pos(0, 0), "x\ny"));
    }

    [Fact]
    public void Unchanged_paragraphs_keep_reference_identity()
    {
        Document doc = WithParagraphs("one", "two", "three");
        Document edited = DocumentEditor.InsertText(doc, Pos(1, 0), "X").Document;
        Assert.Same(doc.Para(0), edited.Para(0));
        Assert.NotSame(doc.Para(1), edited.Para(1));
        Assert.Same(doc.Para(2), edited.Para(2));
    }

    [Fact]
    public void DeleteRange_within_a_paragraph_splits_runs_correctly()
    {
        Document doc = WithBlocks(new Paragraph([new Run("abc", Bold), new Run("def")]));
        EditResult result = DocumentEditor.DeleteRange(doc, Range(0, 2, 0, 4));
        Paragraph paragraph = result.Document.Para(0);
        Assert.Equal("abef", paragraph.FlatText);
        Assert.Equal("ab", ((Run)paragraph.Inlines[0]).Text);
        Assert.Equal("ef", ((Run)paragraph.Inlines[1]).Text);
        Assert.Equal(Pos(0, 2), result.Selection.Active);
    }

    [Fact]
    public void DeleteRange_across_paragraphs_keeps_first_paragraph_formatting()
    {
        Document doc = WithBlocks(Paragraph.FromText("Heading", DefaultStyleSheet.Heading1Id), Paragraph.FromText("body text"));
        EditResult result = DocumentEditor.DeleteRange(doc, Range(0, 4, 1, 5));
        Assert.Equal(["Headtext"], result.Document.BodyTexts());
        Assert.Equal(DefaultStyleSheet.Heading1Id, result.Document.Para(0).StyleId);
    }

    [Fact]
    public void DeleteRange_that_removes_all_of_the_first_paragraph_keeps_the_last_ones_formatting()
    {
        Document doc = WithBlocks(Paragraph.FromText("Heading", DefaultStyleSheet.Heading1Id), Paragraph.FromText("body text"));
        EditResult result = DocumentEditor.DeleteRange(doc, Range(0, 0, 1, 5));
        Assert.Equal(["text"], result.Document.BodyTexts());
        Assert.Null(result.Document.Para(0).StyleId);
    }

    [Fact]
    public void DeleteRange_merges_adjacent_runs_with_equal_formatting()
    {
        Document doc = WithBlocks(new Paragraph([new Run("ab"), new Run("XX", Bold), new Run("cd")]));
        EditResult result = DocumentEditor.DeleteRange(doc, Range(0, 2, 0, 4));
        Assert.Single(result.Document.Para(0).Inlines);
    }

    [Fact]
    public void SplitParagraph_at_end_applies_next_style()
    {
        Document doc = WithBlocks(Paragraph.FromText("Heading", DefaultStyleSheet.Heading1Id));
        EditResult result = DocumentEditor.SplitParagraph(doc, Pos(0, 7), Resolver);
        Assert.Equal(["Heading", ""], result.Document.BodyTexts());
        Assert.Equal(StyleSheet.NormalStyleId, result.Document.Para(1).StyleId);
        Assert.Equal(Pos(1, 0), result.Selection.Active);
        Assert.Same(doc.Para(0), result.Document.Para(0));
    }

    [Fact]
    public void SplitParagraph_at_end_of_normal_keeps_typing_format_in_the_new_mark()
    {
        Document doc = WithBlocks(new Paragraph([new Run("ab", Bold)], properties: new ParagraphProperties { Alignment = Alignment.Center }));
        EditResult result = DocumentEditor.SplitParagraph(doc, Pos(0, 2), Resolver);
        Paragraph created = result.Document.Para(1);
        Assert.Equal(Bold, created.MarkProperties);
        Assert.Equal(Alignment.Center, created.Properties.Alignment);
    }

    [Fact]
    public void SplitParagraph_mid_paragraph_keeps_formatting_on_both_halves()
    {
        Document doc = WithBlocks(new Paragraph([new Run("abcd", Bold)], DefaultStyleSheet.Heading1Id));
        EditResult result = DocumentEditor.SplitParagraph(doc, Pos(0, 2), Resolver);
        Assert.Equal(["ab", "cd"], result.Document.BodyTexts());
        Assert.All(result.Document.Sections[0].Body.Cast<Paragraph>(), p => Assert.Equal(DefaultStyleSheet.Heading1Id, p.StyleId));
        Assert.Equal(Bold, result.Document.Para(0).MarkProperties);
    }

    [Fact]
    public void ApplyRunFormat_splits_runs_at_the_boundaries()
    {
        Document doc = WithParagraphs("abcdef");
        EditResult result = DocumentEditor.ApplyRunFormat(doc, Range(0, 2, 0, 4), Bold);
        Paragraph paragraph = result.Document.Para(0);
        Assert.Equal(["ab", "cd", "ef"], paragraph.Inlines.Cast<Run>().Select(r => r.Text).ToArray());
        Assert.True(paragraph.Inlines[1].Properties.Bold);
        Assert.Null(paragraph.Inlines[0].Properties.Bold);
    }

    [Fact]
    public void ApplyRunFormat_across_paragraphs_formats_inner_marks()
    {
        Document doc = WithParagraphs("ab", "cd");
        EditResult result = DocumentEditor.ApplyRunFormat(doc, Range(0, 1, 1, 1), Bold);
        Assert.True(result.Document.Para(0).MarkProperties.Bold);
        Assert.Null(result.Document.Para(1).MarkProperties.Bold);
        Assert.True(result.Document.Para(1).Inlines[0].Properties.Bold);
        Assert.Equal("c", ((Run)result.Document.Para(1).Inlines[0]).Text);
    }

    [Fact]
    public void ApplyParagraphFormat_touches_every_paragraph_in_range_including_collapsed()
    {
        Document doc = WithParagraphs("a", "b", "c");
        var center = new ParagraphProperties { Alignment = Alignment.Center };
        Document edited = DocumentEditor.ApplyParagraphFormat(doc, Range(0, 1, 1, 1), center).Document;
        Assert.Equal(Alignment.Center, edited.Para(0).Properties.Alignment);
        Assert.Equal(Alignment.Center, edited.Para(1).Properties.Alignment);
        Assert.Null(edited.Para(2).Properties.Alignment);

        // A range ending at the start of a paragraph (triple-click) does not touch that paragraph, like Word.
        Document brushed = DocumentEditor.ApplyParagraphFormat(doc, Range(0, 1, 1, 0), center).Document;
        Assert.Equal(Alignment.Center, brushed.Para(0).Properties.Alignment);
        Assert.Null(brushed.Para(1).Properties.Alignment);

        Document collapsed = DocumentEditor.ApplyParagraphFormat(doc, Range(2, 0, 2, 0), center).Document;
        Assert.Equal(Alignment.Center, collapsed.Para(2).Properties.Alignment);
    }


    [Fact]
    public void SetParagraphStyle_clears_direct_paragraph_formatting()
    {
        Document doc = WithBlocks(new Paragraph([new Run("x")], properties: new ParagraphProperties { Alignment = Alignment.Right }));
        Document edited = DocumentEditor.SetParagraphStyle(doc, Range(0, 0, 0, 0), DefaultStyleSheet.Heading1Id).Document;
        Assert.Equal(DefaultStyleSheet.Heading1Id, edited.Para(0).StyleId);
        Assert.True(edited.Para(0).Properties.IsEmpty);
    }

    [Fact]
    public void Fragment_round_trip_across_paragraphs()
    {
        Document doc = WithBlocks(Paragraph.FromText("one two"), Paragraph.FromText("mid", DefaultStyleSheet.Heading1Id), Paragraph.FromText("three four"));
        DocumentFragment fragment = DocumentEditor.ExtractFragment(doc, Range(0, 4, 2, 5));
        Assert.Equal("two\nmid\nthree", fragment.ToPlainText());
        Assert.Equal(DefaultStyleSheet.Heading1Id, fragment.Paragraphs[1].StyleId);

        Document target = WithParagraphs("[]");
        EditResult pasted = DocumentEditor.InsertFragment(target, Pos(0, 1), fragment);
        Assert.Equal(["[two", "mid", "three]"], pasted.Document.BodyTexts());
        Assert.Equal(Pos(2, 5), pasted.Selection.Active);
        Assert.Equal(DefaultStyleSheet.Heading1Id, pasted.Document.Para(1).StyleId);
        Assert.Null(pasted.Document.Para(2).StyleId);
    }

    [Fact]
    public void Single_paragraph_fragment_is_inserted_inline()
    {
        Document doc = WithParagraphs("ac");
        DocumentFragment fragment = DocumentFragment.FromPlainText("b", Bold);
        EditResult pasted = DocumentEditor.InsertFragment(doc, Pos(0, 1), fragment);
        Assert.Equal(["abc"], pasted.Document.BodyTexts());
        Assert.Equal(Pos(0, 2), pasted.Selection.Active);
        Assert.Equal(3, pasted.Document.Para(0).Inlines.Length);
    }

    [Fact]
    public void Plain_text_fragments_split_on_any_newline_convention()
    {
        DocumentFragment fragment = DocumentFragment.FromPlainText("a\r\nb\nc\rd");
        Assert.Equal(["a", "b", "c", "d"], fragment.Paragraphs.Select(p => p.FlatText).ToArray());
    }

    [Fact]
    public void InsertSectionBreak_moves_following_paragraphs_to_a_new_section()
    {
        Document doc = WithParagraphs("one", "two", "three");
        EditResult result = DocumentEditor.InsertSectionBreak(doc, Pos(1, 0), SectionStart.NextPage, Resolver);
        Assert.Equal(2, result.Document.Sections.Count);
        Assert.Equal(["one"], result.Document.Sections[0].Body.Cast<Paragraph>().Select(p => p.FlatText));
        Assert.Equal(["two", "three"], result.Document.Sections[1].Body.Cast<Paragraph>().Select(p => p.FlatText));
        Assert.Equal(SectionStart.NextPage, result.Document.Sections[1].Properties.Start);
        Assert.Equal(TextPosition.StartOf(StoryId.Body(1)), result.Selection.Active);
        Assert.True(result.Change.StructureChanged);
    }

    [Fact]
    public void InsertSectionBreak_mid_paragraph_splits_it_first()
    {
        Document doc = WithParagraphs("abcd");
        EditResult result = DocumentEditor.InsertSectionBreak(doc, Pos(0, 2), SectionStart.OddPage, Resolver);
        Assert.Equal(["ab"], result.Document.Sections[0].Body.Cast<Paragraph>().Select(p => p.FlatText));
        Assert.Equal(["cd"], result.Document.Sections[1].Body.Cast<Paragraph>().Select(p => p.FlatText));
    }

    [Fact]
    public void Story_access_covers_headers_and_footers()
    {
        Document doc = WithParagraphs("body");
        var header = new StoryId(0, StoryKind.HeaderDefault);
        Assert.Null(doc.TryGetStory(header));
        Document withHeader = doc.WithStory(header, ImmutableList.Create<Block>(Paragraph.FromText("hdr")));
        Assert.Equal("hdr", ((Paragraph)withHeader.GetStory(header)[0]).FlatText);
        Assert.Same(doc.Sections[0].Body, withHeader.Sections[0].Body);
        Assert.Equal(2, withHeader.Stories().Count());
    }

    [Fact]
    public void Page_setup_changes_are_structural()
    {
        Document doc = WithParagraphs("x");
        SectionProperties landscape = doc.Sections[0].Properties.WithOrientation(Orientation.Landscape);
        Assert.Equal(Twips.FromInches(11), landscape.PageWidth);
        EditResult result = DocumentEditor.SetSectionProperties(doc, 0, landscape, Selection.Caret(Pos(0, 0)));
        Assert.True(result.Change.StructureChanged);
        Assert.True(DocumentEditor.SetSectionProperties(result.Document, 0, landscape, Selection.Caret(Pos(0, 0))).IsNoOp);
    }
}
