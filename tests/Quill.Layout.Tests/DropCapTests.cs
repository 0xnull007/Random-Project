using System.Collections.Immutable;
using Quill.Core.Model;
using Quill.Core.Styles;
using Quill.Core.Units;
using Quill.Layout;
using Xunit;

namespace Quill.Layout.Tests;

/// <summary>Drop caps with the fake formatter: every character is 10 wide, so the letter is a 10-wide obstacle. Column 100 wide at x=20.</summary>
public class DropCapTests
{
    private static SectionProperties Page() => new()
    {
        PageWidth = Twips.FromDips(140),
        PageHeight = Twips.FromDips(400),
        MarginTop = Twips.FromDips(20),
        MarginBottom = Twips.FromDips(20),
        MarginLeft = Twips.FromDips(20),
        MarginRight = Twips.FromDips(20),
    };

    private static ParagraphFragment Layout(Paragraph paragraph)
    {
        var document = new Document(ImmutableList.Create(new Section(Page(), ImmutableList.Create<Block>(paragraph))), StyleSheet.Empty);
        LayoutDocument layout = new Paginator(new FakeLineFormatter(), new LayoutCache()).Layout(document, new StyleResolver(document.Styles));
        return layout.Pages[0].Body.OfType<ParagraphFragment>().Single();
    }

    [Fact]
    public void Dropped_letter_leaves_the_flow_and_the_first_lines_start_after_it()
    {
        var paragraph = new Paragraph([new Run("Once upon a time there was a fox")], properties: new ParagraphProperties { DropCap = DropCap.Dropped(2) });
        ParagraphFragment fragment = Layout(paragraph);
        ParagraphLayout layout = fragment.Layout;

        Assert.NotNull(layout.DropCap);
        Assert.Equal(0, layout.DropCap!.X);
        Assert.Equal(RunKind.Hidden, layout.Input.Runs[0].Kind);
        Assert.Equal(1, layout.Input.Runs[0].Length);
        Assert.Equal(RunKind.Text, layout.Input.Runs[1].Kind);
        Assert.Equal(1, layout.Input.Runs[1].Start);
        Assert.Equal("Once upon a time there was a fox".Length, layout.Input.Runs[1].End);

        FlowObstacle obstacle = Assert.Single(layout.Input.Obstacles);
        Assert.Equal(0, obstacle.Bounds.X);
        Assert.Equal(10, obstacle.Bounds.Width);
        Assert.False(obstacle.PushBelow);

        // Lines 0 and 1 (tops 0 and 20) overlap the two-line obstacle and start after it; later lines use the full width.
        Assert.Equal(10, layout.LineStarts[0]);
        Assert.Equal(10, layout.LineStarts[1]);
        Assert.Equal(0, layout.LineStarts[2]);
        Assert.Equal(layout.LineTops[1] + layout.LineBaselines[1] - layout.DropCap.Line.Baseline, layout.DropCap.Top);
        Assert.True(layout.DropCap.Line.Height > FakeLineFormatter.LineHeight - 1);
    }

    [Fact]
    public void In_margin_letter_sits_left_of_the_text_without_narrowing_lines()
    {
        var paragraph = new Paragraph([new Run("Margin letter here")], properties: new ParagraphProperties { DropCap = DropCap.InMargin(3), LeftIndent = Twips.FromDips(30) });
        ParagraphLayout layout = Layout(paragraph).Layout;
        Assert.NotNull(layout.DropCap);
        Assert.Equal(20, layout.DropCap!.X); // left indent 30 minus the 10-wide letter
        Assert.Empty(layout.Input.Obstacles);
        Assert.All(layout.LineStarts, start => Assert.Equal(30, start));
    }

    [Fact]
    public void Paragraphs_that_do_not_start_with_a_letter_get_no_drop_cap()
    {
        Assert.Null(Layout(new Paragraph([new Run(" leading space")], properties: new ParagraphProperties { DropCap = DropCap.Dropped() })).Layout.DropCap);
        Assert.Null(Layout(new Paragraph([new Run("plain")], properties: new ParagraphProperties { DropCap = DropCap.None })).Layout.DropCap);
        Assert.Null(Layout(Paragraph.Empty().WithProperties(new ParagraphProperties { DropCap = DropCap.Dropped() })).Layout.DropCap);
    }
}
