using System.Collections.Immutable;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using Quill.Core.Editing;
using Quill.Core.Model;
using Quill.Core.Styles;
using Quill.Core.Text;
using Xunit;
using W = DocumentFormat.OpenXml.Wordprocessing;

namespace Quill.Docx.Tests;

public class DocxCaptionTests
{
    [Fact]
    public void Captions_round_trip_as_seq_fields_with_their_current_numbers()
    {
        var session = new EditingSession(new Document(ImmutableList.Create(new Section(SectionProperties.Letter, ImmutableList.Create<Block>(Paragraph.FromText("Picture A"), Paragraph.FromText("Picture B")))), DefaultStyleSheet.Create()));
        session.MoveCaret(new TextPosition(StoryId.Body(0), BlockPath.Of(1), 0), extend: false);
        session.InsertCaption("Figure", "B", above: false);
        session.MoveCaret(new TextPosition(StoryId.Body(0), BlockPath.Of(0), 0), extend: false);
        session.InsertCaption("Figure", "A", above: false);

        byte[] bytes = DocxWriter.ToBytes(session.Document);
        using (var stream = new MemoryStream(bytes))
        using (WordprocessingDocument package = WordprocessingDocument.Open(stream, false))
        {
            List<ValidationErrorInfo> errors = new OpenXmlValidator(FileFormatVersions.Office2019).Validate(package).ToList();
            Assert.True(errors.Count == 0, string.Join("\n", errors.Select(e => e.Description + " @ " + e.Path?.XPath)));
            List<W.SimpleField> fields = package.MainDocumentPart!.Document!.Body!.Descendants<W.SimpleField>().ToList();
            Assert.Equal(2, fields.Count);
            Assert.All(fields, f => Assert.Contains("SEQ Figure", f.Instruction!.Value!, StringComparison.Ordinal));
            Assert.Equal(["1", "2"], fields.Select(f => f.InnerText));
        }

        LoadResult reread = DocxReader.Read(new MemoryStream(bytes));
        var second = (Paragraph)reread.Document.Sections[0].Body[3];
        var field = Assert.Single(second.Inlines.OfType<Field>());
        Assert.Equal(FieldKind.Seq, field.Kind);
        Assert.Equal("Figure", SequenceFields.Identifier(field));
        Assert.Equal("2", field.CachedResult);
        Assert.Equal(CaptionSupport.StyleId, second.StyleId);
        Assert.Equal(2, SequenceFields.Numbers(reread.Document)[field]);
    }
}
