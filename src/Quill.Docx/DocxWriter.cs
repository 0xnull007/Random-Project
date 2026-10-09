using System.Collections.Immutable;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using Quill.Core.Model;
using Quill.Core.Text;
using Quill.Core.Units;
using W = DocumentFormat.OpenXml.Wordprocessing;
using static Quill.Docx.OoxmlValues;

namespace Quill.Docx;

/// <summary>
/// Writes the document model as a fresh .docx package. Child elements are emitted in schema order, which the
/// Open XML SDK does not enforce but Word requires.
/// </summary>
public static class DocxWriter
{
    public static void Write(Document document, Stream stream)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(stream);

        using WordprocessingDocument package = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document);
        MainDocumentPart main = package.AddMainDocumentPart();
        var body = new W.Body();
        main.Document = new W.Document(body);

        StyleDefinitionsPart stylesPart = main.AddNewPart<StyleDefinitionsPart>();
        stylesPart.Styles = BuildStyles(document.Styles);

        if (!document.Lists.IsEmpty)
        {
            NumberingDefinitionsPart numberingPart = main.AddNewPart<NumberingDefinitionsPart>();
            numberingPart.Numbering = BuildNumbering(document.Lists);
        }

        DocumentSettingsPart settingsPart = main.AddNewPart<DocumentSettingsPart>();
        var settings = new W.Settings(new W.DefaultTabStop { Val = (short)Math.Clamp(document.Settings.DefaultTabStop.Value, 1, short.MaxValue) });
        if (document.Settings.EvenAndOddHeaders)
        {
            settings.Append(new W.EvenAndOddHeaders());
        }

        settingsPart.Settings = settings;

        for (int s = 0; s < document.Sections.Count; s++)
        {
            Section section = document.Sections[s];
            W.SectionProperties sectPr = BuildSectionProperties(main, section, s);
            bool last = s == document.Sections.Count - 1;

            var blocks = new List<OpenXmlElement>();
            foreach (Block block in section.Body)
            {
                blocks.Add(BuildBlock(main, block));
            }

            if (last)
            {
                body.Append(blocks);
                body.Append(sectPr);
            }
            else
            {
                // A non-final section's properties live in the paragraph properties of its last paragraph.
                if (blocks.Count == 0 || blocks[^1] is not W.Paragraph)
                {
                    blocks.Add(new W.Paragraph());
                }

                var lastParagraph = (W.Paragraph)blocks[^1];
                W.ParagraphProperties pPr = lastParagraph.ParagraphProperties ?? lastParagraph.PrependChild(new W.ParagraphProperties());
                pPr.Append(sectPr);
                body.Append(blocks);
            }
        }

        package.PackageProperties.Title = document.Metadata.Title;
        package.PackageProperties.Creator = document.Metadata.Author;
        package.PackageProperties.Subject = document.Metadata.Subject;
        package.PackageProperties.Description = document.Metadata.Description;
        package.PackageProperties.Created = document.Metadata.Created?.UtcDateTime;
        package.PackageProperties.Modified = (document.Metadata.Modified ?? DateTimeOffset.UtcNow).UtcDateTime;
        main.Document.Save();
    }

    public static byte[] ToBytes(Document document)
    {
        using var memory = new MemoryStream();
        Write(document, memory);
        return memory.ToArray();
    }

    /// <summary>Writes atomically: to a temporary file in the same directory, then replaces the target.</summary>
    public static void WriteFile(Document document, string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        string directory = Path.GetDirectoryName(Path.GetFullPath(path)) ?? ".";
        string temp = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
            {
                Write(document, stream);
            }

            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp))
            {
                File.Delete(temp);
            }
        }
    }

    // ------------------------------------------------------------------ blocks

    private static OpenXmlElement BuildBlock(MainDocumentPart main, Block block) => block switch
    {
        Paragraph paragraph => BuildParagraph(paragraph),
        OpaqueBlock opaque => BuildOpaque(main, opaque),
        _ => throw new NotSupportedException($"Unknown block type {block.GetType().Name}."),
    };

    private static OpenXmlElement BuildOpaque(MainDocumentPart main, OpaqueBlock opaque)
    {
        try
        {
            return opaque.LocalName == "tbl" ? new W.Table(opaque.OuterXml) : main.CreateUnknownElement(opaque.OuterXml);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.Xml.XmlException or ArgumentException)
        {
            return new W.Paragraph();
        }
    }

    private static W.Paragraph BuildParagraph(Paragraph paragraph)
    {
        var result = new W.Paragraph();
        W.ParagraphProperties? pPr = BuildParagraphProperties(paragraph);
        if (pPr is not null)
        {
            result.Append(pPr);
        }

        foreach (Inline inline in paragraph.Inlines)
        {
            switch (inline)
            {
                case Run run:
                    result.Append(BuildRun(run));
                    break;
                case Break br:
                {
                    var element = new W.Run();
                    AppendRunProperties(element, br.Properties, br.StyleId);
                    element.Append(new W.Break
                    {
                        Type = br.Kind switch
                        {
                            BreakKind.Page => W.BreakValues.Page,
                            BreakKind.Column => W.BreakValues.Column,
                            _ => W.BreakValues.TextWrapping,
                        },
                    });
                    result.Append(element);
                    break;
                }

                case Field field:
                {
                    var simple = new W.SimpleField { Instruction = " " + field.Instruction + " " };
                    var element = new W.Run();
                    AppendRunProperties(element, field.Properties, field.StyleId);
                    element.Append(new W.Text(field.CachedResult) { Space = SpaceProcessingModeValues.Preserve });
                    simple.Append(element);
                    result.Append(simple);
                    break;
                }
            }
        }

        return result;
    }

    private static W.Run BuildRun(Run run)
    {
        var element = new W.Run();
        AppendRunProperties(element, run.Properties, run.StyleId);
        string[] parts = run.Text.Split('\t');
        for (int i = 0; i < parts.Length; i++)
        {
            if (i > 0)
            {
                element.Append(new W.TabChar());
            }

            if (parts[i].Length > 0)
            {
                element.Append(new W.Text(parts[i]) { Space = SpaceProcessingModeValues.Preserve });
            }
        }

        return element;
    }

    private static void AppendRunProperties(W.Run element, RunProperties properties, string? styleId)
    {
        var rPr = new W.RunProperties();
        FillRunProperties(rPr, properties, styleId);
        if (rPr.HasChildren)
        {
            element.Append(rPr);
        }
    }

    // ------------------------------------------------------------------ properties (schema order matters)

    private static void FillRunProperties(OpenXmlCompositeElement rPr, RunProperties p, string? styleId)
    {
        if (styleId is not null)
        {
            rPr.Append(new W.RunStyle { Val = styleId });
        }

        if (p.FontFamily is not null)
        {
            rPr.Append(new W.RunFonts { Ascii = p.FontFamily, HighAnsi = p.FontFamily, ComplexScript = p.FontFamily });
        }

        if (p.Bold is { } bold)
        {
            rPr.Append(new W.Bold { Val = OnOffValue.FromBoolean(bold) });
            rPr.Append(new W.BoldComplexScript { Val = OnOffValue.FromBoolean(bold) });
        }

        if (p.Italic is { } italic)
        {
            rPr.Append(new W.Italic { Val = OnOffValue.FromBoolean(italic) });
            rPr.Append(new W.ItalicComplexScript { Val = OnOffValue.FromBoolean(italic) });
        }

        if (p.AllCaps is { } caps)
        {
            rPr.Append(new W.Caps { Val = OnOffValue.FromBoolean(caps) });
        }

        if (p.SmallCaps is { } smallCaps)
        {
            rPr.Append(new W.SmallCaps { Val = OnOffValue.FromBoolean(smallCaps) });
        }

        if (p.Strikethrough is { } strike)
        {
            rPr.Append(new W.Strike { Val = OnOffValue.FromBoolean(strike) });
        }

        if (p.DoubleStrikethrough is { } dstrike)
        {
            rPr.Append(new W.DoubleStrike { Val = OnOffValue.FromBoolean(dstrike) });
        }

        if (p.Hidden is { } hidden)
        {
            rPr.Append(new W.Vanish { Val = OnOffValue.FromBoolean(hidden) });
        }

        if (p.Color is { } color)
        {
            rPr.Append(new W.Color { Val = color.IsAuto ? "auto" : color.ToHex() });
        }

        if (p.FontSize is { } size)
        {
            rPr.Append(new W.FontSize { Val = Inv(size.Value) });
            rPr.Append(new W.FontSizeComplexScript { Val = Inv(size.Value) });
        }

        if (p.Highlight is { } highlight)
        {
            rPr.Append(new W.Highlight { Val = HighlightTo(highlight) });
        }

        if (p.Underline is { } underline)
        {
            rPr.Append(new W.Underline { Val = UnderlineTo(underline) });
        }

        if (p.VerticalAlignment is { } vertical)
        {
            rPr.Append(new W.VerticalTextAlignment { Val = VerticalTo(vertical) });
        }

        if (p.Language is not null)
        {
            rPr.Append(new W.Languages { Val = p.Language });
        }
    }

    private static W.ParagraphProperties? BuildParagraphProperties(Paragraph paragraph)
    {
        var pPr = new W.ParagraphProperties();
        if (paragraph.StyleId is not null)
        {
            pPr.Append(new W.ParagraphStyleId { Val = paragraph.StyleId });
        }

        FillParagraphProperties(pPr, paragraph.Properties);
        if (!paragraph.MarkProperties.IsEmpty)
        {
            var mark = new W.ParagraphMarkRunProperties();
            FillRunProperties(mark, paragraph.MarkProperties, null);
            pPr.Append(mark);
        }

        return pPr.HasChildren ? pPr : null;
    }

    private static void FillParagraphProperties(OpenXmlCompositeElement pPr, ParagraphProperties p)
    {
        if (p.KeepWithNext is { } keepNext)
        {
            pPr.Append(new W.KeepNext { Val = OnOffValue.FromBoolean(keepNext) });
        }

        if (p.KeepLinesTogether is { } keepLines)
        {
            pPr.Append(new W.KeepLines { Val = OnOffValue.FromBoolean(keepLines) });
        }

        if (p.PageBreakBefore is { } pageBreakBefore)
        {
            pPr.Append(new W.PageBreakBefore { Val = OnOffValue.FromBoolean(pageBreakBefore) });
        }

        if (p.WidowControl is { } widow)
        {
            pPr.Append(new W.WidowControl { Val = OnOffValue.FromBoolean(widow) });
        }

        if (p.List is { } list)
        {
            pPr.Append(new W.NumberingProperties(
                new W.NumberingLevelReference { Val = list.IsNone ? 0 : list.Level },
                new W.NumberingId { Val = list.IsNone ? 0 : list.NumberingId }));
        }

        if (p.Tabs is { Count: > 0 } tabs)
        {
            var element = new W.Tabs();
            foreach (TabStop stop in tabs)
            {
                element.Append(new W.TabStop
                {
                    Val = TabAlignmentTo(stop.Alignment, stop.IsCleared),
                    Leader = stop.Leader == TabLeader.None ? null : TabLeaderTo(stop.Leader),
                    Position = stop.Position.Value,
                });
            }

            pPr.Append(element);
        }

        if (p.SpaceBefore is not null || p.SpaceAfter is not null || p.LineSpacing is not null)
        {
            var spacing = new W.SpacingBetweenLines();
            if (p.SpaceBefore is { } before)
            {
                spacing.Before = Inv(Math.Max(0, before.Value));
            }

            if (p.SpaceAfter is { } after)
            {
                spacing.After = Inv(Math.Max(0, after.Value));
            }

            if (p.LineSpacing is { } line)
            {
                spacing.Line = Inv(line.Value);
                spacing.LineRule = line.Rule switch
                {
                    LineSpacingRule.Exact => W.LineSpacingRuleValues.Exact,
                    LineSpacingRule.AtLeast => W.LineSpacingRuleValues.AtLeast,
                    _ => W.LineSpacingRuleValues.Auto,
                };
            }

            pPr.Append(spacing);
        }

        if (p.LeftIndent is not null || p.RightIndent is not null || p.FirstLineIndent is not null)
        {
            var ind = new W.Indentation();
            if (p.LeftIndent is { } left)
            {
                ind.Left = Inv(left.Value);
            }

            if (p.RightIndent is { } right)
            {
                ind.Right = Inv(right.Value);
            }

            if (p.FirstLineIndent is { } first)
            {
                if (first.IsNegative)
                {
                    ind.Hanging = Inv(-first.Value);
                }
                else
                {
                    ind.FirstLine = Inv(first.Value);
                }
            }

            pPr.Append(ind);
        }

        if (p.ContextualSpacing is { } contextual)
        {
            pPr.Append(new W.ContextualSpacing { Val = OnOffValue.FromBoolean(contextual) });
        }

        if (p.Alignment is { } alignment)
        {
            pPr.Append(new W.Justification { Val = AlignmentTo(alignment) });
        }

        if (p.OutlineLevel is { } outline)
        {
            pPr.Append(new W.OutlineLevel { Val = outline });
        }
    }

    // ------------------------------------------------------------------ sections

    private static W.SectionProperties BuildSectionProperties(MainDocumentPart main, Section section, int sectionIndex)
    {
        SectionProperties p = section.Properties;
        var sectPr = new W.SectionProperties();

        foreach (HeaderFooterVariant variant in Enum.GetValues<HeaderFooterVariant>())
        {
            if (section.Headers.Get(variant) is { } header)
            {
                HeaderPart part = main.AddNewPart<HeaderPart>();
                var element = new W.Header();
                element.Append(header.Select(b => BuildBlock(main, b)));
                part.Header = element;
                sectPr.Append(new W.HeaderReference { Type = VariantTo(variant), Id = main.GetIdOfPart(part) });
            }
        }

        foreach (HeaderFooterVariant variant in Enum.GetValues<HeaderFooterVariant>())
        {
            if (section.Footers.Get(variant) is { } footer)
            {
                FooterPart part = main.AddNewPart<FooterPart>();
                var element = new W.Footer();
                element.Append(footer.Select(b => BuildBlock(main, b)));
                part.Footer = element;
                sectPr.Append(new W.FooterReference { Type = VariantTo(variant), Id = main.GetIdOfPart(part) });
            }
        }

        if (sectionIndex > 0 || p.Start != SectionStart.NextPage)
        {
            sectPr.Append(new W.SectionType { Val = SectionStartTo(p.Start) });
        }

        sectPr.Append(new W.PageSize
        {
            Width = (uint)Math.Max(1, p.PageWidth.Value),
            Height = (uint)Math.Max(1, p.PageHeight.Value),
            Orient = p.Orientation == Orientation.Landscape ? W.PageOrientationValues.Landscape : null,
        });
        sectPr.Append(new W.PageMargin
        {
            Top = p.MarginTop.Value,
            Right = (uint)Math.Max(0, p.MarginRight.Value),
            Bottom = p.MarginBottom.Value,
            Left = (uint)Math.Max(0, p.MarginLeft.Value),
            Header = (uint)Math.Max(0, p.HeaderDistance.Value),
            Footer = (uint)Math.Max(0, p.FooterDistance.Value),
            Gutter = (uint)Math.Max(0, p.Gutter.Value),
        });

        if (p.PageNumberStart is not null || p.PageNumberFormat != PageNumberFormat.Decimal)
        {
            sectPr.Append(new W.PageNumberType
            {
                Start = p.PageNumberStart,
                Format = p.PageNumberFormat == PageNumberFormat.Decimal ? null : PageNumberFormatTo(p.PageNumberFormat),
            });
        }

        if (p.ColumnCount > 1)
        {
            sectPr.Append(new W.Columns { ColumnCount = (short)p.ColumnCount });
        }

        if (p.TitlePage)
        {
            sectPr.Append(new W.TitlePage());
        }

        return sectPr;
    }

    // ------------------------------------------------------------------ numbering

    private static W.Numbering BuildNumbering(ListStore lists)
    {
        var numbering = new W.Numbering();
        foreach (ListDefinition definition in lists.Definitions.Values.OrderBy(d => d.Id))
        {
            var abstractNum = new W.AbstractNum { AbstractNumberId = definition.Id };
            abstractNum.Append(new W.MultiLevelType { Val = W.MultiLevelValues.HybridMultilevel });
            for (int i = 0; i < definition.Levels.Length; i++)
            {
                ListLevel level = definition.Levels[i];
                var element = new W.Level { LevelIndex = i };
                element.Append(new W.StartNumberingValue { Val = level.Start });
                element.Append(new W.NumberingFormat { Val = NumberFormatTo(level.Format) });
                element.Append(new W.LevelText { Val = level.Text });
                element.Append(new W.LevelJustification { Val = LevelJustificationTo(level.Alignment) });
                element.Append(new W.PreviousParagraphProperties(new W.Indentation { Left = Inv(level.LeftIndent.Value), Hanging = Inv(level.Hanging.Value) }));
                if (level.MarkerFont is not null)
                {
                    element.Append(new W.NumberingSymbolRunProperties(new W.RunFonts { Ascii = level.MarkerFont, HighAnsi = level.MarkerFont }));
                }

                abstractNum.Append(element);
            }

            numbering.Append(abstractNum);
        }

        foreach (ListInstance instance in lists.Instances.Values.OrderBy(i => i.Id))
        {
            var num = new W.NumberingInstance { NumberID = instance.Id };
            num.Append(new W.AbstractNumId { Val = instance.DefinitionId });
            if (instance.StartOverrides is not null)
            {
                foreach ((int level, int start) in instance.StartOverrides.OrderBy(o => o.Key))
                {
                    num.Append(new W.LevelOverride(new W.StartOverrideNumberingValue { Val = start }) { LevelIndex = level });
                }
            }

            numbering.Append(num);
        }

        return numbering;
    }

    private static W.NumberFormatValues NumberFormatTo(NumberFormat format) => format switch
    {
        NumberFormat.Bullet => W.NumberFormatValues.Bullet,
        NumberFormat.LowerLetter => W.NumberFormatValues.LowerLetter,
        NumberFormat.UpperLetter => W.NumberFormatValues.UpperLetter,
        NumberFormat.LowerRoman => W.NumberFormatValues.LowerRoman,
        NumberFormat.UpperRoman => W.NumberFormatValues.UpperRoman,
        NumberFormat.None => W.NumberFormatValues.None,
        _ => W.NumberFormatValues.Decimal,
    };

    private static W.LevelJustificationValues LevelJustificationTo(Alignment alignment) => alignment switch
    {
        Alignment.Center => W.LevelJustificationValues.Center,
        Alignment.Right => W.LevelJustificationValues.Right,
        _ => W.LevelJustificationValues.Left,
    };

    // ------------------------------------------------------------------ styles

    private static W.Styles BuildStyles(StyleSheet sheet)
    {
        var runDefaults = new W.RunPropertiesBaseStyle();
        FillRunProperties(runDefaults, sheet.Defaults.Run, null);
        var paragraphDefaults = new W.ParagraphPropertiesBaseStyle();
        FillParagraphProperties(paragraphDefaults, sheet.Defaults.Paragraph);

        var styles = new W.Styles(new W.DocDefaults(
            new W.RunPropertiesDefault(runDefaults),
            new W.ParagraphPropertiesDefault(paragraphDefaults)));

        foreach (Style style in sheet.Styles.Values.OrderBy(s => s.Priority).ThenBy(s => s.Id, StringComparer.Ordinal))
        {
            var element = new W.Style { Type = StyleTypeTo(style.Type), StyleId = style.Id };
            if (style.IsDefault)
            {
                element.Default = OnOffValue.FromBoolean(true);
            }

            element.Append(new W.StyleName { Val = style.Name });
            if (style.BasedOn is not null)
            {
                element.Append(new W.BasedOn { Val = style.BasedOn });
            }

            if (style.Next is not null)
            {
                element.Append(new W.NextParagraphStyle { Val = style.Next });
            }

            element.Append(new W.UIPriority { Val = style.Priority });
            if (style.QuickFormat)
            {
                element.Append(new W.PrimaryStyle());
            }

            if (!style.ParagraphProperties.IsEmpty)
            {
                var pPr = new W.StyleParagraphProperties();
                FillParagraphProperties(pPr, style.ParagraphProperties);
                element.Append(pPr);
            }

            if (!style.RunProperties.IsEmpty)
            {
                var rPr = new W.StyleRunProperties();
                FillRunProperties(rPr, style.RunProperties, null);
                element.Append(rPr);
            }

            styles.Append(element);
        }

        return styles;
    }
}
