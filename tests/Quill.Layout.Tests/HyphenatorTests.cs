using System.Collections.Immutable;
using System.Globalization;
using Quill.Core.Model;
using Quill.Core.Styles;
using Quill.Core.Units;
using Quill.Layout;
using Quill.Layout.Hyphenation;
using Xunit;

namespace Quill.Layout.Tests;

public class HyphenatorTests
{
    [Theory]
    [InlineData("hyphenation", "hy-phen-ation")]
    [InlineData("computer", "com-puter")]
    [InlineData("Supercalifragilistic", "Su-per-cal-ifrag-ilis-tic")]
    [InlineData("associate", "as-so-ciate")] // from the exception list
    [InlineData("table", "ta-ble")]
    [InlineData("the", "the")]
    [InlineData("word", "word")]
    public void English_patterns_hyphenate_like_TeX(string word, string expected)
    {
        Assert.Equal(expected, Hyphenator.English.ShowHyphens(word));
    }

    [Fact]
    public void Minimums_keep_two_letters_before_and_three_after_a_hyphen()
    {
        Hyphenator english = Hyphenator.English;
        Assert.All(english.Opportunities("hyphenation"), index => Assert.InRange(index, english.LeftMin, "hyphenation".Length - english.RightMin));
        Assert.Empty(english.Opportunities("abcd")); // shorter than 2 + 3
        Assert.True(english.PatternCount > 4000);
    }

    [Fact]
    public void Words_with_digits_or_punctuation_are_left_alone_and_text_breaks_are_offset_per_word()
    {
        Hyphenator english = Hyphenator.English;
        Assert.Empty(english.Opportunities("hyphen8ation"));
        IReadOnlyList<int> breaks = english.TextBreaks("see hyphenation, computer!");
        // "hyphenation" starts at 4 (hy|phen|ation -> 2, 6); "computer" starts at 17 (com|puter -> 3).
        Assert.Equal([6, 10, 20], breaks);
        Assert.True(english.IsCultureSupported(CultureInfo.GetCultureInfo("en-GB")));
        Assert.False(english.IsCultureSupported(CultureInfo.GetCultureInfo("de-DE")));
    }

    [Fact]
    public void Patterns_parse_from_text_and_odd_levels_win()
    {
        Hyphenator single = Hyphenator.Parse(new StringReader("# patterns\n.ab1c\n"), "xx", leftMin: 1, rightMin: 1);
        Assert.Equal("ab-cd", single.ShowHyphens("abcd"));

        var reader = new StringReader("# patterns\n.ab1c\nb2c\n# exceptions\nfoo-bar\n");
        Hyphenator custom = Hyphenator.Parse(reader, "xx", leftMin: 1, rightMin: 1);
        Assert.Equal(2, custom.PatternCount);
        Assert.Equal("foo-bar", custom.ShowHyphens("foobar"));
        Assert.Equal("abcd", custom.ShowHyphens("abcd")); // b2c (even) beats .ab1c at the same gap
    }

    [Fact]
    public void Layout_text_gets_soft_hyphens_at_the_breaks_and_offsets_still_map_both_ways()
    {
        var paragraph = new Paragraph([new Run("hyphenation "), new Run("computer", new RunProperties { AllCaps = true })]);
        var document = new Document(ImmutableList.Create(new Section(SectionProperties.Letter, ImmutableList.Create<Block>(paragraph))), StyleSheet.Empty);
        ParagraphLayoutInput input = ParagraphLayoutInput.Create(paragraph, new StyleResolver(document.Styles), FieldValues.Placeholder, 200, Twips.FromInches(0.5), 1.0);

        Assert.Equal("hyphenation computer", TextExpansion.Build(input).LayoutText.Replace("COMPUTER", "computer", StringComparison.Ordinal));

        TextExpansion expansion = TextExpansion.Build(input with { Hyphenate = true });
        Assert.Equal("hy­phen­ation COM­PUTER", expansion.LayoutText);
        Assert.Equal(20, expansion.FlatLength);
        Assert.Equal(3, expansion.ToLayout(2)); // the caret after "hy" sits after the soft hyphen
        Assert.Equal(2, expansion.ToFlat(2)); // the soft hyphen itself belongs to flat offset 2
        Assert.Equal(2, expansion.ToFlat(3));
        Assert.Equal(8, expansion.ToLayout(6));
        Assert.Equal(6, expansion.ToFlat(8));
        Assert.Equal(20, expansion.ToFlat(expansion.LayoutLength));
        Assert.Equal(expansion.LayoutLength, expansion.ToLayout(20));
        Assert.Equal(14, expansion.ToLayout(12)); // "computer" starts at flat 12 = layout 14; "COM" + soft hyphen puts flat 15 at layout 18
        Assert.Equal(18, expansion.ToLayout(15));
        Assert.Equal(RunKind.Text, expansion.RunAtLayout(2).Run.Kind);
    }

    [Fact]
    public void Paginator_asks_for_hyphenation_only_when_the_document_wants_it_and_the_paragraph_allows_it()
    {
        var page = new SectionProperties { PageWidth = Twips.FromDips(200), PageHeight = Twips.FromDips(200), MarginTop = Twips.FromDips(20), MarginBottom = Twips.FromDips(20), MarginLeft = Twips.FromDips(20), MarginRight = Twips.FromDips(20) };
        Block normal = Paragraph.FromText("hyphenation");
        var optedOut = new Paragraph([new Run("computer")], properties: new ParagraphProperties { SuppressAutoHyphens = true });
        var cache = new LayoutCache();
        var paginator = new Paginator(new FakeLineFormatter(), cache);

        var off = new Document(ImmutableList.Create(new Section(page, ImmutableList.Create(normal, optedOut))), StyleSheet.Empty);
        LayoutDocument offLayout = paginator.Layout(off, new StyleResolver(off.Styles));
        Assert.All(offLayout.Pages[0].Body.OfType<ParagraphFragment>(), f => Assert.False(f.Layout.Input.Hyphenate));

        Document on = off.WithSettings(off.Settings with { AutoHyphenation = true });
        LayoutDocument onLayout = paginator.Layout(on, new StyleResolver(on.Styles));
        List<ParagraphFragment> fragments = onLayout.Pages[0].Body.OfType<ParagraphFragment>().ToList();
        Assert.True(fragments[0].Layout.Input.Hyphenate);
        Assert.False(fragments[1].Layout.Input.Hyphenate);
        Assert.True(fragments[1].Layout.Properties.SuppressAutoHyphens);
        Assert.NotSame(offLayout.Pages[0].Body[0], onLayout.Pages[0].Body[0]); // switching the setting re-formats the paragraph
    }
}
