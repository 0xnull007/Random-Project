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

public class DocxTrackChangesTests
{
    [Fact]
    public void Tracked_insertions_and_deletions_round_trip_with_the_tracking_switch()
    {
        var document = new Document(ImmutableList.Create(new Section(SectionProperties.Letter, ImmutableList.Create<Block>(Paragraph.FromText("red car")))), DefaultStyleSheet.Create());
        var session = new EditingSession(document) { Author = "Ada", Now = () => new DateTime(2026, 10, 11, 9, 30, 0) };
        session.SetTrackChanges(true);
        session.MoveCaret(new TextPosition(StoryId.Body(0), BlockPath.Of(0), 0), extend: false);
        session.MoveCaret(new TextPosition(StoryId.Body(0), BlockPath.Of(0), 3), extend: true);
        session.InsertText("blue");

        byte[] bytes = DocxWriter.ToBytes(session.Document);
        using (var stream = new MemoryStream(bytes))
        using (WordprocessingDocument package = WordprocessingDocument.Open(stream, false))
        {
            List<ValidationErrorInfo> errors = new OpenXmlValidator(FileFormatVersions.Office2019).Validate(package).ToList();
            Assert.True(errors.Count == 0, string.Join("\n", errors.Select(e => e.Description + " @ " + e.Path?.XPath)));
            W.Body body = package.MainDocumentPart!.Document!.Body!;
            W.DeletedRun deleted = body.Descendants<W.DeletedRun>().Single();
            W.InsertedRun inserted = body.Descendants<W.InsertedRun>().Single();
            Assert.Equal("Ada", deleted.Author?.Value);
            Assert.Equal("Ada", inserted.Author?.Value);
            Assert.Equal("red", deleted.Descendants<W.DeletedText>().Single().Text);
            Assert.Empty(deleted.Descendants<W.Text>());
            Assert.Equal("blue", inserted.Descendants<W.Text>().Single().Text);
            Assert.NotNull(package.MainDocumentPart!.DocumentSettingsPart!.Settings!.GetFirstChild<W.TrackRevisions>());
        }

        LoadResult reread = DocxReader.Read(new MemoryStream(bytes));
        Assert.True(reread.Document.Settings.TrackChanges);
        var paragraph = (Paragraph)reread.Document.Sections[0].Body[0];
        Assert.Equal("redblue car", paragraph.FlatText);
        Assert.Equal(
            [("red", RevisionKind.Deletion), ("blue", RevisionKind.Insertion), (" car", null)],
            paragraph.Inlines.Select(i => (((Run)i).Text, i.Properties.Revision?.Kind)));
        Assert.Equal("Ada", paragraph.Inlines[0].Properties.Revision!.Author);
        Assert.Equal(new DateTime(2026, 10, 11, 9, 30, 0), paragraph.Inlines[0].Properties.Revision!.Date.ToUniversalTime());
    }
}
