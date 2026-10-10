using System.Collections.Immutable;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using Quill.Core.Model;
using Quill.Core.Styles;
using Xunit;
using W = DocumentFormat.OpenXml.Wordprocessing;

namespace Quill.Docx.Tests;

public class DocxCommentTests
{
    [Fact]
    public void Comments_round_trip_with_their_ranges()
    {
        var first = new Paragraph([new Run("Plain "), new Run("commented", new RunProperties { CommentId = 4 }), new Run(" end")]);
        var second = new Paragraph([new Run("also", new RunProperties { CommentId = 4 }), new Run(" done")]);
        var comment = new Comment(4, "Reviewer One", "RO", new DateTimeOffset(2026, 10, 10, 12, 0, 0, TimeSpan.Zero), ImmutableList.Create<Block>(Paragraph.FromText("Check this", CommentSupport.TextStyleId)));
        var document = new Document(
            ImmutableList.Create(new Section(SectionProperties.Letter, ImmutableList.Create<Block>(first, second))),
            DefaultStyleSheet.Create(),
            comments: ImmutableDictionary<int, Comment>.Empty.Add(4, comment));

        byte[] bytes = DocxWriter.ToBytes(document);
        using (var stream = new MemoryStream(bytes))
        using (WordprocessingDocument package = WordprocessingDocument.Open(stream, false))
        {
            List<ValidationErrorInfo> errors = new OpenXmlValidator(FileFormatVersions.Office2019).Validate(package).ToList();
            Assert.True(errors.Count == 0, string.Join("\n", errors.Select(e => e.Description + " @ " + e.Path?.XPath)));
            W.Body body = package.MainDocumentPart!.Document!.Body!;
            Assert.Single(body.Descendants<W.CommentRangeStart>());
            Assert.Single(body.Descendants<W.CommentRangeEnd>());
            Assert.Single(body.Descendants<W.CommentReference>());
            Assert.Single(package.MainDocumentPart.WordprocessingCommentsPart!.Comments!.Elements<W.Comment>());
        }

        LoadResult result = DocxReader.Read(new MemoryStream(bytes));
        Comment read = Assert.Single(result.Document.Comments.Values);
        Assert.Equal("Reviewer One", read.Author);
        Assert.Equal("Check this", read.Text);
        var p1 = (Paragraph)result.Document.Sections[0].Body[0];
        var p2 = (Paragraph)result.Document.Sections[0].Body[1];
        // A docx comment range is contiguous: everything from the first marked run to the last belongs to it.
        Assert.Equal("commented end", string.Concat(p1.Inlines.Where(i => i.Properties.CommentId == read.Id).Select(i => ((Run)i).Text)));
        Assert.Null(p1.Inlines[0].Properties.CommentId);
        Assert.Equal(read.Id, p2.Inlines[0].Properties.CommentId);
        Assert.Null(p2.Inlines[1].Properties.CommentId);
    }
}

