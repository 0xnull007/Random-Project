using System.Collections.Immutable;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using Quill.Core.Model;
using Quill.Core.Styles;
using Quill.Core.Units;
using Xunit;
using W = DocumentFormat.OpenXml.Wordprocessing;

namespace Quill.Docx.Tests;

public class DocxDesignTests
{
    [Fact]
    public void Page_colour_and_page_borders_round_trip()
    {
        var box = new PageBorders { Style = PageBorderStyle.Dashed, Width = 12, Color = DocColor.Parse("C00000"), Space = 10, FromText = true };
        var section = new Section(SectionProperties.Letter with { PageBorders = box }, ImmutableList.Create<Block>(Paragraph.FromText("x")));
        var document = new Document(ImmutableList.Create(section), DefaultStyleSheet.Create(), new DocumentSettings { PageColor = DocColor.Parse("FFF2CC") });

        byte[] bytes = DocxWriter.ToBytes(document);
        using (var stream = new MemoryStream(bytes))
        using (WordprocessingDocument package = WordprocessingDocument.Open(stream, false))
        {
            List<ValidationErrorInfo> errors = new OpenXmlValidator(FileFormatVersions.Office2019).Validate(package).ToList();
            Assert.True(errors.Count == 0, string.Join("\n", errors.Select(e => e.Description + " @ " + e.Path?.XPath)));
            Assert.Equal("FFF2CC", package.MainDocumentPart!.Document!.GetFirstChild<W.DocumentBackground>()!.Color!.Value);
            Assert.NotNull(package.MainDocumentPart.DocumentSettingsPart!.Settings!.GetFirstChild<W.DisplayBackgroundShape>());
            W.PageBorders borders = package.MainDocumentPart.Document.Body!.Descendants<W.PageBorders>().Single();
            Assert.Equal(W.PageBorderOffsetValues.Text, borders.OffsetFrom!.Value);
            Assert.Equal(12U, borders.TopBorder!.Size!.Value);
        }

        LoadResult reread = DocxReader.Read(new MemoryStream(bytes));
        Assert.Equal(DocColor.Parse("FFF2CC"), reread.Document.Settings.PageColor);
        Assert.Equal(box, reread.Document.Sections[0].Properties.PageBorders);
    }

    [Fact]
    public void Paragraph_shading_and_borders_round_trip()
    {
        var borders = new ParagraphBorders(Top: new BorderLine(PageBorderStyle.Double, 6, DocColor.Parse("0070C0"), 2), Bottom: new BorderLine());
        var paragraph = new Paragraph([new Run("boxed")], properties: new ParagraphProperties { Shading = DocColor.Parse("FFF2CC"), Borders = borders });
        var cleared = new Paragraph([new Run("plain")], properties: new ParagraphProperties { Shading = DocColor.Auto, Borders = ParagraphBorders.None });
        var document = new Document(ImmutableList.Create(new Section(SectionProperties.Letter, ImmutableList.Create<Block>(paragraph, cleared))), DefaultStyleSheet.Create());

        byte[] bytes = DocxWriter.ToBytes(document);
        using (var stream = new MemoryStream(bytes))
        using (WordprocessingDocument package = WordprocessingDocument.Open(stream, false))
        {
            List<ValidationErrorInfo> errors = new OpenXmlValidator(FileFormatVersions.Office2019).Validate(package).ToList();
            Assert.True(errors.Count == 0, string.Join("\n", errors.Select(e => e.Description + " @ " + e.Path?.XPath)));
        }

        LoadResult reread = DocxReader.Read(new MemoryStream(bytes));
        var first = (Paragraph)reread.Document.Sections[0].Body[0];
        Assert.Equal(DocColor.Parse("FFF2CC"), first.Properties.Shading);
        Assert.Equal(borders, first.Properties.Borders);
        var second = (Paragraph)reread.Document.Sections[0].Body[1];
        Assert.True(second.Properties.Shading!.Value.IsAuto);
        Assert.True(second.Properties.Borders!.IsNone);
    }

    [Fact]
    public void Watermark_round_trips_as_a_vml_shape_in_the_header()
    {
        var document = new Document(ImmutableList.Create(new Section(SectionProperties.Letter, ImmutableList.Create<Block>(Paragraph.FromText("x")))), DefaultStyleSheet.Create(), new DocumentSettings { Watermark = new Watermark("CONFIDENTIAL", DocColor.Parse("FF0000")) });
        byte[] bytes = DocxWriter.ToBytes(document);
        using (var stream = new MemoryStream(bytes))
        using (WordprocessingDocument package = WordprocessingDocument.Open(stream, false))
        {
            List<ValidationErrorInfo> errors = new OpenXmlValidator(FileFormatVersions.Office2019).Validate(package).ToList();
            Assert.True(errors.Count == 0, string.Join("\n", errors.Select(e => e.Description + " @ " + e.Path?.XPath)));
            HeaderPart header = Assert.Single(package.MainDocumentPart!.HeaderParts);
            DocumentFormat.OpenXml.Vml.TextPath path = header.Header!.Descendants<DocumentFormat.OpenXml.Vml.TextPath>().Single(p => p.String is not null);
            Assert.Equal("CONFIDENTIAL", path.String!.Value);
            Assert.Equal("#ff0000", header.Header.Descendants<DocumentFormat.OpenXml.Vml.Shape>().Single().FillColor!.Value);
        }

        LoadResult reread = DocxReader.Read(new MemoryStream(bytes));
        Assert.Equal(new Watermark("CONFIDENTIAL", DocColor.Parse("FF0000")), reread.Document.Settings.Watermark);
        Assert.Empty(reread.Warnings);
        Assert.NotNull(reread.Document.Sections[0].Headers.Default);
    }

    [Fact]
    public void Drop_caps_round_trip_as_Words_framed_letter_paragraph()
    {
        var dropped = new Paragraph([new Run("Once upon a time", new RunProperties { Italic = true }), new Run(" there was")], properties: new ParagraphProperties { DropCap = DropCap.Dropped(3) });
        var inMargin = new Paragraph([new Run("Margin text")], properties: new ParagraphProperties { DropCap = new DropCap(DropCapPosition.InMargin, 2, new Twips(100)) });
        var document = new Document(ImmutableList.Create(new Section(SectionProperties.Letter, ImmutableList.Create<Block>(dropped, inMargin, Paragraph.FromText("Plain")))), DefaultStyleSheet.Create());

        byte[] bytes = DocxWriter.ToBytes(document);
        using (var stream = new MemoryStream(bytes))
        using (WordprocessingDocument package = WordprocessingDocument.Open(stream, false))
        {
            List<ValidationErrorInfo> errors = new OpenXmlValidator(FileFormatVersions.Office2019).Validate(package).ToList();
            Assert.True(errors.Count == 0, string.Join("\n", errors.Select(e => e.Description + " @ " + e.Path?.XPath)));
            List<DocumentFormat.OpenXml.Wordprocessing.Paragraph> paragraphs = package.MainDocumentPart!.Document!.Body!.Elements<DocumentFormat.OpenXml.Wordprocessing.Paragraph>().ToList();
            Assert.Equal(5, paragraphs.Count); // letter + rest, letter + rest, plain
            var frame = paragraphs[0].ParagraphProperties!.FrameProperties!;
            Assert.Equal(DocumentFormat.OpenXml.Wordprocessing.DropCapLocationValues.Drop, frame.DropCap!.Value);
            Assert.Equal(3, frame.Lines!.Value);
            Assert.Equal("O", paragraphs[0].InnerText);
            Assert.Equal("nce upon a time there was", paragraphs[1].InnerText);
            Assert.Null(paragraphs[1].ParagraphProperties?.FrameProperties);
            Assert.Equal("100", paragraphs[2].ParagraphProperties!.FrameProperties!.HorizontalSpace!.Value);
        }

        LoadResult reread = DocxReader.Read(new MemoryStream(bytes));
        Assert.Empty(reread.Warnings);
        Assert.Equal(3, reread.Document.Sections[0].Body.Count);
        var first = (Paragraph)reread.Document.Sections[0].Body[0];
        Assert.Equal("Once upon a time there was", first.FlatText);
        Assert.Equal(DropCap.Dropped(3), first.Properties.DropCap);
        Assert.True(first.Inlines[0].Properties.Italic);
        Assert.Null(first.Inlines[0].Properties.FontSize);
        var second = (Paragraph)reread.Document.Sections[0].Body[1];
        Assert.Equal("Margin text", second.FlatText);
        Assert.Equal(new DropCap(DropCapPosition.InMargin, 2, new Twips(100)), second.Properties.DropCap);
        Assert.Null(((Paragraph)reread.Document.Sections[0].Body[2]).Properties.DropCap);
    }

    [Fact]
    public void Line_numbering_round_trips()
    {
        var numbering = new LineNumbering { CountBy = 5, Start = 1, Distance = Twips.FromInches(0.3), Restart = LineNumberRestart.EachPage };
        var paragraph = Paragraph.FromText("skip").WithProperties(new ParagraphProperties { SuppressLineNumbers = true, SuppressAutoHyphens = true });
        var document = new Document(ImmutableList.Create(new Section(SectionProperties.Letter with { LineNumbers = numbering }, ImmutableList.Create<Block>(paragraph, Paragraph.FromText("count")))), DefaultStyleSheet.Create());
        byte[] bytes = DocxWriter.ToBytes(document);
        using (var stream = new MemoryStream(bytes))
        using (WordprocessingDocument package = WordprocessingDocument.Open(stream, false))
        {
            List<ValidationErrorInfo> errors = new OpenXmlValidator(FileFormatVersions.Office2019).Validate(package).ToList();
            Assert.True(errors.Count == 0, string.Join("\n", errors.Select(e => e.Description + " @ " + e.Path?.XPath)));
        }

        LoadResult reread = DocxReader.Read(new MemoryStream(bytes));
        Assert.Equal(numbering, reread.Document.Sections[0].Properties.LineNumbers);
        Assert.True(((Paragraph)reread.Document.Sections[0].Body[0]).Properties.SuppressLineNumbers);
        Assert.True(((Paragraph)reread.Document.Sections[0].Body[0]).Properties.SuppressAutoHyphens);
        Assert.Null(((Paragraph)reread.Document.Sections[0].Body[1]).Properties.SuppressLineNumbers);
        Assert.Null(((Paragraph)reread.Document.Sections[0].Body[1]).Properties.SuppressAutoHyphens);
    }

    [Fact]
    public void Templates_are_written_with_the_template_package_type_and_read_back()
    {
        var document = new Document(ImmutableList.Create(new Section(SectionProperties.Letter, ImmutableList.Create<Block>(Paragraph.FromText("Letterhead")))), DefaultStyleSheet.Create());
        using var memory = new MemoryStream();
        DocxWriter.Write(document, memory, template: true);
        memory.Position = 0;
        using (WordprocessingDocument package = WordprocessingDocument.Open(memory, false))
        {
            Assert.Equal(WordprocessingDocumentType.Template, package.DocumentType);
        }

        LoadResult reread = DocxReader.Read(new MemoryStream(memory.ToArray()));
        Assert.Equal("Letterhead", ((Paragraph)reread.Document.Sections[0].Body[0]).FlatText);

        string path = Path.Combine(Path.GetTempPath(), "quill-" + Guid.NewGuid().ToString("N") + ".dotx");
        try
        {
            DocxWriter.WriteFile(document, path);
            using WordprocessingDocument file = WordprocessingDocument.Open(path, false);
            Assert.Equal(WordprocessingDocumentType.Template, file.DocumentType);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
