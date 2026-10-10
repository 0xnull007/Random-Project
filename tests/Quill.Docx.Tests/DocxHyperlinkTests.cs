using System.Collections.Immutable;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using Quill.Core.Model;
using Quill.Core.Styles;
using Xunit;
using W = DocumentFormat.OpenXml.Wordprocessing;

namespace Quill.Docx.Tests;

public class DocxHyperlinkTests
{
    [Fact]
    public void Links_round_trip_as_hyperlinks_and_anchors()
    {
        var paragraph = new Paragraph(
        [
            new Run("See "),
            new Run("the site", new RunProperties { Link = "https://example.com/a?b=1" }, DefaultStyleSheet.HyperlinkStyleId),
            new Run(" and "),
            new Run("the top", new RunProperties { Link = "#top" }, DefaultStyleSheet.HyperlinkStyleId),
            new Run("."),
        ]);
        var document = new Document(ImmutableList.Create(new Section(SectionProperties.Letter, ImmutableList.Create<Block>(paragraph))), DefaultStyleSheet.Create());

        byte[] bytes = DocxWriter.ToBytes(document);
        using (var stream = new MemoryStream(bytes))
        using (WordprocessingDocument package = WordprocessingDocument.Open(stream, false))
        {
            List<ValidationErrorInfo> errors = new OpenXmlValidator(FileFormatVersions.Office2019).Validate(package).ToList();
            Assert.True(errors.Count == 0, string.Join("\n", errors.Select(e => e.Description + " @ " + e.Path?.XPath)));
            List<W.Hyperlink> hyperlinks = package.MainDocumentPart!.Document!.Body!.Descendants<W.Hyperlink>().ToList();
            Assert.Equal(2, hyperlinks.Count);
            Assert.Equal("top", hyperlinks[1].Anchor!.Value);
            Assert.Equal("https://example.com/a?b=1", package.MainDocumentPart.HyperlinkRelationships.Single().Uri.OriginalString);
        }

        LoadResult result = DocxReader.Read(new MemoryStream(bytes));
        var read = (Paragraph)result.Document.Sections[0].Body[0];
        Assert.Equal("See the site and the top.", read.FlatText);
        Assert.Equal("https://example.com/a?b=1", read.Inlines[1].Properties.Link);
        Assert.Equal(DefaultStyleSheet.HyperlinkStyleId, read.Inlines[1].StyleId);
        Assert.Equal("#top", read.Inlines[3].Properties.Link);
        Assert.Null(read.Inlines[2].Properties.Link);
    }

    [Fact]
    public void Hyperlink_fields_become_links()
    {
        Assert.Equal("https://x.y/z", DocxReader.HyperlinkTarget(" HYPERLINK \"https://x.y/z\" "));
        Assert.Equal("#there", DocxReader.HyperlinkTarget("HYPERLINK \\l \"there\""));
        Assert.Null(DocxReader.HyperlinkTarget("PAGE \\* MERGEFORMAT"));

        using var memory = new MemoryStream();
        using (WordprocessingDocument package = WordprocessingDocument.Create(memory, WordprocessingDocumentType.Document))
        {
            MainDocumentPart main = package.AddMainDocumentPart();
            main.Document = new W.Document(new W.Body(
                new W.Paragraph(
                    new W.Run(new W.FieldChar { FieldCharType = W.FieldCharValues.Begin }),
                    new W.Run(new W.FieldCode(" HYPERLINK \"https://example.com\" ")),
                    new W.Run(new W.FieldChar { FieldCharType = W.FieldCharValues.Separate }),
                    new W.Run(new W.Text("Example")),
                    new W.Run(new W.FieldChar { FieldCharType = W.FieldCharValues.End }),
                    new W.SimpleField(new W.Run(new W.Text("Simple"))) { Instruction = " HYPERLINK \"https://simple.test\" " })));
            main.Document.Save();
        }

        LoadResult result = DocxReader.Read(new MemoryStream(memory.ToArray()));
        var paragraph = (Paragraph)result.Document.Sections[0].Body[0];
        Assert.Equal("ExampleSimple", paragraph.FlatText);
        Assert.Equal("https://example.com", paragraph.Inlines[0].Properties.Link);
        Assert.Equal("https://simple.test", paragraph.Inlines[1].Properties.Link);
    }
}
