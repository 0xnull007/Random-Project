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

public class DocxBookmarkTests
{
    [Fact]
    public void Bookmarks_and_cross_references_round_trip()
    {
        var session = new EditingSession(new Document(ImmutableList.Create(new Section(SectionProperties.Letter, ImmutableList.Create<Block>(Paragraph.FromText("Intro words here"), Paragraph.FromText("See ")))), DefaultStyleSheet.Create()));
        session.MoveCaret(new TextPosition(StoryId.Body(0), BlockPath.Of(0), 6), extend: false);
        session.MoveCaret(new TextPosition(StoryId.Body(0), BlockPath.Of(0), 11), extend: true);
        Assert.True(session.AddBookmark("Target"));
        session.MoveCaret(new TextPosition(StoryId.Body(0), BlockPath.Of(1), 4), extend: false);
        session.InsertCrossReference("Target", pageNumber: false, asLink: true);
        session.InsertCrossReference("Target", pageNumber: true, asLink: false, pageText: "3");

        byte[] bytes = DocxWriter.ToBytes(session.Document);
        using (var stream = new MemoryStream(bytes))
        using (WordprocessingDocument package = WordprocessingDocument.Open(stream, false))
        {
            List<ValidationErrorInfo> errors = new OpenXmlValidator(FileFormatVersions.Office2019).Validate(package).ToList();
            Assert.True(errors.Count == 0, string.Join("\n", errors.Select(e => e.Description + " @ " + e.Path?.XPath)));
            W.Body body = package.MainDocumentPart!.Document!.Body!;
            W.BookmarkStart start = body.Descendants<W.BookmarkStart>().Single();
            Assert.Equal("Target", start.Name!.Value);
            Assert.Equal(start.Id!.Value, body.Descendants<W.BookmarkEnd>().Single().Id!.Value);
            Assert.Equal(2, body.Descendants<W.SimpleField>().Count());
            Assert.Equal("Target", body.Descendants<W.Hyperlink>().Single().Anchor!.Value);
        }

        LoadResult reread = DocxReader.Read(new MemoryStream(bytes));
        Assert.Equal("words", BookmarkSupport.TextOf(reread.Document, "Target"));
        var second = (Paragraph)reread.Document.Sections[0].Body[1];
        List<Field> fields = second.Inlines.OfType<Field>().ToList();
        Assert.Equal([FieldKind.Ref, FieldKind.PageRef], fields.Select(f => f.Kind));
        Assert.Equal("words", fields[0].CachedResult);
        Assert.Equal("#Target", fields[0].Properties.Link);
        Assert.Equal("3", fields[1].CachedResult);
        Assert.Equal("Target", BookmarkSupport.Target(fields[1]));
    }
}
