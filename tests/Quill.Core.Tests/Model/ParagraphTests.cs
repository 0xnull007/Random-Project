using System.Collections.Immutable;
using Quill.Core.Model;
using Quill.Core.Units;
using Xunit;

namespace Quill.Core.Tests.Model;

public class ParagraphTests
{
    private static readonly RunProperties Bold = new() { Bold = true };

    [Fact]
    public void FlatText_uses_replacement_char_for_non_text_inlines()
    {
        var paragraph = new Paragraph([new Run("ab"), new Break(BreakKind.Line), Field.Page(), new Run("c")]);
        Assert.Equal("ab￼￼c", paragraph.FlatText);
        Assert.Equal(5, paragraph.Length);
        Assert.Equal([0, 2, 3, 4], paragraph.Spans().Select(s => s.Start).ToArray());
    }

    [Fact]
    public void TryGetInlineAt_resolves_boundaries_to_the_following_inline()
    {
        var paragraph = new Paragraph([new Run("ab"), new Run("cd", Bold)]);
        Assert.True(paragraph.TryGetInlineAt(1, out InlineSpan span));
        Assert.Equal(0, span.Index);
        Assert.True(paragraph.TryGetInlineAt(2, out span));
        Assert.Equal(1, span.Index);
        Assert.False(paragraph.TryGetInlineAt(4, out _));
    }

    [Fact]
    public void Typing_format_comes_from_the_character_before_the_caret()
    {
        var paragraph = new Paragraph([new Run("ab"), new Run("cd", Bold)]);
        Assert.Equal(RunProperties.Empty, paragraph.GetTypingFormatAt(2).Properties);
        Assert.Equal(Bold, paragraph.GetTypingFormatAt(3).Properties);
        Assert.Equal(Bold, paragraph.GetTypingFormatAt(4).Properties);
        Assert.Equal(RunProperties.Empty, paragraph.GetTypingFormatAt(0).Properties);
    }

    [Fact]
    public void Empty_paragraph_types_with_mark_properties()
    {
        var paragraph = new Paragraph(ImmutableArray<Inline>.Empty, markProperties: Bold);
        Assert.Equal(Bold, paragraph.GetTypingFormatAt(0).Properties);
    }

    [Fact]
    public void With_methods_return_new_instances_and_keep_the_rest()
    {
        Paragraph original = Paragraph.FromText("x", "Heading1");
        Paragraph styled = original.WithStyle("Normal");
        Assert.NotSame(original, styled);
        Assert.Equal("Heading1", original.StyleId);
        Assert.Equal("Normal", styled.StyleId);
        Assert.Equal(original.FlatText, styled.FlatText);
    }

    [Fact]
    public void Run_properties_merge_overrides_only_specified_values()
    {
        var baseProps = new RunProperties { FontFamily = "Calibri", Bold = true, FontSize = HalfPoints.FromPoints(11) };
        var delta = new RunProperties { Bold = false, Italic = true };
        RunProperties merged = baseProps.Merge(delta);
        Assert.Equal("Calibri", merged.FontFamily);
        Assert.False(merged.Bold);
        Assert.True(merged.Italic);
        Assert.Equal(HalfPoints.FromPoints(11), merged.FontSize);
        Assert.Same(baseProps, baseProps.Merge(null));
        Assert.Same(baseProps, baseProps.Merge(RunProperties.Empty));
    }

    [Fact]
    public void Tab_stops_merge_and_clear()
    {
        TabStops fromStyle = TabStops.Create(new TabStop(new Twips(720)), new TabStop(new Twips(1440), TabAlignment.Right));
        TabStops direct = TabStops.Create(new TabStop(new Twips(720), IsCleared: true), new TabStop(new Twips(2160), TabAlignment.Center));
        TabStops merged = fromStyle.Merge(direct).WithoutCleared();
        Assert.Equal([1440, 2160], merged.Select(t => t.Position.Value).ToArray());
        Assert.Equal(TabAlignment.Right, merged[0].Alignment);
        Assert.Equal(TabStops.Create(new TabStop(new Twips(1440), TabAlignment.Right), new TabStop(new Twips(2160), TabAlignment.Center)), merged);
    }

    [Fact]
    public void Paragraph_properties_merge_tabs_along_the_chain()
    {
        var style = new ParagraphProperties { Tabs = TabStops.Create(new TabStop(new Twips(720))), SpaceAfter = new Twips(160) };
        var direct = new ParagraphProperties { Tabs = TabStops.Create(new TabStop(new Twips(1440))), Alignment = Alignment.Center };
        ParagraphProperties merged = style.Merge(direct);
        Assert.Equal(2, merged.Tabs!.Count);
        Assert.Equal(new Twips(160), merged.SpaceAfter);
        Assert.Equal(Alignment.Center, merged.Alignment);
    }
}
