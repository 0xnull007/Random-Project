using System.Collections.Immutable;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using Quill.Core.Model;
using Quill.Core.Styles;
using Quill.Core.Units;
using Xunit;

namespace Quill.Docx.Tests;

public class DocxCellAlignmentTests
{
    [Fact]
    public void Cell_vertical_alignment_round_trips_as_vAlign()
    {
        TableCell Cell(string text, CellVerticalAlignment alignment) => new(ImmutableList.Create<Block>(Paragraph.FromText(text)), verticalAlignment: alignment);
        var table = new Table(
            [new TableRow([Cell("top", CellVerticalAlignment.Top), Cell("middle", CellVerticalAlignment.Center), Cell("bottom", CellVerticalAlignment.Bottom)], Twips.FromInches(1))],
            [Twips.FromInches(2), Twips.FromInches(2), Twips.FromInches(2)],
            hasBorders: true,
            Twips.Zero);
        var document = new Document(ImmutableList.Create(new Section(SectionProperties.Letter, ImmutableList.Create<Block>(Paragraph.FromText("Before"), table))), DefaultStyleSheet.Create());

        byte[] bytes = DocxWriter.ToBytes(document);
        using (var stream = new MemoryStream(bytes))
        using (WordprocessingDocument package = WordprocessingDocument.Open(stream, false))
        {
            List<ValidationErrorInfo> errors = new OpenXmlValidator(FileFormatVersions.Office2019).Validate(package).ToList();
            Assert.True(errors.Count == 0, string.Join("\n", errors.Select(e => e.Description + " @ " + e.Path?.XPath)));
            List<DocumentFormat.OpenXml.Wordprocessing.TableCell> cells = package.MainDocumentPart!.Document!.Descendants<DocumentFormat.OpenXml.Wordprocessing.TableCell>().ToList();
            Assert.Null(cells[0].TableCellProperties?.TableCellVerticalAlignment);
            Assert.Equal(DocumentFormat.OpenXml.Wordprocessing.TableVerticalAlignmentValues.Center, cells[1].TableCellProperties!.TableCellVerticalAlignment!.Val!.Value);
            Assert.Equal(DocumentFormat.OpenXml.Wordprocessing.TableVerticalAlignmentValues.Bottom, cells[2].TableCellProperties!.TableCellVerticalAlignment!.Val!.Value);
        }

        LoadResult result = DocxReader.Read(new MemoryStream(bytes));
        var read = Assert.IsType<Table>(result.Document.Sections[0].Body[1]);
        Assert.Equal(
            [CellVerticalAlignment.Top, CellVerticalAlignment.Center, CellVerticalAlignment.Bottom],
            read.Rows[0].Cells.Select(c => c.VerticalAlignment));
    }
}
