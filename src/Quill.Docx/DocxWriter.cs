using System.Collections.Immutable;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using Quill.Core.Editing;
using Quill.Core.Model;
using Quill.Core.Text;
using Quill.Core.Units;
using A = DocumentFormat.OpenXml.Drawing;
using DW = DocumentFormat.OpenXml.Drawing.Wordprocessing;
using PIC = DocumentFormat.OpenXml.Drawing.Pictures;
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
        main.Document = document.Settings.PageColor is { IsAuto: false } pageColor
            ? new W.Document(new W.DocumentBackground { Color = pageColor.ToHex() }, body)
            : new W.Document(body);
        var writerState = new WriterState(document.Images, FootnoteSupport.ReferencedNoteIds(document), document);
        var bodyContext = new PartContext(writerState, main, main);

        StyleDefinitionsPart stylesPart = main.AddNewPart<StyleDefinitionsPart>();
        stylesPart.Styles = BuildStyles(document.Styles);

        if (!document.Lists.IsEmpty)
        {
            NumberingDefinitionsPart numberingPart = main.AddNewPart<NumberingDefinitionsPart>();
            numberingPart.Numbering = BuildNumbering(document.Lists);
        }

        DocumentSettingsPart settingsPart = main.AddNewPart<DocumentSettingsPart>();
        var settings = new W.Settings(new W.DefaultTabStop { Val = (short)Math.Clamp(document.Settings.DefaultTabStop.Value, 1, short.MaxValue) });
        if (document.Settings.TrackChanges)
        {
            settings.PrependChild(new W.TrackRevisions());
        }

        if (document.Settings.PageColor is { IsAuto: false })
        {
            settings.PrependChild(new W.DisplayBackgroundShape()); // first in w:settings: Word shows the colour only with this switch
        }

        if (document.Settings.EvenAndOddHeaders)
        {
            settings.Append(new W.EvenAndOddHeaders());
        }

        if (writerState.NoteIds.Count > 0)
        {
            settings.Append(new W.FootnoteDocumentWideProperties(new W.FootnoteSpecialReference { Id = -1 }, new W.FootnoteSpecialReference { Id = 0 }));
            WriteFootnotes(main, writerState, document);
        }

        if (writerState.EndnoteIds.Count > 0)
        {
            settings.Append(new W.EndnoteDocumentWideProperties(new W.EndnoteSpecialReference { Id = -1 }, new W.EndnoteSpecialReference { Id = 0 }));
            WriteEndnotes(main, writerState, document);
        }

        WriteComments(main, writerState);


        settingsPart.Settings = settings;

        for (int s = 0; s < document.Sections.Count; s++)
        {
            Section section = document.Sections[s];
            W.SectionProperties sectPr = BuildSectionProperties(bodyContext, section, s);
            bool last = s == document.Sections.Count - 1;

            var blocks = new List<OpenXmlElement>();
            bodyContext.CurrentStory = StoryId.Body(s);
            for (int b = 0; b < section.Body.Count; b++)
            {
                bodyContext.CurrentBlock = BlockPath.Of(b);
                blocks.Add(BuildBlock(bodyContext, section.Body[b]));
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

    private static OpenXmlElement BuildBlock(PartContext context, Block block) => block switch
    {
        Paragraph paragraph => BuildParagraph(context, paragraph),
        OpaqueBlock opaque => BuildOpaque(context.Main, opaque),
        Table table => BuildTable(context, table),
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

    /// <summary>Tables are read-only, so a table read from a file is written back from its original XML; tables built in code get a plain grid.</summary>
    private static OpenXmlElement BuildTable(PartContext context, Table table)
    {
        if (table.OuterXml is { } xml)
        {
            try
            {
                return new W.Table(xml);
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.Xml.XmlException or ArgumentException)
            {
                // Fall through and rebuild from the model.
            }
        }

        var element = new W.Table();
        var tblPr = new W.TableProperties(new W.TableWidth { Width = "0", Type = W.TableWidthUnitValues.Auto });
        if (table.Indent.Value != 0)
        {
            tblPr.Append(new W.TableIndentation { Width = table.Indent.Value, Type = W.TableWidthUnitValues.Dxa });
        }

        if (table.HasBorders)
        {
            tblPr.Append(new W.TableBorders(
                new W.TopBorder { Val = W.BorderValues.Single, Size = 4U, Space = 0U, Color = "auto" },
                new W.LeftBorder { Val = W.BorderValues.Single, Size = 4U, Space = 0U, Color = "auto" },
                new W.BottomBorder { Val = W.BorderValues.Single, Size = 4U, Space = 0U, Color = "auto" },
                new W.RightBorder { Val = W.BorderValues.Single, Size = 4U, Space = 0U, Color = "auto" },
                new W.InsideHorizontalBorder { Val = W.BorderValues.Single, Size = 4U, Space = 0U, Color = "auto" },
                new W.InsideVerticalBorder { Val = W.BorderValues.Single, Size = 4U, Space = 0U, Color = "auto" }));
        }

        element.Append(tblPr);
        var grid = new W.TableGrid();
        int columnCount = Math.Max(1, table.ColumnCount);
        for (int c = 0; c < columnCount; c++)
        {
            int width = c < table.ColumnWidths.Length && table.ColumnWidths[c].Value > 0 ? table.ColumnWidths[c].Value : 9360 / columnCount;
            grid.Append(new W.GridColumn { Width = width.ToString(System.Globalization.CultureInfo.InvariantCulture) });
        }

        element.Append(grid);
        BlockPath? tablePath = context.CurrentBlock;
        for (int r = 0; r < table.Rows.Length; r++)
        {
            TableRow row = table.Rows[r];
            var tr = new W.TableRow();
            var trPr = new W.TableRowProperties();
            if (row.Height is { } height)
            {
                trPr.Append(new W.TableRowHeight { Val = (uint)Math.Max(0, height.Value), HeightType = row.ExactHeight ? W.HeightRuleValues.Exact : W.HeightRuleValues.AtLeast });
            }

            if (row.IsHeader)
            {
                trPr.Append(new W.TableHeader());
            }

            if (trPr.HasChildren)
            {
                tr.Append(trPr);
            }


            for (int c = 0; c < row.Cells.Length; c++)
            {
                TableCell cell = row.Cells[c];
                var tcPr = new W.TableCellProperties();
                if (cell.GridSpan > 1)
                {
                    tcPr.Append(new W.GridSpan { Val = cell.GridSpan });
                }

                if (cell.VerticalMerge != VerticalMerge.None)
                {
                    tcPr.Append(cell.VerticalMerge == VerticalMerge.Restart ? new W.VerticalMerge { Val = W.MergedCellValues.Restart } : new W.VerticalMerge());
                }

                if (cell.Shading is { IsAuto: false } shading)
                {
                    tcPr.Append(new W.Shading { Val = W.ShadingPatternValues.Clear, Color = "auto", Fill = shading.ToHex() });
                }

                var tc = new W.TableCell(tcPr);
                for (int k = 0; k < cell.Blocks.Count; k++)
                {
                    context.CurrentBlock = tablePath?.Child(r, c, k);
                    tc.Append(BuildBlock(context, cell.Blocks[k]));
                }

                context.CurrentBlock = tablePath;
                if (tc.Elements<W.Paragraph>().LastOrDefault() is null || tc.LastChild is not W.Paragraph)
                {
                    tc.Append(new W.Paragraph());
                }

                tr.Append(tc);
            }

            element.Append(tr);
        }

        return element;
    }

    private static W.Paragraph BuildParagraph(PartContext context, Paragraph paragraph)

    {
        var result = new W.Paragraph();
        W.ParagraphProperties? pPr = BuildParagraphProperties(paragraph);
        if (pPr is not null)
        {
            result.Append(pPr);
        }

        OpenXmlElement container = result;
        OpenXmlElement runContainer = result;
        string? openLink = null;
        Revision? openRevision = null;
        for (int inlineIndex = 0; inlineIndex < paragraph.Inlines.Length; inlineIndex++)
        {
            Inline inline = paragraph.Inlines[inlineIndex];
            string? link = inline.Properties.Link;
            if (!string.Equals(link, openLink, StringComparison.Ordinal))
            {
                container = result;
                openLink = null;
                openRevision = null;
                if (link is not null && BuildHyperlink(context, link) is { } hyperlink)
                {
                    result.Append(hyperlink);
                    container = hyperlink;
                    openLink = link;
                }

                runContainer = container;
            }

            // Tracked runs sit inside w:ins / w:del; consecutive runs of one change share a wrapper.
            Revision? revision = inline is Run ? inline.Properties.Revision : null;
            if (!Equals(revision, openRevision) || revision is null)
            {
                openRevision = revision;
                runContainer = container;
                if (revision is not null)
                {
                    OpenXmlElement wrapper = BuildRevisionWrapper(context, revision);
                    container.Append(wrapper);
                    runContainer = wrapper;
                }
            }

            switch (inline)
            {
                case Run run:
                    AppendCommentStart(runContainer, context, run);
                    runContainer.Append(BuildRun(run, deleted: revision is { IsDeletion: true }));
                    AppendCommentEnd(runContainer, context, run, inlineIndex);
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
                    container.Append(element);
                    break;
                }

                case InlineImage image:
                    if (context.AddImage(image.ImageId) is { } relationshipId)
                    {
                        container.Append(BuildPictureRun(image, relationshipId, context.NextPictureId()));
                    }

                    break;
                case FootnoteReference note:
                {
                    if (note.IsNoteMark)
                    {
                        var markRun = new W.Run();
                        AppendRunProperties(markRun, note.Properties, note.StyleId);
                        markRun.Append(note.IsEndnote ? new W.EndnoteReferenceMark() : new W.FootnoteReferenceMark());
                        container.Append(markRun);
                    }
                    else if (note.IsEndnote ? context.State.EndnoteIds.Contains(note.NoteId) : context.State.NoteIds.Contains(note.NoteId))
                    {
                        var referenceRun = new W.Run();
                        AppendRunProperties(referenceRun, note.Properties, note.StyleId);
                        referenceRun.Append(note.IsEndnote ? new W.EndnoteReference { Id = note.NoteId } : new W.FootnoteReference { Id = note.NoteId });
                        container.Append(referenceRun);
                    }

                    break;
                }

                case Field field:
                {
                    var simple = new W.SimpleField { Instruction = " " + field.Instruction + " " };

                    var element = new W.Run();
                    AppendRunProperties(element, field.Properties, field.StyleId);
                    element.Append(new W.Text(SequenceFields.Text(field, context.State.Sequences)) { Space = SpaceProcessingModeValues.Preserve });
                    simple.Append(element);
                    container.Append(simple);
                    break;
                }
            }
        }

        return result;
    }

    private static OpenXmlElement BuildRevisionWrapper(PartContext context, Revision revision)
    {
        W.RunTrackChangeType wrapper = revision.IsInsertion ? new W.InsertedRun() : new W.DeletedRun();
        wrapper.Id = context.State.NextRevisionId().ToString(System.Globalization.CultureInfo.InvariantCulture);
        wrapper.Author = revision.Author;
        if (revision.Date > DateTime.MinValue)
        {
            wrapper.Date = new DateTimeValue(DateTime.SpecifyKind(revision.Date, DateTimeKind.Utc));
        }

        return wrapper;
    }

    private static W.Run BuildRun(Run run, bool deleted = false)
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
                element.Append(deleted
                    ? new W.DeletedText(parts[i]) { Space = SpaceProcessingModeValues.Preserve }
                    : new W.Text(parts[i]) { Space = SpaceProcessingModeValues.Preserve });
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

    private static W.SectionProperties BuildSectionProperties(PartContext context, Section section, int sectionIndex)
    {
        SectionProperties p = section.Properties;
        var sectPr = new W.SectionProperties();

        foreach (HeaderFooterVariant variant in Enum.GetValues<HeaderFooterVariant>())
        {
            if (section.Headers.Get(variant) is { } header)
            {
                HeaderPart part = context.Main.AddNewPart<HeaderPart>();
                var headerContext = new PartContext(context.State, context.Main, part);
                var element = new W.Header();
                element.Append(header.Select(b => BuildBlock(headerContext, b)));
                part.Header = element;
                sectPr.Append(new W.HeaderReference { Type = VariantTo(variant), Id = context.Main.GetIdOfPart(part) });
            }
        }

        foreach (HeaderFooterVariant variant in Enum.GetValues<HeaderFooterVariant>())
        {
            if (section.Footers.Get(variant) is { } footer)
            {
                FooterPart part = context.Main.AddNewPart<FooterPart>();
                var footerContext = new PartContext(context.State, context.Main, part);
                var element = new W.Footer();
                element.Append(footer.Select(b => BuildBlock(footerContext, b)));
                part.Footer = element;
                sectPr.Append(new W.FooterReference { Type = VariantTo(variant), Id = context.Main.GetIdOfPart(part) });
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

        if (p.PageBorders is { } borders)
        {
            W.BorderValues value = borders.Style switch
            {
                PageBorderStyle.Double => W.BorderValues.Double,
                PageBorderStyle.Dotted => W.BorderValues.Dotted,
                PageBorderStyle.Dashed => W.BorderValues.Dashed,
                PageBorderStyle.Thick => W.BorderValues.Thick,
                _ => W.BorderValues.Single,
            };
            T Side<T>()
                where T : W.BorderType, new() => new() { Val = value, Size = (uint)borders.Width, Space = (uint)borders.Space, Color = borders.Color.IsAuto ? "auto" : borders.Color.ToHex() };
            sectPr.Append(new W.PageBorders(Side<W.TopBorder>(), Side<W.LeftBorder>(), Side<W.BottomBorder>(), Side<W.RightBorder>())
            {
                OffsetFrom = borders.FromText ? W.PageBorderOffsetValues.Text : W.PageBorderOffsetValues.Page,
            });
        }

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
            sectPr.Append(new W.Columns { ColumnCount = (short)p.ColumnCount, Space = p.ColumnSpacing.Value.ToString(System.Globalization.CultureInfo.InvariantCulture), EqualWidth = true });
        }

        if (p.TitlePage)
        {
            sectPr.Append(new W.TitlePage());
        }

        return sectPr;
    }

    private static StoryKind PageLayoutHeaderKind(HeaderFooterVariant variant) => variant switch
    {
        HeaderFooterVariant.First => StoryKind.HeaderFirst,
        HeaderFooterVariant.Even => StoryKind.HeaderEven,
        _ => StoryKind.HeaderDefault,
    };

    private static StoryKind PageLayoutFooterKind(HeaderFooterVariant variant) => variant switch
    {
        HeaderFooterVariant.First => StoryKind.FooterFirst,
        HeaderFooterVariant.Even => StoryKind.FooterEven,
        _ => StoryKind.FooterDefault,
    };

    // ------------------------------------------------------------------ comments

    /// <summary>Writes comments.xml with every comment that has runs (or none, when unreferenced ones exist only).</summary>
    private static void WriteComments(MainDocumentPart main, WriterState state)
    {
        if (state.Comments.IsEmpty)
        {
            return;
        }

        WordprocessingCommentsPart part = main.AddNewPart<WordprocessingCommentsPart>();
        var comments = new W.Comments();
        var context = new PartContext(state, main, part);
        foreach (Comment comment in state.Comments.Values.OrderBy(c => c.Id))
        {
            var element = new W.Comment
            {
                Id = comment.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                Author = comment.Author,
                Initials = comment.Initials,
                Date = comment.Date?.UtcDateTime,
            };
            bool first = true;
            foreach (Block block in comment.Blocks)
            {
                OpenXmlElement built = BuildBlock(context, block);
                if (first && built is W.Paragraph paragraph)
                {
                    var reference = new W.Run(new W.RunProperties(new W.RunStyle { Val = CommentSupport.ReferenceStyleId }), new W.AnnotationReferenceMark());
                    if (paragraph.ParagraphProperties is { } pPr)
                    {
                        pPr.InsertAfterSelf(reference);
                    }
                    else
                    {
                        paragraph.PrependChild(reference);
                    }
                }

                first = false;
                element.Append(built);
            }

            if (!element.HasChildren)
            {
                element.Append(new W.Paragraph());
            }

            comments.Append(element);
        }

        part.Comments = comments;
    }

    /// <summary>Range markers around the runs of a comment: start before its first run, end and reference after its last.</summary>
    private static void AppendCommentStart(OpenXmlElement container, PartContext context, Inline inline)
    {
        if (inline.Properties.CommentId is { } id && context.State.Comments.ContainsKey(id) && context.State.OpenComments.Add(id))
        {
            container.Append(new W.CommentRangeStart { Id = id.ToString(System.Globalization.CultureInfo.InvariantCulture) });
        }
    }

    private static void AppendCommentEnd(OpenXmlElement container, PartContext context, Inline inline, int inlineIndex)
    {
        if (inline.Properties.CommentId is not { } id || !context.State.OpenComments.Contains(id) || context.CurrentStory is not { } story)
        {
            return;
        }

        if (context.State.CommentLastRun.TryGetValue(id, out (StoryId Story, BlockPath Block, int Inline) last) && last.Story == story && context.CurrentBlock is { } currentBlock && last.Block == currentBlock && last.Inline == inlineIndex)
        {
            context.State.OpenComments.Remove(id);
            string idText = id.ToString(System.Globalization.CultureInfo.InvariantCulture);
            container.Append(new W.CommentRangeEnd { Id = idText });
            container.Append(new W.Run(new W.RunProperties(new W.RunStyle { Val = CommentSupport.ReferenceStyleId }), new W.CommentReference { Id = idText }));
        }
    }

    // ------------------------------------------------------------------ footnotes


    private static void WriteFootnotes(MainDocumentPart main, WriterState state, Document document)
    {
        FootnotesPart part = main.AddNewPart<FootnotesPart>();
        var footnotes = new W.Footnotes();
        footnotes.Append(new W.Footnote(new W.Paragraph(
            new W.ParagraphProperties(new W.SpacingBetweenLines { After = "0", Line = "240", LineRule = W.LineSpacingRuleValues.Auto }),
            new W.Run(new W.SeparatorMark()))) { Type = W.FootnoteEndnoteValues.Separator, Id = -1 });
        footnotes.Append(new W.Footnote(new W.Paragraph(
            new W.ParagraphProperties(new W.SpacingBetweenLines { After = "0", Line = "240", LineRule = W.LineSpacingRuleValues.Auto }),
            new W.Run(new W.ContinuationSeparatorMark()))) { Type = W.FootnoteEndnoteValues.ContinuationSeparator, Id = 0 });
        var context = new PartContext(state, main, part);
        foreach (int id in state.NoteIds)
        {
            var note = new W.Footnote { Id = id };
            note.Append(document.Footnotes[id].Select(b => BuildBlock(context, b)));
            footnotes.Append(note);
        }

        part.Footnotes = footnotes;
    }

    private static void WriteEndnotes(MainDocumentPart main, WriterState state, Document document)
    {
        EndnotesPart part = main.AddNewPart<EndnotesPart>();
        var endnotes = new W.Endnotes();
        endnotes.Append(new W.Endnote(new W.Paragraph(
            new W.ParagraphProperties(new W.SpacingBetweenLines { After = "0", Line = "240", LineRule = W.LineSpacingRuleValues.Auto }),
            new W.Run(new W.SeparatorMark()))) { Type = W.FootnoteEndnoteValues.Separator, Id = -1 });
        endnotes.Append(new W.Endnote(new W.Paragraph(
            new W.ParagraphProperties(new W.SpacingBetweenLines { After = "0", Line = "240", LineRule = W.LineSpacingRuleValues.Auto }),
            new W.Run(new W.ContinuationSeparatorMark()))) { Type = W.FootnoteEndnoteValues.ContinuationSeparator, Id = 0 });
        var context = new PartContext(state, main, part);
        foreach (int id in state.EndnoteIds)
        {
            var note = new W.Endnote { Id = id };
            context.CurrentStory = StoryId.Endnote(id);
            note.Append(document.Endnotes[id].Select((b, i) => { context.CurrentBlock = BlockPath.Of(i); return BuildBlock(context, b); }));
            endnotes.Append(note);
        }

        part.Endnotes = endnotes;
    }

    // ------------------------------------------------------------------ hyperlinks



    /// <summary>A w:hyperlink for an external URL (through a relationship on the part) or an internal "#bookmark" anchor.</summary>
    private static W.Hyperlink? BuildHyperlink(PartContext context, string link)
    {
        if (link.StartsWith('#'))
        {
            return link.Length > 1 ? new W.Hyperlink { Anchor = link[1..] } : null;
        }

        if (!Uri.TryCreate(link, UriKind.Absolute, out Uri? uri) && !Uri.TryCreate("http://" + link, UriKind.Absolute, out uri))
        {
            return null;
        }

        try
        {
            HyperlinkRelationship relationship = context.Part.AddHyperlinkRelationship(uri, true);
            return new W.Hyperlink { Id = relationship.Id, History = true };
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or UriFormatException)
        {
            return null;
        }
    }

    // ------------------------------------------------------------------ pictures


    private static W.Run BuildPictureRun(InlineImage image, string relationshipId, uint pictureId)
    {
        long cx = Math.Max(1, image.Width.ToEmu());
        long cy = Math.Max(1, image.Height.ToEmu());
        string name = "Picture " + pictureId.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var inline = new DW.Inline(
            new DW.Extent { Cx = cx, Cy = cy },
            new DW.EffectExtent { LeftEdge = 0L, TopEdge = 0L, RightEdge = 0L, BottomEdge = 0L },
            new DW.DocProperties { Id = pictureId, Name = name },
            new DW.NonVisualGraphicFrameDrawingProperties(new A.GraphicFrameLocks { NoChangeAspect = true }),
            new A.Graphic(
                new A.GraphicData(
                    new PIC.Picture(
                        new PIC.NonVisualPictureProperties(
                            new PIC.NonVisualDrawingProperties { Id = 0U, Name = name },
                            new PIC.NonVisualPictureDrawingProperties()),
                        new PIC.BlipFill(
                            new A.Blip { Embed = relationshipId },
                            new A.Stretch(new A.FillRectangle())),
                        new PIC.ShapeProperties(
                            new A.Transform2D(new A.Offset { X = 0L, Y = 0L }, new A.Extents { Cx = cx, Cy = cy }),
                            new A.PresetGeometry(new A.AdjustValueList()) { Preset = A.ShapeTypeValues.Rectangle })))
                { Uri = "http://schemas.openxmlformats.org/drawingml/2006/picture" }))
        {
            DistanceFromTop = 0U,
            DistanceFromBottom = 0U,
            DistanceFromLeft = 0U,
            DistanceFromRight = 0U,
        };
        var run = new W.Run();
        AppendRunProperties(run, image.Properties, image.StyleId);
        run.Append(new W.Drawing(inline));
        return run;
    }

    /// <summary>State shared by every part of one package: the picture bytes, the referenced footnotes, comment bookkeeping and the running drawing id.</summary>
    private sealed class WriterState
    {
        private uint _nextPictureId;
        private int _nextRevisionId;

        public int NextRevisionId() => ++_nextRevisionId;

        public WriterState(ImageStore images, IReadOnlyList<int> noteIds, Document document)
        {
            Images = images;
            NoteIds = noteIds;
            EndnoteIds = FootnoteSupport.ReferencedEndnoteIds(document);
            Sequences = SequenceFields.Numbers(document);
            Comments = document.Comments;
            foreach ((StoryId story, BlockPath path, Paragraph paragraph) in DocumentEditor.AllParagraphs(document))
            {
                for (int i = 0; i < paragraph.Inlines.Length; i++)
                {
                    if (paragraph.Inlines[i].Properties.CommentId is { } id && Comments.ContainsKey(id))
                    {
                        CommentLastRun[id] = (story, path, i);
                    }
                }
            }
        }

        public ImageStore Images { get; }

        public IReadOnlyList<int> NoteIds { get; }

        public IReadOnlyList<int> EndnoteIds { get; }

        /// <summary>Running numbers of the SEQ fields, so saved captions show the right number without a field update.</summary>
        public IReadOnlyDictionary<Field, int> Sequences { get; }

        public ImmutableDictionary<int, Comment> Comments { get; }

        /// <summary>Where each comment's range ends (story, block, inline index), so the end marker goes after that run.</summary>
        public Dictionary<int, (StoryId Story, BlockPath Block, int Inline)> CommentLastRun { get; } = [];

        public HashSet<int> OpenComments { get; } = [];

        public HashSet<int> WrittenComments { get; } = [];

        public uint NextPictureId() => ++_nextPictureId;
    }



    /// <summary>The part being written (body, a header or a footer); pictures become image parts related to it.</summary>
    private sealed class PartContext(WriterState state, MainDocumentPart main, OpenXmlPart part)
    {
        private readonly Dictionary<string, string> _relationshipIds = new(StringComparer.Ordinal);

        public WriterState State { get; } = state;

        public MainDocumentPart Main { get; } = main;

        public OpenXmlPart Part { get; } = part;

        /// <summary>The story and block being written (set by the callers that walk stories), for comment range markers.</summary>
        public StoryId? CurrentStory { get; set; }

        public BlockPath? CurrentBlock { get; set; }

        public uint NextPictureId() => State.NextPictureId();

        /// <summary>Adds the picture to this part once and returns its relationship id; null when the document has no such picture.</summary>
        public string? AddImage(string imageId)
        {
            if (_relationshipIds.TryGetValue(imageId, out string? existing))
            {
                return existing;
            }

            if (State.Images.Get(imageId) is not { } data)
            {
                return null;
            }

            ImagePart imagePart = Part switch
            {
                MainDocumentPart m => m.AddImagePart(data.ContentType),
                HeaderPart h => h.AddImagePart(data.ContentType),
                FooterPart f => f.AddImagePart(data.ContentType),
                FootnotesPart n => n.AddImagePart(data.ContentType),
                EndnotesPart e => e.AddImagePart(data.ContentType),
                _ => throw new NotSupportedException("Pictures can only be written into the body, headers and footers."),
            };
            using (var stream = new MemoryStream(data.Bytes, writable: false))
            {
                imagePart.FeedData(stream);
            }

            string id = Part.GetIdOfPart(imagePart);
            _relationshipIds[imageId] = id;
            return id;
        }
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
