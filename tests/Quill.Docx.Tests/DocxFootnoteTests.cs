using System.Collections.Immutable;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using Quill.Core.Model;
using Quill.Core.Styles;
using Xunit;
using W = DocumentFormat.OpenXml.Wordprocessing;

namespace Quill.Docx.Tests;

public class DocxFootnoteTests
{
    [Fact]
    public void Footnotes_round_trip()
    {
        var body = new Paragraph([new Run("Text"), new FootnoteReference(3, false, null, FootnoteSupport.ReferenceStyleId), new Run(" more")]);
        var note = ImmutableList.Create<Block>(new Paragraph([new FootnoteReference(3, true, null, FootnoteSupport.ReferenceStyleId), new Run(" The note.")], FootnoteSupport.TextStyleId));
        var document = new Document(
            ImmutableList.Create(new Section(SectionProperties.Letter, ImmutableList.Create<Block>(body))),
            DefaultStyleSheet.Create(),
            footnotes: ImmutableDictionary<int, ImmutableList<Block>>.Empty.Add(3, note).Add(9, note));

        byte[] bytes = DocxWriter.ToBytes(document);
        using (var stream = new MemoryStream(bytes))
        using (WordprocessingDocument package = WordprocessingDocument.Open(stream, false))
        {
            List<ValidationErrorInfo> errors = new OpenXmlValidator(FileFormatVersions.Office2019).Validate(package).ToList();
            Assert.True(errors.Count == 0, string.Join("\n", errors.Select(e => e.Description + " @ " + e.Path?.XPath)));
            W.Footnotes footnotes = package.MainDocumentPart!.FootnotesPart!.Footnotes!;
            Assert.Equal(3, footnotes.Elements<W.Footnote>().Count()); // separator, continuation, and the one referenced note
            Assert.Single(package.MainDocumentPart.Document!.Body!.Descendants<W.FootnoteReference>());
        }

        LoadResult result = DocxReader.Read(new MemoryStream(bytes));
        Assert.Empty(result.Warnings);
        var paragraph = (Paragraph)result.Document.Sections[0].Body[0];
        var reference = Assert.IsType<FootnoteReference>(paragraph.Inlines[1]);
        Assert.False(reference.IsNoteMark);
        ImmutableList<Block> read = result.Document.Footnotes[reference.NoteId];
        var noteParagraph = (Paragraph)read[0];
        Assert.True(((FootnoteReference)noteParagraph.Inlines[0]).IsNoteMark);
        Assert.Equal("￼ The note.", noteParagraph.FlatText);
        Assert.Equal(FootnoteSupport.TextStyleId, noteParagraph.StyleId);
        Assert.Single(result.Document.Footnotes);
    }
}
