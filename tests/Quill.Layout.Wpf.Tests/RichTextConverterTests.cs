using System.Collections.Immutable;
using Quill.Core.Editing;
using Quill.Core.Model;
using Quill.Core.Styles;
using Quill.Core.Units;
using Quill.Layout.Wpf;
using Xunit;

namespace Quill.Layout.Wpf.Tests;

/// <summary>Runs only on Windows: exercises WPF's RTF reader and writer through the converter.</summary>
public class RichTextConverterTests
{
    private static readonly StyleResolver Resolver = new(DefaultStyleSheet.Create());

    [WpfFact]
    public void Formatting_survives_an_rtf_round_trip()
    {
        var fragment = new DocumentFragment(
        [
            new Paragraph(
                [
                    new Run("Bold ", new RunProperties { Bold = true }),
                    new Run("red", new RunProperties { Color = DocColor.Parse("FF0000"), FontSize = HalfPoints.FromPoints(14) }),
                    new Break(BreakKind.Line),
                    new Run("under", new RunProperties { Underline = UnderlineStyle.Single, Italic = true }),
                ],
                properties: new ParagraphProperties { Alignment = Alignment.Center }),
            new Paragraph([new Run("second", new RunProperties { Highlight = HighlightColor.Yellow, VerticalAlignment = VerticalTextAlignment.Superscript })]),
        ]);

        string rtf = RichTextConverter.ToRtf(fragment, Resolver, ListStore.Empty);
        Assert.StartsWith("{\\rtf1", rtf, StringComparison.Ordinal);
        DocumentFragment back = RichTextConverter.FromRtf(rtf);

        Assert.Equal(2, back.Paragraphs.Length);
        Paragraph first = back.Paragraphs[0];
        Assert.Equal(Alignment.Center, first.Properties.Alignment);
        Assert.Equal("Bold red￼under", first.FlatText);
        Assert.True(first.Inlines[0].Properties.Bold);
        Assert.Equal(DocColor.Parse("FF0000"), first.Inlines[1].Properties.Color);
        Assert.Equal(HalfPoints.FromPoints(14), first.Inlines[1].Properties.FontSize);
        Inline last = first.Inlines[^1];
        Assert.True(last.Properties.Italic);
        Assert.Equal(UnderlineStyle.Single, last.Properties.Underline);
        Assert.Equal(HighlightColor.Yellow, back.Paragraphs[1].Inlines[0].Properties.Highlight);
        Assert.Equal(VerticalTextAlignment.Superscript, back.Paragraphs[1].Inlines[0].Properties.VerticalAlignment);
    }

    [WpfFact]
    public void Word_style_rtf_is_read()
    {
        const string rtf = "{\\rtf1\\ansi{\\fonttbl{\\f0 Calibri;}}{\\colortbl;\\red255\\green0\\blue0;}\\pard\\qr\\f0\\fs28\\b Hello\\b0  \\cf1 world\\par Plain\\par}";
        DocumentFragment fragment = RichTextConverter.FromRtf(rtf);
        Assert.Equal(2, fragment.Paragraphs.Length);
        Assert.Equal(Alignment.Right, fragment.Paragraphs[0].Properties.Alignment);
        Assert.Equal("Hello world", fragment.Paragraphs[0].FlatText);
        Assert.True(fragment.Paragraphs[0].Inlines[0].Properties.Bold);
        Assert.Equal(HalfPoints.FromPoints(14), fragment.Paragraphs[0].Inlines[0].Properties.FontSize);
        Assert.Equal("Calibri", fragment.Paragraphs[0].Inlines[0].Properties.FontFamily);
        Assert.Equal(DocColor.Parse("FF0000"), fragment.Paragraphs[0].Inlines[^1].Properties.Color);
        Assert.Equal("Plain", fragment.Paragraphs[1].FlatText);
    }

    [WpfFact]
    public void List_items_become_marker_prefixed_paragraphs()
    {
        (ListStore store, int id) = ListStore.Empty.AddList(DefaultLists.NumberedLevels());
        var fragment = new DocumentFragment(
        [
            new Paragraph([new Run("first")], properties: new ParagraphProperties { List = new ListFormat(id, 0) }),
            new Paragraph([new Run("second")], properties: new ParagraphProperties { List = new ListFormat(id, 0) }),
        ]);
        var resolver = new StyleResolver(DefaultStyleSheet.Create(), store);
        string rtf = RichTextConverter.ToRtf(fragment, resolver, store);
        DocumentFragment back = RichTextConverter.FromRtf(rtf);
        Assert.StartsWith("1.\t", back.Paragraphs[0].FlatText, StringComparison.Ordinal);
        Assert.StartsWith("2.\t", back.Paragraphs[1].FlatText, StringComparison.Ordinal);
    }
}
