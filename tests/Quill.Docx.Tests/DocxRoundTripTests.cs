using System.Collections.Immutable;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using Quill.Core.Model;
using Quill.Core.Styles;
using Quill.Core.Text;
using Quill.Core.Units;
using Quill.Docx;
using Xunit;
using W = DocumentFormat.OpenXml.Wordprocessing;

namespace Quill.Docx.Tests;

public class DocxRoundTripTests
{
    private static readonly RunProperties Fancy = new()
    {
        FontFamily = "Georgia",
        FontSize = HalfPoints.FromPoints(14),
        Bold = true,
        Italic = true,
        Underline = UnderlineStyle.Double,
        Color = DocColor.Parse("C00000"),
        Highlight = HighlightColor.Yellow,
        AllCaps = true,
        Language = "de-DE",
    };

    private static Document RichDocument()
    {
        var body1 = new Paragraph(
            [
                new Run("Hello "),
                new Run("world", Fancy),
                new Run("\ttabbed"),
                new Break(BreakKind.Line),
                new Run("after line break"),
            ],
            DefaultStyleSheet.Heading1Id,
            new ParagraphProperties
            {
                Alignment = Alignment.Center,
                LeftIndent = Twips.FromInches(0.5),
                RightIndent = Twips.FromInches(0.25),
                FirstLineIndent = -Twips.FromInches(0.25),
                SpaceBefore = Twips.FromPoints(6),
                SpaceAfter = Twips.FromPoints(12),
                LineSpacing = LineSpacing.Exactly(Twips.FromPoints(18)),
                KeepWithNext = true,
                KeepLinesTogether = true,
                PageBreakBefore = false,
                WidowControl = false,
                ContextualSpacing = true,
                OutlineLevel = 2,
                Tabs = TabStops.Create(new TabStop(Twips.FromInches(2), TabAlignment.Right, TabLeader.Dot), new TabStop(Twips.FromInches(3), IsCleared: true)),
            },
            new RunProperties { Bold = true });
        var body2 = new Paragraph([new Run("Before break"), new Break(BreakKind.Page), new Run("after break", new RunProperties { Hidden = true, VerticalAlignment = VerticalTextAlignment.Superscript })]);
        var table = new OpaqueBlock("tbl", "<w:tbl xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:tblPr><w:tblW w:w=\"0\" w:type=\"auto\"/></w:tblPr><w:tblGrid><w:gridCol w:w=\"2000\"/></w:tblGrid><w:tr><w:tc><w:tcPr><w:tcW w:w=\"2000\" w:type=\"dxa\"/></w:tcPr><w:p><w:r><w:t>cell</w:t></w:r></w:p></w:tc></w:tr></w:tbl>");

        var header = ImmutableList.Create<Block>(new Paragraph([new Run("Page "), Field.Page(), new Run(" of "), Field.NumPages()], DefaultStyleSheet.HeaderId));
        var firstHeader = ImmutableList.Create<Block>(Paragraph.FromText("Title page header", DefaultStyleSheet.HeaderId));
        var footer = ImmutableList.Create<Block>(new Paragraph([new Field(FieldKind.Unknown, "DATE \\@ \"yyyy\"", "2026")], DefaultStyleSheet.FooterId));

        var section1 = new Section(
            SectionProperties.Letter with { TitlePage = true, HeaderDistance = Twips.FromInches(0.4) },
            ImmutableList.Create<Block>(body1, body2, table),
            new HeaderFooterSet(header, firstHeader, null),
            new HeaderFooterSet(footer, null, null));
        var section2 = new Section(
            SectionProperties.A4.WithOrientation(Orientation.Landscape) with { Start = SectionStart.OddPage, PageNumberStart = 5, PageNumberFormat = PageNumberFormat.LowerRoman, Gutter = Twips.FromInches(0.1) },
            ImmutableList.Create<Block>(Paragraph.FromText("Second section"), Paragraph.Empty()));

        return new Document(
            ImmutableList.Create(section1, section2),
            DefaultStyleSheet.Create(),
            new DocumentSettings { DefaultTabStop = Twips.FromInches(0.25), EvenAndOddHeaders = true },
            new DocumentMetadata { Title = "Round trip", Author = "Quill tests", Subject = "docx", Created = new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero) });
    }

    private static IReadOnlyList<ValidationErrorInfo> Validate(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        using WordprocessingDocument package = WordprocessingDocument.Open(stream, false);
        return new OpenXmlValidator(FileFormatVersions.Office2019).Validate(package).ToList();
    }

    [Fact]
    public void Written_package_is_schema_valid()
    {
        byte[] bytes = DocxWriter.ToBytes(RichDocument());
        IReadOnlyList<ValidationErrorInfo> errors = Validate(bytes);
        Assert.True(errors.Count == 0, string.Join("\n", errors.Select(e => $"{e.Description} @ {e.Path?.XPath}")));
    }

    [Fact]
    public void Document_survives_a_round_trip()
    {
        Document original = RichDocument();
        LoadResult loaded = DocxReader.Read(new MemoryStream(DocxWriter.ToBytes(original)));
        Document doc = loaded.Document;

        Assert.Contains(loaded.Warnings, w => w.Message.Contains("Tables", StringComparison.Ordinal));
        Assert.Equal(2, doc.Sections.Count);

        // Section 1 body
        ImmutableList<Block> body = doc.Sections[0].Body;
        Assert.Equal(4, body.Count); // the writer adds a paragraph after the trailing table to hold the section break
        Assert.True(((Paragraph)body[3]).IsEmpty);
        var p1 = (Paragraph)body[0];
        Assert.Equal("Hello world\ttabbed￼after line break", p1.FlatText);
        Assert.Equal(DefaultStyleSheet.Heading1Id, p1.StyleId);
        Assert.Equal(Fancy, p1.Inlines[1].Properties);
        Assert.Equal(BreakKind.Line, ((Break)p1.Inlines[3]).Kind);
        Assert.Equal(((Paragraph)original.Sections[0].Body[0]).Properties, p1.Properties);
        Assert.Equal(new RunProperties { Bold = true }, p1.MarkProperties);

        var p2 = (Paragraph)body[1];
        Assert.Equal(BreakKind.Page, ((Break)p2.Inlines[1]).Kind);
        Assert.True(p2.Inlines[2].Properties.Hidden);
        Assert.Equal(VerticalTextAlignment.Superscript, p2.Inlines[2].Properties.VerticalAlignment);

        var opaque = Assert.IsType<OpaqueBlock>(body[2]);
        Assert.Equal("tbl", opaque.LocalName);
        Assert.Contains("cell", opaque.OuterXml, StringComparison.Ordinal);

        // Section 1 setup, headers and footers
        SectionProperties s1 = doc.Sections[0].Properties;
        Assert.True(s1.TitlePage);
        Assert.Equal(Twips.FromInches(0.4), s1.HeaderDistance);
        Assert.Equal(Twips.FromInches(8.5), s1.PageWidth);
        var headerParagraph = (Paragraph)doc.Sections[0].Headers.Default![0];
        Assert.Equal("Page ￼ of ￼", headerParagraph.FlatText);
        Assert.Equal(FieldKind.Page, ((Field)headerParagraph.Inlines[1]).Kind);
        Assert.Equal(FieldKind.NumPages, ((Field)headerParagraph.Inlines[3]).Kind);
        Assert.Equal("Title page header", ((Paragraph)doc.Sections[0].Headers.First![0]).FlatText);
        Assert.Null(doc.Sections[0].Headers.Even);
        var footerField = (Field)((Paragraph)doc.Sections[0].Footers.Default![0]).Inlines[0];
        Assert.Equal(FieldKind.Unknown, footerField.Kind);
        Assert.Equal("2026", footerField.CachedResult);
        Assert.Equal("DATE \\@ \"yyyy\"", footerField.Instruction);

        // Section 2
        SectionProperties s2 = doc.Sections[1].Properties;
        Assert.Equal(Orientation.Landscape, s2.Orientation);
        Assert.True(s2.PageWidth > s2.PageHeight);
        Assert.Equal(SectionStart.OddPage, s2.Start);
        Assert.Equal(5, s2.PageNumberStart);
        Assert.Equal(PageNumberFormat.LowerRoman, s2.PageNumberFormat);
        Assert.Equal(Twips.FromInches(0.1), s2.Gutter);
        Assert.Null(doc.Sections[1].Headers.Default);
        Assert.Equal(["Second section", ""], doc.Sections[1].Body.Cast<Paragraph>().Select(p => p.FlatText));

        // Styles, settings, metadata
        Assert.Equal(StyleSheet.NormalStyleId, doc.Styles.DefaultParagraphStyleId);
        Assert.Equal("Calibri Light", doc.Styles.Get(DefaultStyleSheet.Heading1Id)!.RunProperties.FontFamily);
        Assert.Equal(StyleSheet.NormalStyleId, doc.Styles.Get(DefaultStyleSheet.Heading1Id)!.Next);
        Assert.Equal(HalfPoints.FromPoints(11), doc.Styles.Defaults.Run.FontSize);
        Assert.Equal(LineSpacing.Multiple(1.08), doc.Styles.Defaults.Paragraph.LineSpacing);
        Assert.True(doc.Styles.Get(DefaultStyleSheet.Heading1Id)!.QuickFormat);
        Assert.Equal(Twips.FromInches(0.25), doc.Settings.DefaultTabStop);
        Assert.True(doc.Settings.EvenAndOddHeaders);
        Assert.Equal("Round trip", doc.Metadata.Title);
        Assert.Equal("Quill tests", doc.Metadata.Author);
        Assert.Equal(new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero), doc.Metadata.Created);

        // A second round trip is stable and the re-written package is still valid.
        byte[] again = DocxWriter.ToBytes(doc);
        Assert.Empty(Validate(again));
        Document doc2 = DocxReader.Read(new MemoryStream(again)).Document;
        Assert.Equal(p1.FlatText, ((Paragraph)doc2.Sections[0].Body[0]).FlatText);
    }

    [Fact]
    public void Empty_and_new_documents_round_trip()
    {
        Document fresh = Document.CreateNew();
        Document loaded = DocxReader.Read(new MemoryStream(DocxWriter.ToBytes(fresh))).Document;
        Assert.Single(loaded.Sections);
        Paragraph only = Assert.IsType<Paragraph>(Assert.Single(loaded.Sections[0].Body));
        Assert.True(only.IsEmpty);
        Assert.Equal(SectionProperties.Letter, loaded.Sections[0].Properties);
    }

    [Fact]
    public void Reads_word_style_documents_with_theme_fonts_complex_fields_and_wrappers()
    {
        using var stream = new MemoryStream();
        using (WordprocessingDocument package = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document))
        {
            MainDocumentPart main = package.AddMainDocumentPart();

            ThemePart theme = main.AddNewPart<ThemePart>();
            theme.Theme = new DocumentFormat.OpenXml.Drawing.Theme(
                new DocumentFormat.OpenXml.Drawing.ThemeElements(
                    new DocumentFormat.OpenXml.Drawing.ColorScheme(
                        new DocumentFormat.OpenXml.Drawing.Dark1Color(new DocumentFormat.OpenXml.Drawing.SystemColor { Val = DocumentFormat.OpenXml.Drawing.SystemColorValues.WindowText, LastColor = "000000" }),
                        new DocumentFormat.OpenXml.Drawing.Light1Color(new DocumentFormat.OpenXml.Drawing.SystemColor { Val = DocumentFormat.OpenXml.Drawing.SystemColorValues.Window, LastColor = "FFFFFF" }),
                        new DocumentFormat.OpenXml.Drawing.Dark2Color(new DocumentFormat.OpenXml.Drawing.RgbColorModelHex { Val = "44546A" }),
                        new DocumentFormat.OpenXml.Drawing.Light2Color(new DocumentFormat.OpenXml.Drawing.RgbColorModelHex { Val = "E7E6E6" }),
                        new DocumentFormat.OpenXml.Drawing.Accent1Color(new DocumentFormat.OpenXml.Drawing.RgbColorModelHex { Val = "4472C4" }),
                        new DocumentFormat.OpenXml.Drawing.Accent2Color(new DocumentFormat.OpenXml.Drawing.RgbColorModelHex { Val = "ED7D31" }),
                        new DocumentFormat.OpenXml.Drawing.Accent3Color(new DocumentFormat.OpenXml.Drawing.RgbColorModelHex { Val = "A5A5A5" }),
                        new DocumentFormat.OpenXml.Drawing.Accent4Color(new DocumentFormat.OpenXml.Drawing.RgbColorModelHex { Val = "FFC000" }),
                        new DocumentFormat.OpenXml.Drawing.Accent5Color(new DocumentFormat.OpenXml.Drawing.RgbColorModelHex { Val = "5B9BD5" }),
                        new DocumentFormat.OpenXml.Drawing.Accent6Color(new DocumentFormat.OpenXml.Drawing.RgbColorModelHex { Val = "70AD47" }),
                        new DocumentFormat.OpenXml.Drawing.Hyperlink(new DocumentFormat.OpenXml.Drawing.RgbColorModelHex { Val = "0563C1" }),
                        new DocumentFormat.OpenXml.Drawing.FollowedHyperlinkColor(new DocumentFormat.OpenXml.Drawing.RgbColorModelHex { Val = "954F72" }))
                    { Name = "Office" },
                    new DocumentFormat.OpenXml.Drawing.FontScheme(
                        new DocumentFormat.OpenXml.Drawing.MajorFont(new DocumentFormat.OpenXml.Drawing.LatinFont { Typeface = "Aptos Display" }, new DocumentFormat.OpenXml.Drawing.EastAsianFont { Typeface = "" }, new DocumentFormat.OpenXml.Drawing.ComplexScriptFont { Typeface = "" }),
                        new DocumentFormat.OpenXml.Drawing.MinorFont(new DocumentFormat.OpenXml.Drawing.LatinFont { Typeface = "Aptos" }, new DocumentFormat.OpenXml.Drawing.EastAsianFont { Typeface = "" }, new DocumentFormat.OpenXml.Drawing.ComplexScriptFont { Typeface = "" }))
                    { Name = "Office" },
                    new DocumentFormat.OpenXml.Drawing.FormatScheme(
                        new DocumentFormat.OpenXml.Drawing.FillStyleList(new DocumentFormat.OpenXml.Drawing.SolidFill(new DocumentFormat.OpenXml.Drawing.SchemeColor { Val = DocumentFormat.OpenXml.Drawing.SchemeColorValues.PhColor }), new DocumentFormat.OpenXml.Drawing.SolidFill(new DocumentFormat.OpenXml.Drawing.SchemeColor { Val = DocumentFormat.OpenXml.Drawing.SchemeColorValues.PhColor }), new DocumentFormat.OpenXml.Drawing.SolidFill(new DocumentFormat.OpenXml.Drawing.SchemeColor { Val = DocumentFormat.OpenXml.Drawing.SchemeColorValues.PhColor })),
                        new DocumentFormat.OpenXml.Drawing.LineStyleList(new DocumentFormat.OpenXml.Drawing.Outline(), new DocumentFormat.OpenXml.Drawing.Outline(), new DocumentFormat.OpenXml.Drawing.Outline()),
                        new DocumentFormat.OpenXml.Drawing.EffectStyleList(new DocumentFormat.OpenXml.Drawing.EffectStyle(new DocumentFormat.OpenXml.Drawing.EffectList()), new DocumentFormat.OpenXml.Drawing.EffectStyle(new DocumentFormat.OpenXml.Drawing.EffectList()), new DocumentFormat.OpenXml.Drawing.EffectStyle(new DocumentFormat.OpenXml.Drawing.EffectList())),
                        new DocumentFormat.OpenXml.Drawing.BackgroundFillStyleList(new DocumentFormat.OpenXml.Drawing.SolidFill(new DocumentFormat.OpenXml.Drawing.SchemeColor { Val = DocumentFormat.OpenXml.Drawing.SchemeColorValues.PhColor }), new DocumentFormat.OpenXml.Drawing.SolidFill(new DocumentFormat.OpenXml.Drawing.SchemeColor { Val = DocumentFormat.OpenXml.Drawing.SchemeColorValues.PhColor }), new DocumentFormat.OpenXml.Drawing.SolidFill(new DocumentFormat.OpenXml.Drawing.SchemeColor { Val = DocumentFormat.OpenXml.Drawing.SchemeColorValues.PhColor })))
                    { Name = "Office" }))
            { Name = "Office Theme" };

            StyleDefinitionsPart styles = main.AddNewPart<StyleDefinitionsPart>();
            styles.Styles = new W.Styles(
                new W.DocDefaults(new W.RunPropertiesDefault(new W.RunPropertiesBaseStyle(new W.RunFonts { AsciiTheme = W.ThemeFontValues.MinorHighAnsi, HighAnsiTheme = W.ThemeFontValues.MinorHighAnsi }, new W.FontSize { Val = "24" }))),
                new W.Style(new W.StyleName { Val = "Normal" }) { Type = W.StyleValues.Paragraph, StyleId = "Normal", Default = true },
                new W.Style(new W.StyleName { Val = "heading 1" }, new W.BasedOn { Val = "Normal" }, new W.StyleRunProperties(new W.RunFonts { AsciiTheme = W.ThemeFontValues.MajorHighAnsi }, new W.Color { Val = "0F4761", ThemeColor = W.ThemeColorValues.Accent1 })) { Type = W.StyleValues.Paragraph, StyleId = "Heading1" });

            // Section 1 ends inside the last paragraph (Word's layout); section 2 ends at the body level.
            var p1 = new W.Paragraph(
                new W.ParagraphProperties(new W.ParagraphStyleId { Val = "Heading1" }),
                new W.Run(new W.Text("Heading ")),
                new W.Hyperlink(new W.Run(new W.RunProperties(new W.RunStyle { Val = "Hyperlink" }), new W.Text("link"))) { Anchor = "top" },
                new W.InsertedRun(new W.Run(new W.Text(" inserted"))) { Id = "1", Author = "a", Date = DateTime.UtcNow },
                new W.DeletedRun(new W.Run(new W.DeletedText(" deleted"))) { Id = "2", Author = "a", Date = DateTime.UtcNow },
                new W.Run(new W.FieldChar { FieldCharType = W.FieldCharValues.Begin }),
                new W.Run(new W.FieldCode(" PAGE \\* MERGEFORMAT ") { Space = SpaceProcessingModeValues.Preserve }),
                new W.Run(new W.FieldChar { FieldCharType = W.FieldCharValues.Separate }),
                new W.Run(new W.RunProperties(new W.Bold()), new W.Text("7")),
                new W.Run(new W.FieldChar { FieldCharType = W.FieldCharValues.End }),
                new W.Run(new W.Text(" end")));
            var p2 = new W.Paragraph(
                new W.ParagraphProperties(
                    new W.SectionProperties(new W.PageSize { Width = 12240, Height = 15840 }, new W.PageMargin { Top = 1440, Right = 1440, Bottom = 1440, Left = 1440, Header = 708, Footer = 708, Gutter = 0 })),
                new W.Run(new W.RunProperties(new W.Color { ThemeColor = W.ThemeColorValues.Accent2 }), new W.Text("theme colored")),
                new W.Run(new W.Drawing()));
            var sdt = new W.SdtBlock(new W.SdtProperties(), new W.SdtContentBlock(new W.Paragraph(new W.Run(new W.Text("inside content control")))));
            var table = new W.Table(new W.TableProperties(), new W.TableGrid(new W.GridColumn { Width = "1000" }), new W.TableRow(new W.TableCell(new W.Paragraph(new W.Run(new W.Text("cell"))))));
            var p3 = new W.Paragraph(new W.Run(new W.Text("last")));
            main.Document = new W.Document(new W.Body(p1, p2, sdt, table, p3, new W.SectionProperties(new W.SectionType { Val = W.SectionMarkValues.Continuous }, new W.PageSize { Width = 11906, Height = 16838 })));
            main.Document.Save();
        }

        stream.Position = 0;
        LoadResult result = DocxReader.Read(stream);
        Document doc = result.Document;

        Assert.Equal(2, doc.Sections.Count);
        Assert.Equal(["Heading link inserted￼ end", "theme colored"], doc.Sections[0].Body.Cast<Paragraph>().Select(p => p.FlatText));
        var heading = (Paragraph)doc.Sections[0].Body[0];
        var field = (Field)heading.Inlines.Single(i => i is Field);
        Assert.Equal(FieldKind.Page, field.Kind);
        Assert.Equal("PAGE \\* MERGEFORMAT", field.Instruction);
        Assert.Equal("7", field.CachedResult);
        Assert.Equal("Hyperlink", heading.Inlines[1].StyleId);

        Assert.Equal("Aptos", doc.Styles.Defaults.Run.FontFamily);
        Assert.Equal("Aptos Display", doc.Styles.Get("Heading1")!.RunProperties.FontFamily);
        Assert.Equal(DocColor.Parse("0F4761"), doc.Styles.Get("Heading1")!.RunProperties.Color);
        Assert.Equal(DocColor.Parse("ED7D31"), ((Paragraph)doc.Sections[0].Body[1]).Inlines[0].Properties.Color);

        Assert.Equal(Twips.FromInches(8.5), doc.Sections[0].Properties.PageWidth);
        Assert.Equal(new Twips(708), doc.Sections[0].Properties.HeaderDistance);
        Assert.Equal(SectionStart.Continuous, doc.Sections[1].Properties.Start);
        Assert.Equal(new Twips(11906), doc.Sections[1].Properties.PageWidth);
        Assert.Equal(["inside content control", "<OpaqueBlock(tbl)>", "last"], doc.Sections[1].Body.Select(b => b is Paragraph p ? p.FlatText : $"<{b}>"));

        Assert.Contains(result.Warnings, w => w.Message.Contains("pictures", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Warnings, w => w.Message.Contains("Tables", StringComparison.Ordinal));
        Assert.True(result.HasLossyContent);
    }

    [Fact]
    public void Universal_measures_and_on_off_values_parse()
    {
        Assert.Equal(Twips.FromInches(0.5), OoxmlValues.TwipsFrom("0.5in"));
        Assert.Equal(Twips.FromPoints(12), OoxmlValues.TwipsFrom("12pt"));
        Assert.Equal(new Twips(720), OoxmlValues.TwipsFrom("720"));
        Assert.Null(OoxmlValues.TwipsFrom("abc"));
        Assert.True(OoxmlValues.OnOff(new W.Bold()));
        Assert.False(OoxmlValues.OnOff(new W.Bold { Val = false }));
        Assert.Null(OoxmlValues.OnOff(null));
        Assert.Equal(FieldKind.NumPages, OoxmlValues.FieldKindFrom("  NUMPAGES  \\* MERGEFORMAT"));
        Assert.Equal(FieldKind.Unknown, OoxmlValues.FieldKindFrom("PAGEREF _Toc1"));
    }

    [Fact]
    public void Write_file_is_atomic_and_readable()
    {
        string path = Path.Combine(Path.GetTempPath(), $"quill-{Guid.NewGuid():N}.docx");
        try
        {
            DocxWriter.WriteFile(Document.CreateNew(), path);
            Assert.True(File.Exists(path));
            Assert.Empty(Directory.GetFiles(Path.GetTempPath(), $".{Path.GetFileName(path)}.*.tmp"));
            LoadResult loaded = DocxReader.ReadFile(path);
            Assert.False(loaded.HasLossyContent);
            Assert.Single(loaded.Document.Sections);
        }
        finally
        {
            File.Delete(path);
        }
    }
}

public class DocxListTests
{
    [Fact]
    public void Lists_round_trip_with_levels_overrides_and_bullets()
    {
        (ListStore store, int bullets) = ListStore.Empty.AddList(DefaultLists.BulletLevels());
        (store, int numbers) = store.AddList(DefaultLists.NumberedLevels());
        store = store.With(new ListInstance(numbers + 1, store.GetInstance(numbers)!.DefinitionId, ImmutableDictionary<int, int>.Empty.Add(0, 7)));
        Block[] blocks =
        [
            new Paragraph([new Run("bullet one")], properties: new ParagraphProperties { List = new ListFormat(bullets, 0) }),
            new Paragraph([new Run("bullet nested")], properties: new ParagraphProperties { List = new ListFormat(bullets, 1) }),
            new Paragraph([new Run("number one")], properties: new ParagraphProperties { List = new ListFormat(numbers, 0) }),
            new Paragraph([new Run("restarted at seven")], properties: new ParagraphProperties { List = new ListFormat(numbers + 1, 0) }),
            new Paragraph([new Run("switched off")], properties: new ParagraphProperties { List = ListFormat.None }),
        ];
        var original = new Document(ImmutableList.Create(new Section(SectionProperties.Letter, blocks.ToImmutableList())), DefaultStyleSheet.Create(), lists: store);

        byte[] bytes = DocxWriter.ToBytes(original);
        using (var stream = new MemoryStream(bytes))
        using (WordprocessingDocument package = WordprocessingDocument.Open(stream, false))
        {
            Assert.NotNull(package.MainDocumentPart!.NumberingDefinitionsPart);
            Assert.Empty(new OpenXmlValidator(FileFormatVersions.Office2019).Validate(package));
        }

        Document doc = DocxReader.Read(new MemoryStream(bytes)).Document;
        Assert.Equal(2, doc.Lists.Definitions.Count);
        Assert.Equal(3, doc.Lists.Instances.Count);
        Assert.Equal(new ListFormat(bullets, 1), ((Paragraph)doc.Sections[0].Body[1]).Properties.List);
        Assert.Equal(ListFormat.None, ((Paragraph)doc.Sections[0].Body[4]).Properties.List);
        Assert.Equal(7, doc.Lists.GetInstance(numbers + 1)!.StartOverride(0));
        ListLevel level1 = doc.Lists.GetLevel(bullets, 1)!;
        Assert.Equal(NumberFormat.Bullet, level1.Format);
        Assert.Equal("o", level1.Text);
        Assert.Equal(Twips.FromInches(1.0), level1.LeftIndent);
        Assert.Equal(Twips.FromInches(0.25), level1.Hanging);
        ListLevel numbered2 = doc.Lists.GetLevel(numbers, 2)!;
        Assert.Equal(NumberFormat.LowerRoman, numbered2.Format);
        Assert.Equal("%3.", numbered2.Text);
        Assert.Equal(Alignment.Right, numbered2.Alignment);

        var resolver = new StyleResolver(doc.Styles, doc.Lists);
        IReadOnlyDictionary<int, ListMarker> markers = ListNumbering.Compute(doc.Sections[0].Body, doc.Lists, resolver);
        Assert.Equal("\u2022", markers[0].Text);
        Assert.Equal("1.", markers[2].Text);
        Assert.Equal("7.", markers[3].Text);
        Assert.False(markers.ContainsKey(4));
    }

    [Fact]
    public void Word_symbol_bullets_map_to_unicode()
    {
        using var stream = new MemoryStream();
        using (WordprocessingDocument package = WordprocessingDocument.Create(stream, DocumentFormat.OpenXml.WordprocessingDocumentType.Document))
        {
            MainDocumentPart main = package.AddMainDocumentPart();
            NumberingDefinitionsPart numbering = main.AddNewPart<NumberingDefinitionsPart>();
            numbering.Numbering = new W.Numbering(
                new W.AbstractNum(
                    new W.Level(
                        new W.StartNumberingValue { Val = 1 },
                        new W.NumberingFormat { Val = W.NumberFormatValues.Bullet },
                        new W.LevelText { Val = "\uF0B7" },
                        new W.LevelJustification { Val = W.LevelJustificationValues.Left },
                        new W.PreviousParagraphProperties(new W.Indentation { Left = "720", Hanging = "360" }),
                        new W.NumberingSymbolRunProperties(new W.RunFonts { Ascii = "Symbol", HighAnsi = "Symbol" }))
                    { LevelIndex = 0 })
                { AbstractNumberId = 0 },
                new W.NumberingInstance(new W.AbstractNumId { Val = 0 }) { NumberID = 1 });
            main.Document = new W.Document(new W.Body(
                new W.Paragraph(new W.ParagraphProperties(new W.NumberingProperties(new W.NumberingLevelReference { Val = 0 }, new W.NumberingId { Val = 1 })), new W.Run(new W.Text("item")))));
            main.Document.Save();
        }

        stream.Position = 0;
        Document doc = DocxReader.Read(stream).Document;
        ListLevel level = doc.Lists.GetLevel(1, 0)!;
        Assert.Equal("\u2022", level.Text);
        Assert.Null(level.MarkerFont);
        Assert.Equal(new Twips(720), level.LeftIndent);
        Assert.Equal(new Twips(360), level.Hanging);
        Assert.Equal(new ListFormat(1, 0), ((Paragraph)doc.Sections[0].Body[0]).Properties.List);
    }
}
