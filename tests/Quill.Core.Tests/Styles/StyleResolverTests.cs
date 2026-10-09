using System.Collections.Immutable;
using Quill.Core.Model;
using Quill.Core.Styles;
using Quill.Core.Units;
using Xunit;

namespace Quill.Core.Tests.Styles;

public class StyleResolverTests
{
    private static readonly StyleResolver Resolver = new(DefaultStyleSheet.Create());

    [Fact]
    public void Normal_paragraph_takes_document_defaults()
    {
        Paragraph paragraph = Paragraph.FromText("hello");
        ResolvedRunProperties run = Resolver.ResolveRun(paragraph, paragraph.Inlines[0]);
        Assert.Equal("Calibri", run.FontFamily);
        Assert.Equal(HalfPoints.FromPoints(11), run.FontSize);
        Assert.False(run.Bold);
        Assert.True(run.Color.IsAuto);

        ResolvedParagraphProperties para = Resolver.ResolveParagraph(paragraph);
        Assert.Equal(Twips.FromPoints(8), para.SpaceAfter);
        Assert.Equal(LineSpacing.Multiple(1.08), para.LineSpacing);
        Assert.True(para.WidowControl);
        Assert.Equal(Alignment.Left, para.Alignment);
    }

    [Fact]
    public void Heading_style_overrides_through_the_based_on_chain()
    {
        Paragraph heading = Paragraph.FromText("Title", DefaultStyleSheet.Heading1Id);
        ResolvedRunProperties run = Resolver.ResolveRun(heading, heading.Inlines[0]);
        Assert.Equal("Calibri Light", run.FontFamily);
        Assert.Equal(HalfPoints.FromPoints(16), run.FontSize);
        Assert.Equal("en-US", run.Language); // inherited from document defaults
        Assert.True(Resolver.ResolveParagraph(heading).KeepWithNext);
        Assert.Equal(Twips.FromPoints(12), Resolver.ResolveParagraph(heading).SpaceBefore);
    }

    [Fact]
    public void Direct_formatting_wins_over_styles()
    {
        Paragraph heading = new([new Run("x", new RunProperties { FontSize = HalfPoints.FromPoints(9), Bold = true })], DefaultStyleSheet.Heading1Id);
        ResolvedRunProperties run = Resolver.ResolveRun(heading, heading.Inlines[0]);
        Assert.Equal(HalfPoints.FromPoints(9), run.FontSize);
        Assert.True(run.Bold);
        Assert.Equal("Calibri Light", run.FontFamily);
    }

    [Fact]
    public void Character_style_sits_between_paragraph_style_and_direct_formatting()
    {
        StyleSheet sheet = DefaultStyleSheet.Create().With(new Style
        {
            Id = "Strong",
            Name = "Strong",
            Type = StyleType.Character,
            RunProperties = new RunProperties { Bold = true, Color = DocColor.Parse("FF0000") },
        });
        var resolver = new StyleResolver(sheet);
        var paragraph = new Paragraph([new Run("x", new RunProperties { Color = DocColor.Parse("0000FF") }, "Strong")]);
        ResolvedRunProperties run = resolver.ResolveRun(paragraph, paragraph.Inlines[0]);
        Assert.True(run.Bold);
        Assert.Equal(DocColor.Parse("0000FF"), run.Color);
    }

    [Fact]
    public void Unknown_style_ids_fall_back_to_defaults()
    {
        var paragraph = new Paragraph([new Run("x", styleId: "Nope")], "AlsoNope");
        ResolvedRunProperties run = Resolver.ResolveRun(paragraph, paragraph.Inlines[0]);
        Assert.Equal("Calibri", run.FontFamily);
    }

    [Fact]
    public void Next_style_after_heading_is_normal_and_otherwise_the_same()
    {
        Assert.Equal(StyleSheet.NormalStyleId, Resolver.NextStyleAfter(DefaultStyleSheet.Heading1Id));
        Assert.Null(Resolver.NextStyleAfter(null));
        Assert.Equal(DefaultStyleSheet.HeaderId, Resolver.NextStyleAfter(DefaultStyleSheet.HeaderId));
    }

    [Fact]
    public void Based_on_cycles_do_not_hang()
    {
        var a = new Style { Id = "A", Name = "A", BasedOn = "B", RunProperties = new RunProperties { Bold = true } };
        var b = new Style { Id = "B", Name = "B", BasedOn = "A", RunProperties = new RunProperties { Italic = true } };
        var sheet = new StyleSheet(
            new[] { a, b }.ToImmutableDictionary(s => s.Id),
            DocumentDefaults.Empty,
            "A",
            "DefaultParagraphFont");
        var resolver = new StyleResolver(sheet);
        ResolvedRunProperties run = resolver.ResolveRun("A", null, RunProperties.Empty);
        Assert.True(run.Bold);
        Assert.True(run.Italic);
    }

    [Fact]
    public void Results_are_cached_by_value()
    {
        var p1 = new Paragraph([new Run("a", new RunProperties { Bold = true })]);
        var p2 = new Paragraph([new Run("b", new RunProperties { Bold = true })]);
        Assert.Same(Resolver.ResolveRun(p1, p1.Inlines[0]), Resolver.ResolveRun(p2, p2.Inlines[0]));
    }

    [Fact]
    public void Header_style_defines_center_and_right_tabs()
    {
        ResolvedParagraphProperties header = Resolver.ResolveParagraph(DefaultStyleSheet.HeaderId, ParagraphProperties.Empty);
        Assert.Equal(2, header.Tabs.Count);
        Assert.Equal(TabAlignment.Center, header.Tabs[0].Alignment);
        Assert.Equal(Twips.FromInches(6.5), header.Tabs[1].Position);
        Assert.Equal(Twips.Zero, header.SpaceAfter);
    }
}
