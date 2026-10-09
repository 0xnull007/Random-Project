using System.Collections.Immutable;
using Quill.Core.Model;
using Quill.Core.Styles;
using Quill.Core.Units;
using Quill.Layout;
using Quill.Layout.Wpf;
using Xunit;

namespace Quill.Layout.Wpf.Tests;

/// <summary>Runs only on Windows: exercises WPF's TextFormatter through the adapter.</summary>
public class WpfLineFormatterTests
{
    private static readonly StyleResolver Resolver = new(DefaultStyleSheet.Create());

    private static ParagraphLayoutInput Input(Paragraph paragraph, double width, FieldValues? fields = null) =>
        ParagraphLayoutInput.Create(paragraph, Resolver, fields ?? FieldValues.Placeholder, width, Twips.FromInches(0.5), 1.0);

    [WpfFact]
    public void Long_paragraph_wraps_into_contiguous_lines()
    {
        Paragraph paragraph = Paragraph.FromText(string.Join(" ", Enumerable.Repeat("word", 80)));
        IReadOnlyList<IFormattedLine> lines = new WpfLineFormatter().FormatParagraph(Input(paragraph, 200));
        Assert.True(lines.Count > 5);
        Assert.Equal(0, lines[0].Start);
        Assert.Equal(paragraph.Length, lines[^1].End);
        for (int i = 1; i < lines.Count; i++)
        {
            Assert.Equal(lines[i - 1].End, lines[i].Start);
            Assert.True(lines[i].Width <= 200 + 0.01);
        }

        Assert.All(lines, l => Assert.True(l.Height > 8 && l.Baseline > 0 && l.Baseline <= l.Height));
    }

    [WpfFact]
    public void Empty_paragraph_yields_one_line_with_the_mark_height()
    {
        IReadOnlyList<IFormattedLine> lines = new WpfLineFormatter().FormatParagraph(Input(Paragraph.Empty(), 300));
        IFormattedLine line = Assert.Single(lines);
        Assert.Equal(0, line.Length);
        Assert.True(line.Height > 10);
    }

    [WpfFact]
    public void Caret_and_hit_test_round_trip()
    {
        Paragraph paragraph = Paragraph.FromText("Hello world");
        IFormattedLine line = new WpfLineFormatter().FormatParagraph(Input(paragraph, 500))[0];
        double previous = -1;
        for (int offset = 0; offset <= paragraph.Length; offset++)
        {
            double x = line.GetCaretX(offset);
            Assert.True(x > previous);
            previous = x;
            Assert.Equal(offset, line.HitTest(x + 0.5));
        }

        Assert.Equal(1, line.NextCaretOffset(0));
        Assert.Equal(0, line.PreviousCaretOffset(1));
    }

    [WpfFact]
    public void Fields_render_their_values_but_keep_model_offsets()
    {
        var paragraph = new Paragraph([new Run("Page "), Field.Page(), new Run(" of "), Field.NumPages()]);
        IFormattedLine line = new WpfLineFormatter().FormatParagraph(Input(paragraph, 500, new FieldValues("12", "345", "1")))[0];
        Assert.Equal(paragraph.Length, line.End);
        double before = line.GetCaretX(5);
        double after = line.GetCaretX(6);
        Assert.True(after - before > 8, "a two-digit page number should be wider than one character");
        Assert.Equal(5, line.HitTest(before + 1));
        Assert.Equal(6, line.HitTest(after - 1));
    }

    [WpfFact]
    public void Breaks_end_lines_and_page_breaks_are_flagged()
    {
        var paragraph = new Paragraph([new Run("ab"), new Break(BreakKind.Line), new Run("cd"), new Break(BreakKind.Page), new Run("ef")]);
        IReadOnlyList<IFormattedLine> lines = new WpfLineFormatter().FormatParagraph(Input(paragraph, 500));
        Assert.Equal(3, lines.Count);
        Assert.Equal(1, lines[0].NewlineLength);
        Assert.Null(lines[0].ForcedBreakAfter);
        Assert.Equal(BreakKind.Page, lines[1].ForcedBreakAfter);
        Assert.Equal(6, lines[2].Start);
        Assert.Equal(8, lines[2].End);
    }

    [WpfFact]
    public void Trailing_page_break_adds_an_empty_last_line()
    {
        var paragraph = new Paragraph([new Run("ab"), new Break(BreakKind.Page)]);
        IReadOnlyList<IFormattedLine> lines = new WpfLineFormatter().FormatParagraph(Input(paragraph, 500));
        Assert.Equal(2, lines.Count);
        Assert.Equal(3, lines[1].Start);
        Assert.Equal(0, lines[1].Length);
    }

    [WpfFact]
    public void Bold_and_larger_text_measure_wider_and_taller()
    {
        IFormattedLine regular = new WpfLineFormatter().FormatParagraph(Input(Paragraph.FromText("Measure me"), 500))[0];
        IFormattedLine bold = new WpfLineFormatter().FormatParagraph(Input(Paragraph.FromText("Measure me", runProperties: new RunProperties { Bold = true }), 500))[0];
        IFormattedLine big = new WpfLineFormatter().FormatParagraph(Input(Paragraph.FromText("Measure me", runProperties: new RunProperties { FontSize = HalfPoints.FromPoints(24) }), 500))[0];
        Assert.True(bold.Width > regular.Width);
        Assert.True(big.Height > regular.Height);
    }

    [WpfFact]
    public void Segments_cover_each_word_with_increasing_positions()
    {
        var paragraph = new Paragraph([new Run("Hello big "), Field.Page(), new Run(" world", new RunProperties { Bold = true })]);
        IFormattedLine line = new WpfLineFormatter().FormatParagraph(Input(paragraph, 500, new FieldValues("12", "3", "1")))[0];
        List<TextSegment> segments = line.GetSegments().ToList();
        Assert.Equal(["Hello", "big", "12", "world"], segments.Select(s => s.Text));
        for (int i = 1; i < segments.Count; i++)
        {
            Assert.True(segments[i].X > segments[i - 1].X);
        }

        Assert.True(segments[^1].Properties.Bold);
        Assert.All(segments, s => Assert.True(s.Width > 0 && s.Height > 0));
        Assert.Equal(line.Baseline, segments[0].Baseline);
    }

    [WpfFact]
    public void Justified_text_keeps_word_positions_in_segments()
    {
        string text = string.Join(" ", Enumerable.Repeat("word", 30));
        var paragraph = new Paragraph([new Run(text)], properties: new ParagraphProperties { Alignment = Alignment.Justify });
        IReadOnlyList<IFormattedLine> lines = new WpfLineFormatter().FormatParagraph(Input(paragraph, 200));
        Assert.True(lines.Count > 1);
        List<TextSegment> first = lines[0].GetSegments().ToList();
        Assert.True(first.Count > 2);
        double lastRight = first[^1].X + first[^1].Width;
        Assert.True(lastRight > 190, "a justified line should reach the right edge");
    }

    [WpfFact]
    public void Paginator_produces_letter_pages_with_real_text_metrics()
    {
        string text = string.Join(" ", Enumerable.Repeat("The quick brown fox jumps over the lazy dog.", 400));
        Document document = Document.CreateNew().WithStory(Core.Text.StoryId.Body(0), ImmutableList.Create<Block>(Paragraph.FromText(text)));
        using var cache = new LayoutCache();
        LayoutDocument layout = new Paginator(new WpfLineFormatter(), cache).Layout(document, new StyleResolver(document.Styles));
        Assert.True(layout.PageCount >= 3);
        Assert.Equal(816, layout.Pages[0].Size.Width, 3);
        Assert.Equal(1056, layout.Pages[0].Size.Height, 3);
        Assert.All(layout.Pages, p => Assert.NotEmpty(p.Body));
    }
}
