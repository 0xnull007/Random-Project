using System.Collections.Immutable;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using Quill.Core.Editing;
using Quill.Core.Model;
using Quill.Core.Styles;
using Quill.Core.Units;
using Block = System.Windows.Documents.Block;
using Inline = Quill.Core.Model.Inline;
using Section = System.Windows.Documents.Section;

namespace Quill.Layout.Wpf;

/// <summary>
/// Converts between document fragments and WPF FlowDocuments so the clipboard can carry RTF: WPF's own RTF
/// reader and writer do the parsing, and this maps the FlowDocument's inline and paragraph properties onto
/// the model (bold, italic, underline, strikethrough, font, size, color, highlight, super/subscript,
/// alignment, indents, spacing, line breaks, list items as marker text).
/// </summary>
public static class RichTextConverter
{
    private const double PointsPerDip = 72.0 / 96.0;

    public static string ToRtf(DocumentFragment fragment, StyleResolver resolver, ListStore lists)
    {
        FlowDocument flow = ToFlowDocument(fragment, resolver, lists);
        var range = new TextRange(flow.ContentStart, flow.ContentEnd);
        using var stream = new MemoryStream();
        range.Save(stream, DataFormats.Rtf);
        return Encoding.Latin1.GetString(stream.ToArray());
    }

    public static DocumentFragment FromRtf(string rtf)
    {
        ArgumentNullException.ThrowIfNull(rtf);
        var flow = new FlowDocument();
        var range = new TextRange(flow.ContentStart, flow.ContentEnd);
        using var stream = new MemoryStream(Encoding.Latin1.GetBytes(rtf));
        range.Load(stream, DataFormats.Rtf);
        return FromFlowDocument(flow);
    }

    public static FlowDocument ToFlowDocument(DocumentFragment fragment, StyleResolver resolver, ListStore lists)
    {
        ArgumentNullException.ThrowIfNull(fragment);
        ArgumentNullException.ThrowIfNull(resolver);
        ArgumentNullException.ThrowIfNull(lists);
        var flow = new FlowDocument();
        IReadOnlyDictionary<int, ListMarker> markers = ListNumbering.Compute(fragment.Paragraphs.Cast<Core.Model.Block>().ToImmutableList(), lists, resolver);
        for (int i = 0; i < fragment.Paragraphs.Length; i++)
        {
            Core.Model.Paragraph source = fragment.Paragraphs[i];
            ResolvedParagraphProperties pp = resolver.ResolveParagraph(source);
            var paragraph = new System.Windows.Documents.Paragraph
            {
                TextAlignment = pp.Alignment switch
                {
                    Alignment.Center => TextAlignment.Center,
                    Alignment.Right => TextAlignment.Right,
                    Alignment.Justify => TextAlignment.Justify,
                    _ => TextAlignment.Left,
                },
                Margin = new Thickness(pp.LeftIndent.ToDips(), pp.SpaceBefore.ToDips(), pp.RightIndent.ToDips(), pp.SpaceAfter.ToDips()),
                TextIndent = pp.FirstLineIndent.ToDips(),
            };
            if (pp.LineSpacing.Rule == LineSpacingRule.Exact)
            {
                paragraph.LineHeight = Math.Max(1, pp.LineSpacing.Height.ToDips());
            }

            if (markers.TryGetValue(i, out ListMarker marker))
            {
                var markerRun = new System.Windows.Documents.Run(marker.Text + "\t");
                Apply(markerRun, resolver.ResolveParagraphMark(source));
                paragraph.Inlines.Add(markerRun);
            }

            foreach (Inline inline in source.Inlines)
            {
                switch (inline)
                {
                    case Core.Model.Run run:
                    {
                        var target = new System.Windows.Documents.Run(run.Text);
                        Apply(target, resolver.ResolveRun(source, run));
                        if (run.Properties.Link is { } url && Uri.TryCreate(url, UriKind.Absolute, out Uri? uri))
                        {
                            paragraph.Inlines.Add(new Hyperlink(target) { NavigateUri = uri });
                        }
                        else
                        {
                            paragraph.Inlines.Add(target);
                        }

                        break;
                    }


                    case Break:
                        paragraph.Inlines.Add(new LineBreak());
                        break;
                    case Field field:
                    {
                        var target = new System.Windows.Documents.Run(field.CachedResult);
                        Apply(target, resolver.ResolveRun(source, field));
                        paragraph.Inlines.Add(target);
                        break;
                    }
                }
            }

            flow.Blocks.Add(paragraph);
        }

        return flow;
    }

    public static DocumentFragment FromFlowDocument(FlowDocument flow)
    {
        ArgumentNullException.ThrowIfNull(flow);
        var paragraphs = ImmutableArray.CreateBuilder<Core.Model.Paragraph>();
        foreach (Block block in flow.Blocks)
        {
            Collect(block, paragraphs, null);
        }

        return new DocumentFragment(paragraphs.ToImmutable());
    }

    private static void Apply(System.Windows.Documents.Run run, ResolvedRunProperties p)
    {
        run.FontFamily = FontCatalog.Shared.GetFamily(p.FontFamily);
        run.FontSize = Math.Max(1, p.FontSize.ToDips());
        run.FontWeight = p.Bold ? FontWeights.Bold : FontWeights.Normal;
        run.FontStyle = p.Italic ? FontStyles.Italic : FontStyles.Normal;
        var decorations = new TextDecorationCollection();
        if (p.Underline != UnderlineStyle.None)
        {
            decorations.Add(TextDecorations.Underline);
        }

        if (p.Strikethrough || p.DoubleStrikethrough)
        {
            decorations.Add(TextDecorations.Strikethrough);
        }

        run.TextDecorations = decorations;
        run.Foreground = FontCatalog.Shared.GetBrush(p.Color);
        if (FontCatalog.Shared.GetHighlightBrush(p.Highlight) is { } highlight)
        {
            run.Background = highlight;
        }

        run.BaselineAlignment = p.VerticalAlignment switch
        {
            VerticalTextAlignment.Superscript => BaselineAlignment.Superscript,
            VerticalTextAlignment.Subscript => BaselineAlignment.Subscript,
            _ => BaselineAlignment.Baseline,
        };
    }

    private static void Collect(Block block, ImmutableArray<Core.Model.Paragraph>.Builder paragraphs, string? markerPrefix)
    {
        switch (block)
        {
            case System.Windows.Documents.Paragraph paragraph:
                paragraphs.Add(Convert(paragraph, markerPrefix));
                break;
            case Section section:
                foreach (Block child in section.Blocks)
                {
                    Collect(child, paragraphs, markerPrefix);
                }

                break;
            case List list:
            {
                int index = list.StartIndex;
                foreach (ListItem item in list.ListItems)
                {
                    string prefix = MarkerText(list.MarkerStyle, index++) + "\t";
                    bool first = true;
                    foreach (Block child in item.Blocks)
                    {
                        Collect(child, paragraphs, first ? prefix : null);
                        first = false;
                    }
                }

                break;
            }

            case Table table:
                foreach (TableRowGroup group in table.RowGroups)
                {
                    foreach (TableRow row in group.Rows)
                    {
                        foreach (TableCell cell in row.Cells)
                        {
                            foreach (Block child in cell.Blocks)
                            {
                                Collect(child, paragraphs, null);
                            }
                        }
                    }
                }

                break;
        }
    }

    private static string MarkerText(TextMarkerStyle style, int index) => style switch
    {
        TextMarkerStyle.Disc => "•",
        TextMarkerStyle.Circle => "o",
        TextMarkerStyle.Square or TextMarkerStyle.Box => "▪",
        TextMarkerStyle.Decimal => NumberText.Format(index, NumberFormat.Decimal) + ".",
        TextMarkerStyle.LowerLatin => NumberText.Format(index, NumberFormat.LowerLetter) + ".",
        TextMarkerStyle.UpperLatin => NumberText.Format(index, NumberFormat.UpperLetter) + ".",
        TextMarkerStyle.LowerRoman => NumberText.Format(index, NumberFormat.LowerRoman) + ".",
        TextMarkerStyle.UpperRoman => NumberText.Format(index, NumberFormat.UpperRoman) + ".",
        _ => string.Empty,
    };

    private static Core.Model.Paragraph Convert(System.Windows.Documents.Paragraph paragraph, string? markerPrefix)
    {
        var properties = new ParagraphProperties
        {
            Alignment = paragraph.TextAlignment switch
            {
                TextAlignment.Center => Alignment.Center,
                TextAlignment.Right => Alignment.Right,
                TextAlignment.Justify => Alignment.Justify,
                _ => Alignment.Left,
            },
        };
        if (paragraph.Margin.Left > 0)
        {
            properties = properties with { LeftIndent = Twips.FromDips(paragraph.Margin.Left) };
        }

        if (paragraph.Margin.Right > 0)
        {
            properties = properties with { RightIndent = Twips.FromDips(paragraph.Margin.Right) };
        }

        if (paragraph.Margin.Top > 0)
        {
            properties = properties with { SpaceBefore = Twips.FromDips(paragraph.Margin.Top) };
        }

        if (paragraph.Margin.Bottom > 0)
        {
            properties = properties with { SpaceAfter = Twips.FromDips(paragraph.Margin.Bottom) };
        }

        if (Math.Abs(paragraph.TextIndent) > 0.01)
        {
            properties = properties with { FirstLineIndent = Twips.FromDips(paragraph.TextIndent) };
        }

        var inlines = ImmutableArray.CreateBuilder<Inline>();
        CollectInlines(paragraph.Inlines, inlines);
        if (markerPrefix is not null)
        {
            RunProperties markerProperties = inlines.Count > 0 ? inlines[0].Properties : RunProperties.Empty;
            inlines.Insert(0, new Core.Model.Run(markerPrefix, markerProperties));
            properties = properties with
            {
                LeftIndent = Twips.Max(properties.LeftIndent ?? Twips.Zero, Twips.FromInches(0.5)),
                FirstLineIndent = -Twips.FromInches(0.25),
            };
        }

        return new Core.Model.Paragraph(inlines.ToImmutable(), null, properties);
    }

    private static void CollectInlines(InlineCollection source, ImmutableArray<Inline>.Builder inlines)
    {
        foreach (System.Windows.Documents.Inline inline in source)
        {
            switch (inline)
            {
                case System.Windows.Documents.Run run when run.Text.Length > 0:
                    inlines.Add(new Core.Model.Run(run.Text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\n', ' ').Replace('\r', ' '), RunPropertiesOf(run)));
                    break;
                case LineBreak:
                    inlines.Add(new Break(BreakKind.Line));
                    break;
                case Hyperlink hyperlink:
                {
                    var inner = ImmutableArray.CreateBuilder<Inline>();
                    CollectInlines(hyperlink.Inlines, inner);
                    string? url = hyperlink.NavigateUri?.OriginalString;
                    foreach (Inline child in inner)
                    {
                        inlines.Add(url is null ? child : child.WithProperties(child.Properties with { Link = url }));
                    }

                    break;
                }

                case Span span:
                    CollectInlines(span.Inlines, inlines);
                    break;

            }
        }
    }

    private static RunProperties RunPropertiesOf(System.Windows.Documents.Run run)
    {
        bool underline = false;
        bool strike = false;
        if (run.TextDecorations is not null)
        {
            foreach (TextDecoration decoration in run.TextDecorations)
            {
                underline |= decoration.Location == TextDecorationLocation.Underline;
                strike |= decoration.Location == TextDecorationLocation.Strikethrough;
            }
        }

        DocColor? color = null;
        if (run.Foreground is SolidColorBrush foreground && foreground.Color.A > 0)
        {
            color = DocColor.FromRgb(foreground.Color.R, foreground.Color.G, foreground.Color.B);
        }

        HighlightColor? highlight = null;
        if (run.Background is SolidColorBrush background && background.Color.A > 0)
        {
            highlight = NearestHighlight(background.Color);
        }

        return new RunProperties
        {
            FontFamily = run.FontFamily?.Source,
            FontSize = HalfPoints.FromPoints(run.FontSize * PointsPerDip),
            Bold = run.FontWeight.ToOpenTypeWeight() >= 600,
            Italic = run.FontStyle != FontStyles.Normal,
            Underline = underline ? UnderlineStyle.Single : UnderlineStyle.None,
            Strikethrough = strike,
            Color = color,
            Highlight = highlight,
            VerticalAlignment = run.BaselineAlignment switch
            {
                BaselineAlignment.Superscript or BaselineAlignment.Top => VerticalTextAlignment.Superscript,
                BaselineAlignment.Subscript or BaselineAlignment.Bottom => VerticalTextAlignment.Subscript,
                _ => VerticalTextAlignment.Baseline,
            },
        };
    }

    private static HighlightColor NearestHighlight(Color color)
    {
        HighlightColor best = HighlightColor.Yellow;
        double bestDistance = double.MaxValue;
        foreach (HighlightColor candidate in Enum.GetValues<HighlightColor>())
        {
            if (candidate == HighlightColor.None || FontCatalog.Shared.GetHighlightBrush(candidate) is not { } brush)
            {
                continue;
            }

            Color c = brush.Color;
            double distance = Math.Pow(c.R - color.R, 2) + Math.Pow(c.G - color.G, 2) + Math.Pow(c.B - color.B, 2);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = candidate;
            }
        }

        return best;
    }
}
