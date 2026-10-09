using System.Collections.Immutable;
using Quill.Core.Model;
using Quill.Core.Styles;
using Quill.Core.Text;
using Quill.Core.Units;
using Quill.Layout;
using Xunit;

namespace Quill.Layout.Tests;

public class PaginatorTests
{
    private const double Margin = 20;

    /// <summary>A page whose body holds exactly <paramref name="lines"/> lines of <paramref name="charsPerLine"/> characters (fake metrics).</summary>
    private static SectionProperties Page(int lines, int charsPerLine = 10) => new()
    {
        PageWidth = Twips.FromDips(charsPerLine * FakeLineFormatter.CharWidth + 2 * Margin),
        PageHeight = Twips.FromDips(lines * FakeLineFormatter.LineHeight + 2 * Margin),
        MarginTop = Twips.FromDips(Margin),
        MarginBottom = Twips.FromDips(Margin),
        MarginLeft = Twips.FromDips(Margin),
        MarginRight = Twips.FromDips(Margin),
        HeaderDistance = Twips.FromDips(5),
        FooterDistance = Twips.FromDips(5),
    };

    private static Document Doc(SectionProperties page, params Block[] blocks) =>
        new(ImmutableList.Create(new Section(page, blocks.ToImmutableList())), StyleSheet.Empty);

    private static Document Doc(params Section[] sections) => new(sections.ToImmutableList(), StyleSheet.Empty);

    private static Paragraph P(string text, ParagraphProperties? properties = null, string? styleId = null) =>
        new([new Run(text)], styleId, properties);

    /// <summary>Text that the fake formatter breaks into exactly <paramref name="count"/> 10-character lines.</summary>
    private static string Lines(int count) => string.Concat(Enumerable.Repeat("abcdefghi ", count));

    private static Block[] SingleLines(int count) => Enumerable.Range(0, count).Select(i => (Block)P($"line{i}")).ToArray();

    private static (LayoutDocument Layout, FakeLineFormatter Formatter, LayoutCache Cache) Run(Document document, LayoutCache? cache = null, FakeLineFormatter? formatter = null)
    {
        formatter ??= new FakeLineFormatter();
        cache ??= new LayoutCache();
        var paginator = new Paginator(formatter, cache);
        return (paginator.Layout(document, new StyleResolver(document.Styles)), formatter, cache);
    }

    private static ParagraphFragment[] Paragraphs(PageLayout page) => page.Body.OfType<ParagraphFragment>().ToArray();

    [Fact]
    public void Short_paragraph_fills_one_page()
    {
        (LayoutDocument layout, _, _) = Run(Doc(Page(5), P("hello")));
        PageLayout page = Assert.Single(layout.Pages);
        ParagraphFragment fragment = Assert.Single(Paragraphs(page));
        Assert.Equal(0, fragment.FirstLine);
        Assert.Equal(0, fragment.LastLine);
        Assert.Equal(Margin, fragment.Bounds.Top);
        Assert.Equal(Margin, fragment.Bounds.Left);
        Assert.Equal(100, page.BodyArea.Width);
        Assert.Equal(1, page.PageNumber);
        Assert.Equal("1", page.PageNumberText);
    }

    [Fact]
    public void Paragraphs_flow_onto_the_next_page_in_order()
    {
        (LayoutDocument layout, _, _) = Run(Doc(Page(5), SingleLines(7)));
        Assert.Equal(2, layout.PageCount);
        Assert.Equal(5, layout.Pages[0].Body.Length);
        Assert.Equal(2, layout.Pages[1].Body.Length);
        Assert.Equal([0, 1, 2, 3, 4], layout.Pages[0].Body.Select(f => f.Path.TopIndex));
        Assert.Equal([5, 6], layout.Pages[1].Body.Select(f => f.Path.TopIndex));
        Assert.Equal(Margin + 3 * 20, layout.Pages[0].Body[3].Bounds.Top);
        Assert.Equal(Margin, layout.Pages[1].Body[0].Bounds.Top);
    }

    [Fact]
    public void Long_paragraph_splits_leaving_at_least_two_lines_on_each_side()
    {
        (LayoutDocument layout, _, _) = Run(Doc(Page(5), P("x"), P(Lines(6))));
        Assert.Equal(2, layout.PageCount);
        ParagraphFragment[] first = Paragraphs(layout.Pages[0]);
        Assert.Equal(2, first.Length);
        Assert.Equal((0, 3), (first[1].FirstLine, first[1].LastLine));
        ParagraphFragment rest = Assert.Single(Paragraphs(layout.Pages[1]));
        Assert.Equal((4, 5), (rest.FirstLine, rest.LastLine));
        Assert.Same(first[1].Layout, rest.Layout);
        Assert.False(first[1].IsParagraphEnd);
        Assert.True(rest.IsParagraphEnd);
    }

    [Fact]
    public void Widow_control_moves_a_paragraph_that_cannot_split_cleanly()
    {
        // 3 lines used; a 3-line paragraph could leave 2 lines here and 1 widow there, so it moves whole.
        (LayoutDocument layout, _, _) = Run(Doc(Page(5), [.. SingleLines(3), P(Lines(3))]));
        Assert.Equal(2, layout.PageCount);
        Assert.Equal(3, layout.Pages[0].Body.Length);
        ParagraphFragment moved = Assert.Single(Paragraphs(layout.Pages[1]));
        Assert.Equal((0, 2), (moved.FirstLine, moved.LastLine));
    }

    [Fact]
    public void Orphan_control_refuses_a_single_line_at_the_page_bottom()
    {
        (LayoutDocument layout, _, _) = Run(Doc(Page(5), [.. SingleLines(4), P(Lines(4))]));
        Assert.Equal(4, layout.Pages[0].Body.Length);
        ParagraphFragment moved = Assert.Single(Paragraphs(layout.Pages[1]));
        Assert.Equal((0, 3), (moved.FirstLine, moved.LastLine));
    }

    [Fact]
    public void Widow_and_orphan_control_can_be_turned_off()
    {
        var noControl = new ParagraphProperties { WidowControl = false };
        (LayoutDocument layout, _, _) = Run(Doc(Page(5), [.. SingleLines(4), P(Lines(4), noControl)]));
        ParagraphFragment tail = Paragraphs(layout.Pages[0])[^1];
        Assert.Equal((0, 0), (tail.FirstLine, tail.LastLine));
        Assert.Equal((1, 3), (Paragraphs(layout.Pages[1])[0].FirstLine, Paragraphs(layout.Pages[1])[0].LastLine));
    }

    [Fact]
    public void Keep_lines_together_moves_the_whole_paragraph()
    {
        var keep = new ParagraphProperties { KeepLinesTogether = true };
        (LayoutDocument layout, _, _) = Run(Doc(Page(5), [.. SingleLines(2), P(Lines(4), keep)]));
        Assert.Equal(2, layout.Pages[0].Body.Length);
        Assert.Equal((0, 3), (Paragraphs(layout.Pages[1])[0].FirstLine, Paragraphs(layout.Pages[1])[0].LastLine));
    }

    [Fact]
    public void Keep_lines_together_still_splits_a_paragraph_taller_than_a_page()
    {
        var keep = new ParagraphProperties { KeepLinesTogether = true };
        (LayoutDocument layout, _, _) = Run(Doc(Page(5), P(Lines(7), keep)));
        Assert.Equal(2, layout.PageCount);
        Assert.Equal((0, 4), (Paragraphs(layout.Pages[0])[0].FirstLine, Paragraphs(layout.Pages[0])[0].LastLine));
    }

    [Fact]
    public void Keep_with_next_carries_the_heading_to_the_next_page()
    {
        var heading = new ParagraphProperties { KeepWithNext = true };
        (LayoutDocument layout, _, _) = Run(Doc(Page(5), [.. SingleLines(4), P("Heading", heading), P(Lines(3))]));
        Assert.Equal(2, layout.PageCount);
        Assert.Equal(4, layout.Pages[0].Body.Length);
        ParagraphFragment[] second = Paragraphs(layout.Pages[1]);
        Assert.Equal(2, second.Length);
        Assert.Equal("Heading", second[0].Paragraph.FlatText);
        Assert.Equal(Margin, second[0].Bounds.Top);
        Assert.Equal(Margin + 20, second[1].Bounds.Top);
    }

    [Fact]
    public void Keep_with_next_chain_follows_the_body_paragraph()
    {
        var keep = new ParagraphProperties { KeepWithNext = true };
        (LayoutDocument layout, _, _) = Run(Doc(Page(5), [.. SingleLines(3), P("H1", keep), P("H2", keep), P(Lines(3))]));
        Assert.Equal(3, layout.Pages[0].Body.Length);
        Assert.Equal(["H1", "H2"], Paragraphs(layout.Pages[1]).Take(2).Select(f => f.Paragraph.FlatText));
    }

    [Fact]
    public void Page_break_before_starts_a_new_page_unless_already_at_the_top()
    {
        var pbb = new ParagraphProperties { PageBreakBefore = true };
        (LayoutDocument layout, _, _) = Run(Doc(Page(5), P("a", pbb), P("b"), P("c", pbb)));
        Assert.Equal(2, layout.PageCount);
        Assert.Equal(["a", "b"], Paragraphs(layout.Pages[0]).Select(f => f.Paragraph.FlatText));
        Assert.Equal(["c"], Paragraphs(layout.Pages[1]).Select(f => f.Paragraph.FlatText));
    }

    [Fact]
    public void Explicit_page_break_splits_the_paragraph_and_positions_map_to_pages()
    {
        var paragraph = new Paragraph([new Run("ab"), new Break(BreakKind.Page), new Run("cd")]);
        Document doc = Doc(Page(5), paragraph);
        (LayoutDocument layout, _, _) = Run(doc);
        Assert.Equal(2, layout.PageCount);
        ParagraphFragment head = Assert.Single(Paragraphs(layout.Pages[0]));
        ParagraphFragment tail = Assert.Single(Paragraphs(layout.Pages[1]));
        Assert.Equal((0, 0), (head.FirstLine, head.LastLine));
        Assert.Equal((1, 1), (tail.FirstLine, tail.LastLine));
        Assert.Equal(BreakKind.Page, head.Layout.Lines[0].ForcedBreakAfter);

        var body = StoryId.Body(0);
        Assert.Equal(0, layout.Find(new TextPosition(body, BlockPath.Of(0), 2))!.Value.Page.Index);
        Assert.Equal(1, layout.Find(new TextPosition(body, BlockPath.Of(0), 3))!.Value.Page.Index);
        Assert.Equal(1, layout.Find(new TextPosition(body, BlockPath.Of(0), 5))!.Value.Page.Index);
        Assert.Equal(1, head.Layout.LineIndexOf(3));
    }

    [Fact]
    public void Trailing_page_break_puts_the_paragraph_mark_on_a_new_page()
    {
        var paragraph = new Paragraph([new Run("ab"), new Break(BreakKind.Page)]);
        (LayoutDocument layout, _, _) = Run(Doc(Page(5), paragraph, P("next")));
        Assert.Equal(2, layout.PageCount);
        ParagraphFragment[] second = Paragraphs(layout.Pages[1]);
        Assert.Equal(2, second.Length);
        Assert.Equal(1, second[0].FirstLine);
        Assert.Equal(0, second[0].Layout.Lines[1].Length);
        Assert.Equal(1, layout.Find(new TextPosition(StoryId.Body(0), BlockPath.Of(0), 3))!.Value.Page.Index);
    }

    [Fact]
    public void Line_break_wraps_within_the_paragraph()
    {
        var paragraph = new Paragraph([new Run("ab"), new Break(BreakKind.Line), new Run("cd")]);
        (LayoutDocument layout, _, _) = Run(Doc(Page(5), paragraph));
        ParagraphFragment fragment = Assert.Single(Paragraphs(layout.Pages[0]));
        Assert.Equal(2, fragment.LineCount);
        Assert.Null(fragment.Layout.Lines[0].ForcedBreakAfter);
        Assert.Equal(1, fragment.Layout.Lines[0].NewlineLength);
    }

    [Fact]
    public void Odd_section_start_inserts_a_filler_page_and_numbering_can_restart()
    {
        var first = new Section(Page(5), ImmutableList.Create<Block>(P("one")));
        var second = new Section(Page(5) with { Start = SectionStart.OddPage, PageNumberStart = 7, PageNumberFormat = PageNumberFormat.LowerRoman }, ImmutableList.Create<Block>(P("two")));
        (LayoutDocument layout, _, _) = Run(Doc(first, second));
        Assert.Equal(3, layout.PageCount);
        Assert.True(layout.Pages[1].IsBlankFiller);
        Assert.Equal(0, layout.Pages[1].SectionIndex);
        Assert.Equal(1, layout.Pages[2].SectionIndex);
        Assert.Equal(7, layout.Pages[2].PageNumber);
        Assert.Equal("vii", layout.Pages[2].PageNumberText);
    }

    [Fact]
    public void Even_section_start_without_parity_conflict_adds_no_filler()
    {
        var first = new Section(Page(5), ImmutableList.Create<Block>(P("one")));
        var second = new Section(Page(5) with { Start = SectionStart.EvenPage }, ImmutableList.Create<Block>(P("two")));
        (LayoutDocument layout, _, _) = Run(Doc(first, second));
        Assert.Equal(2, layout.PageCount);
        Assert.Equal(2, layout.Pages[1].PageNumber);
    }

    [Fact]
    public void Header_with_page_fields_is_laid_out_per_page_and_pushes_the_body_down()
    {
        var header = new Paragraph([Field.Page(), new Run(" of "), Field.NumPages()]);
        var section = new Section(Page(5), SingleLines(12).ToImmutableList(), new HeaderFooterSet(ImmutableList.Create<Block>(header), null, null));
        (LayoutDocument layout, FakeLineFormatter formatter, _) = Run(Doc(section));

        // Header occupies 5..25, so the body starts at 25 and holds 4 lines (95/20) per page.
        Assert.Equal(3, layout.PageCount);
        Assert.Equal(25, layout.Pages[0].BodyArea.Top);
        Assert.All(layout.Pages, p => Assert.NotNull(p.Header));
        var headerFragment = (ParagraphFragment)layout.Pages[1].Header!.Fragments[0];
        Assert.Equal("2", headerFragment.Layout.Input.Runs[0].FieldText);
        Assert.Equal("3", headerFragment.Layout.Input.Runs[2].FieldText);
        Assert.Equal(5, headerFragment.Bounds.Top);
    }

    [Fact]
    public void Footer_sits_above_the_footer_distance_and_limits_the_body()
    {
        var footer = new Paragraph([Field.Page()]);
        var section = new Section(Page(5), SingleLines(6).ToImmutableList(), null, new HeaderFooterSet(ImmutableList.Create<Block>(footer), null, null));
        (LayoutDocument layout, _, _) = Run(Doc(section));
        PageLayout page = layout.Pages[0];
        double pageHeight = 5 * 20 + 2 * Margin;
        Assert.Equal(pageHeight - 5 - 20, page.Footer!.Bounds.Top);
        Assert.Equal(pageHeight - 25, page.BodyArea.Bottom);
        Assert.Equal(2, layout.PageCount);
        Assert.Equal(4, layout.Pages[0].Body.Length);
    }

    [Fact]
    public void Headers_link_to_the_previous_section_and_title_page_uses_the_first_variant()
    {
        var defaultHeader = ImmutableList.Create<Block>(P("default"));
        var firstHeader = ImmutableList.Create<Block>(P("first"));
        var s1 = new Section(Page(5) with { TitlePage = true }, SingleLines(4).ToImmutableList(), new HeaderFooterSet(defaultHeader, firstHeader, null));
        var s2 = new Section(Page(5), ImmutableList.Create<Block>(P("s2")));
        (LayoutDocument layout, _, _) = Run(Doc(s1, s2));
        Assert.Equal(2, layout.PageCount);
        Assert.Equal("first", ((ParagraphFragment)layout.Pages[0].Header!.Fragments[0]).Paragraph.FlatText);
        Assert.Equal("default", ((ParagraphFragment)layout.Pages[1].Header!.Fragments[0]).Paragraph.FlatText);
        Assert.Equal(new StoryId(0, StoryKind.HeaderDefault), layout.Pages[1].Header!.Story);
    }

    [Fact]
    public void Space_before_is_kept_at_document_start_and_after_hard_breaks_but_not_at_flow_page_tops()
    {
        var spaced = new ParagraphProperties { SpaceBefore = Twips.FromDips(20) };
        Block[] blocks =
        [
            P("a", spaced),                       // doc start: space kept (occupies line 1+2)
            P("b"), P("c"), P("d"),               // lines 3,4,5 -> page full
            P("e", spaced),                       // top of page 2 by flow: suppressed
            new Paragraph([new Run("f"), new Break(BreakKind.Page)]),
            P("g", spaced),                       // after hard break: kept
        ];
        (LayoutDocument layout, _, _) = Run(Doc(Page(5), blocks));
        ParagraphFragment a = Paragraphs(layout.Pages[0])[0];
        Assert.Equal(20, a.SpaceBeforeUsed);
        Assert.Equal(Margin + 20, a.ContentTop);
        Assert.Equal(4, layout.Pages[0].Body.Length);
        ParagraphFragment e = Paragraphs(layout.Pages[1])[0];
        Assert.Equal(0, e.SpaceBeforeUsed);
        ParagraphFragment g = Paragraphs(layout.Pages[2]).First(f => f.Paragraph.FlatText == "g");
        Assert.Equal(20, g.SpaceBeforeUsed);
    }

    [Fact]
    public void Space_after_is_added_between_paragraphs_and_contextual_spacing_suppresses_it_for_same_style()
    {
        var after = new ParagraphProperties { SpaceAfter = Twips.FromDips(10) };
        (LayoutDocument layout, _, _) = Run(Doc(Page(10), P("a", after), P("b", after)));
        ParagraphFragment[] frags = Paragraphs(layout.Pages[0]);
        Assert.Equal(Margin + 30, frags[1].Bounds.Top);

        var contextual = new ParagraphProperties { SpaceAfter = Twips.FromDips(10), ContextualSpacing = true };
        (LayoutDocument layout2, _, _) = Run(Doc(Page(10), P("a", contextual, "List"), P("b", contextual, "List"), P("c", contextual, "Other")));
        ParagraphFragment[] frags2 = Paragraphs(layout2.Pages[0]);
        Assert.Equal(Margin + 20, frags2[1].Bounds.Top);
        Assert.Equal(Margin + 50, frags2[2].Bounds.Top);
    }

    [Theory]
    [InlineData(LineSpacingRule.Auto, 480, 40, 36)]
    [InlineData(LineSpacingRule.Auto, 240, 20, 16)]
    [InlineData(LineSpacingRule.Exact, 450, 30, 26)]
    [InlineData(LineSpacingRule.AtLeast, 150, 20, 16)]
    [InlineData(LineSpacingRule.AtLeast, 600, 40, 36)]
    public void Line_spacing_rules_adjust_slot_height_and_baseline(LineSpacingRule rule, int value, double slot, double baseline)
    {
        (double actualSlot, double actualBaseline) = ParagraphLayout.ApplyLineSpacing(new LineSpacing(rule, value), 20, 16);
        Assert.Equal(slot, actualSlot, 6);
        Assert.Equal(baseline, actualBaseline, 6);
    }

    [Fact]
    public void Double_spacing_halves_the_lines_per_page()
    {
        var doubled = new ParagraphProperties { LineSpacing = LineSpacing.Double };
        (LayoutDocument layout, _, _) = Run(Doc(Page(6), P(Lines(5), doubled)));
        Assert.Equal(2, layout.PageCount);
        Assert.Equal((0, 2), (Paragraphs(layout.Pages[0])[0].FirstLine, Paragraphs(layout.Pages[0])[0].LastLine));
        Assert.Equal(40, Paragraphs(layout.Pages[0])[0].Layout.LineHeights[0]);
    }

    [Fact]
    public void Unchanged_paragraphs_reuse_cached_layouts_and_stale_ones_are_disposed()
    {
        Document doc = Doc(Page(5), P("one"), P("two"), P("three"));
        var cache = new LayoutCache();
        var formatter = new FakeLineFormatter();
        (LayoutDocument first, _, _) = Run(doc, cache, formatter);
        Assert.Equal(3, formatter.Calls);
        Assert.Equal(3, cache.Count);

        Document edited = doc.WithStory(StoryId.Body(0), doc.Sections[0].Body.SetItem(1, P("TWO")));
        (LayoutDocument second, _, _) = Run(edited, cache, formatter);
        Assert.Equal(4, formatter.Calls);
        Assert.Equal(3, cache.Count);
        Assert.Same(Paragraphs(first.Pages[0])[0].Layout, Paragraphs(second.Pages[0])[0].Layout);
        Assert.NotSame(Paragraphs(first.Pages[0])[1].Layout, Paragraphs(second.Pages[0])[1].Layout);
        Assert.Same(Paragraphs(first.Pages[0])[2].Layout, Paragraphs(second.Pages[0])[2].Layout);
        var staleLine = (FakeLineFormatter.FakeLine)Paragraphs(first.Pages[0])[1].Layout.Lines[0];
        Assert.True(staleLine.IsDisposed);
    }

    [Fact]
    public void Opaque_blocks_get_a_placeholder_fragment()
    {
        (LayoutDocument layout, _, _) = Run(Doc(Page(10), P("a"), new OpaqueBlock("tbl", "<w:tbl/>"), P("b")));
        BlockFragment[] body = [.. layout.Pages[0].Body];
        var opaque = Assert.IsType<OpaqueFragment>(body[1]);
        Assert.Equal(OpaqueFragment.PlaceholderHeight, opaque.Bounds.Height);
        Assert.Equal(Margin + 20 + OpaqueFragment.PlaceholderHeight, body[2].Bounds.Top);
        Assert.Contains("Table", opaque.Label);
    }

    [Fact]
    public void Indents_shrink_the_line_and_shift_its_origin()
    {
        var indented = new ParagraphProperties { LeftIndent = Twips.FromDips(20), FirstLineIndent = Twips.FromDips(10), RightIndent = Twips.FromDips(10) };
        (LayoutDocument layout, _, _) = Run(Doc(Page(10, 20), P("abcdefghi abcdefghi abcdefghi", indented)));
        ParagraphFragment fragment = Paragraphs(layout.Pages[0])[0];
        // Column 200 wide: first line gets 160 (16 chars), the rest 170 (17 chars).
        Assert.Equal(10, fragment.Layout.Lines[0].Length);
        Assert.Equal(30, fragment.Layout.LineStarts[0]);
        Assert.Equal(20, fragment.Layout.LineStarts[1]);
        Assert.Equal(Margin + 30, fragment.LineOrigin(0).X);
    }

    [Fact]
    public void Title_page_without_a_first_variant_shows_no_header_and_edits_its_own_variant()
    {
        var defaultHeader = ImmutableList.Create<Block>(P("default"));
        var section = new Section(Page(5) with { TitlePage = true }, SingleLines(6).ToImmutableList(), new HeaderFooterSet(defaultHeader, null, null));
        (LayoutDocument layout, _, _) = Run(Doc(section));
        Assert.Equal(2, layout.PageCount);
        Assert.Null(layout.Pages[0].Header);
        Assert.Equal(HeaderFooterVariant.First, layout.Pages[0].Variant);
        Assert.NotNull(layout.Pages[1].Header);
        Assert.Equal(HeaderFooterVariant.Default, layout.Pages[1].Variant);
        Assert.Equal(new StoryId(0, StoryKind.HeaderFirst), layout.Pages[0].EditableHeaderStory());
        Assert.Equal(new StoryId(0, StoryKind.HeaderDefault), layout.Pages[1].EditableHeaderStory());
        Assert.Equal(new StoryId(0, StoryKind.FooterFirst), layout.Pages[0].EditableFooterStory());
    }

    [Fact]
    public void Even_pages_use_the_even_variant_when_enabled()
    {
        var defaultHeader = ImmutableList.Create<Block>(P("default"));
        var section = new Section(Page(5), SingleLines(10).ToImmutableList(), new HeaderFooterSet(defaultHeader, null, null));
        var doc = new Document(ImmutableList.Create(section), StyleSheet.Empty, new DocumentSettings { EvenAndOddHeaders = true });
        (LayoutDocument layout, _, _) = Run(doc);
        Assert.True(layout.PageCount >= 2);
        Assert.Equal(HeaderFooterVariant.Default, layout.Pages[0].Variant);
        Assert.NotNull(layout.Pages[0].Header);
        Assert.Equal(HeaderFooterVariant.Even, layout.Pages[1].Variant);
        Assert.Null(layout.Pages[1].Header);
        Assert.Equal(new StoryId(0, StoryKind.HeaderEven), layout.Pages[1].EditableHeaderStory());
    }

    [Fact]
    public void Editing_a_linked_header_edits_the_section_that_defines_it()
    {
        var defaultHeader = ImmutableList.Create<Block>(P("linked"));
        var s1 = new Section(Page(5), ImmutableList.Create<Block>(P("one")), new HeaderFooterSet(defaultHeader, null, null));
        var s2 = new Section(Page(5), ImmutableList.Create<Block>(P("two")));
        (LayoutDocument layout, _, _) = Run(Doc(s1, s2));
        Assert.Equal(2, layout.PageCount);
        Assert.Equal(new StoryId(0, StoryKind.HeaderDefault), layout.Pages[1].EditableHeaderStory());
        Assert.Equal(new StoryId(1, StoryKind.FooterDefault), layout.Pages[1].EditableFooterStory());
    }

    [Fact]
    public void Empty_document_has_one_page_with_one_empty_line()
    {
        (LayoutDocument layout, _, _) = Run(Doc(Page(5), Paragraph.Empty()));
        ParagraphFragment fragment = Assert.Single(Paragraphs(layout.Pages[0]));
        Assert.Equal(1, fragment.LineCount);
        Assert.Equal(0, fragment.Layout.Lines[0].Length);
        Assert.True(fragment.ContainsOffset(0));
    }
}
