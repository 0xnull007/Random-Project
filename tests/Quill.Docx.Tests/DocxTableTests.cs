using System.Collections.Immutable;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using Quill.Core.Editing;
using Quill.Core.Model;
using Quill.Core.Styles;
using Quill.Core.Text;
using Quill.Core.Units;
using Xunit;
using W = DocumentFormat.OpenXml.Wordprocessing;

namespace Quill.Docx.Tests;

public class DocxTableTests
{
    private const string TableXml =
        "<w:tbl xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\">"
        + "<w:tblPr><w:tblStyle w:val=\"TableGrid\"/><w:tblW w:w=\"0\" w:type=\"auto\"/><w:tblInd w:w=\"720\" w:type=\"dxa\"/></w:tblPr>"
        + "<w:tblGrid><w:gridCol w:w=\"3000\"/><w:gridCol w:w=\"3000\"/><w:gridCol w:w=\"3000\"/></w:tblGrid>"
        + "<w:tr><w:trPr><w:trHeight w:val=\"600\" w:hRule=\"exact\"/></w:trPr>"
        + "<w:tc><w:tcPr><w:gridSpan w:val=\"2\"/><w:shd w:val=\"clear\" w:color=\"auto\" w:fill=\"D9E2F3\"/></w:tcPr><w:p><w:r><w:t>Head</w:t></w:r></w:p></w:tc>"
        + "<w:tc><w:p><w:r><w:t>Right</w:t></w:r></w:p></w:tc></w:tr>"
        + "<w:tr><w:tc><w:tcPr><w:vMerge w:val=\"restart\"/></w:tcPr><w:p><w:r><w:t>A</w:t></w:r></w:p></w:tc>"
        + "<w:tc><w:p><w:r><w:t>B</w:t></w:r></w:p><w:p><w:r><w:t>B2</w:t></w:r></w:p></w:tc>"
        + "<w:tc><w:p/></w:tc></w:tr>"
        + "<w:tr><w:tc><w:tcPr><w:vMerge/></w:tcPr><w:p/></w:tc><w:tc><w:p><w:r><w:t>C</w:t></w:r></w:p></w:tc><w:tc><w:p><w:r><w:t>D</w:t></w:r></w:p></w:tc></w:tr>"
        + "</w:tbl>";

    private static byte[] PackageWithTable(bool gridStyleHasBorders)
    {
        using var memory = new MemoryStream();
        using (WordprocessingDocument package = WordprocessingDocument.Create(memory, WordprocessingDocumentType.Document))
        {
            MainDocumentPart main = package.AddMainDocumentPart();
            main.Document = new W.Document(new W.Body(new W.Paragraph(new W.Run(new W.Text("before"))), new W.Table(TableXml), new W.Paragraph(new W.Run(new W.Text("after")))));
            StyleDefinitionsPart styles = main.AddNewPart<StyleDefinitionsPart>();
            var style = new W.Style { Type = W.StyleValues.Table, StyleId = "TableGrid" };
            style.Append(new W.StyleName { Val = "Table Grid" });
            if (gridStyleHasBorders)
            {
                style.Append(new W.StyleTableProperties(new W.TableBorders(
                    new W.TopBorder { Val = W.BorderValues.Single, Size = 4U },
                    new W.InsideHorizontalBorder { Val = W.BorderValues.Single, Size = 4U })));
            }

            styles.Styles = new W.Styles(style);
            main.Document.Save();
        }

        return memory.ToArray();
    }

    [Fact]
    public void Table_is_read_for_display_and_written_back_unchanged()
    {
        LoadResult result = DocxReader.Read(new MemoryStream(PackageWithTable(gridStyleHasBorders: true)));
        Assert.DoesNotContain(result.Warnings, w => w.Message.Contains("Table", StringComparison.OrdinalIgnoreCase));
        var table = Assert.IsType<Table>(result.Document.Sections[0].Body[1]);
        Assert.Equal(3, table.Rows.Length);
        Assert.Equal(3, table.ColumnCount);
        Assert.Equal(new Twips(3000), table.ColumnWidths[0]);
        Assert.Equal(new Twips(720), table.Indent);
        Assert.True(table.HasBorders);

        TableRow first = table.Rows[0];
        Assert.Equal(new Twips(600), first.Height);
        Assert.True(first.ExactHeight);
        Assert.Equal(2, first.Cells[0].GridSpan);
        Assert.Equal(DocColor.Parse("D9E2F3"), first.Cells[0].Shading);
        Assert.Equal("Head", ((Paragraph)first.Cells[0].Blocks[0]).FlatText);
        Assert.Equal(VerticalMerge.Restart, table.Rows[1].Cells[0].VerticalMerge);
        Assert.Equal(VerticalMerge.Continue, table.Rows[2].Cells[0].VerticalMerge);
        Assert.Equal(2, table.Rows[1].Cells[1].Blocks.Count);
        Assert.Equal(["Head", "Right", "A", "B", "B2", "", "", "C", "D"], table.AllParagraphs().Select(p => p.FlatText));

        byte[] written = DocxWriter.ToBytes(result.Document);
        using var stream = new MemoryStream(written);
        using WordprocessingDocument package = WordprocessingDocument.Open(stream, false);
        W.Table writtenTable = package.MainDocumentPart!.Document!.Body!.Elements<W.Table>().Single();
        Assert.Equal(3, writtenTable.Elements<W.TableRow>().Count());
        Assert.Contains("D9E2F3", writtenTable.OuterXml, StringComparison.Ordinal);
    }

    [Fact]
    public void Borders_come_from_the_table_style_when_the_table_sets_none()
    {
        LoadResult plain = DocxReader.Read(new MemoryStream(PackageWithTable(gridStyleHasBorders: false)));
        Assert.False(((Table)plain.Document.Sections[0].Body[1]).HasBorders);
    }

    [Fact]
    public void Tables_built_in_code_are_written_as_valid_xml()
    {
        var cell = new TableCell(ImmutableList.Create<Block>(Paragraph.FromText("x")), shading: DocColor.Parse("FFFF00"));
        var table = new Table(
            [new TableRow([cell, new TableCell(ImmutableList.Create<Block>(Paragraph.FromText("y")))], Twips.FromPoints(20))],
            [Twips.FromInches(2), Twips.FromInches(3)],
            hasBorders: true,
            Twips.Zero);
        var document = new Document(ImmutableList.Create(new Section(SectionProperties.Letter, ImmutableList.Create<Block>(table, Paragraph.FromText("after")))), DefaultStyleSheet.Create());
        byte[] bytes = DocxWriter.ToBytes(document);
        using var stream = new MemoryStream(bytes);
        using WordprocessingDocument package = WordprocessingDocument.Open(stream, false);
        List<ValidationErrorInfo> errors = new OpenXmlValidator(FileFormatVersions.Office2019).Validate(package).ToList();
        Assert.True(errors.Count == 0, string.Join("\n", errors.Select(e => e.Description + " @ " + e.Path?.XPath)));

        LoadResult reread = DocxReader.Read(new MemoryStream(bytes));
        var read = Assert.IsType<Table>(reread.Document.Sections[0].Body[0]);
        Assert.Equal(2, read.ColumnCount);
        Assert.True(read.HasBorders);
        Assert.Equal(DocColor.Parse("FFFF00"), read.Rows[0].Cells[0].Shading);
    }

    [Fact]
    public void Edited_tables_are_regenerated_from_the_model()
    {
        LoadResult loaded = DocxReader.Read(new MemoryStream(PackageWithTable(gridStyleHasBorders: true)));
        Assert.NotNull(((Table)loaded.Document.Sections[0].Body[1]).OuterXml);

        var at = new TextPosition(StoryId.Body(0), BlockPath.Of(1).Child(0, 0, 0), 4);
        Document edited = DocumentEditor.InsertText(loaded.Document, at, " edited").Document;
        Assert.Null(((Table)edited.Sections[0].Body[1]).OuterXml);

        byte[] bytes = DocxWriter.ToBytes(edited);
        using (var stream = new MemoryStream(bytes))
        using (WordprocessingDocument package = WordprocessingDocument.Open(stream, false))
        {
            List<ValidationErrorInfo> errors = new OpenXmlValidator(FileFormatVersions.Office2019).Validate(package).ToList();
            Assert.True(errors.Count == 0, string.Join("\n", errors.Select(e => e.Description + " @ " + e.Path?.XPath)));
        }

        LoadResult reread = DocxReader.Read(new MemoryStream(bytes));
        var read = Assert.IsType<Table>(reread.Document.Sections[0].Body[1]);
        Assert.Equal("Head edited", ((Paragraph)read.Rows[0].Cells[0].Blocks[0]).FlatText);
        Assert.Equal(2, read.Rows[0].Cells[0].GridSpan);
        Assert.Equal(3, read.ColumnCount);
    }

    [Fact]
    public void Inserted_tables_round_trip()
    {
        var session = new EditingSession(new Document(ImmutableList.Create(new Section(SectionProperties.Letter, ImmutableList.Create<Block>(Paragraph.FromText("Intro")))), DefaultStyleSheet.Create()));
        session.MoveCaret(new TextPosition(StoryId.Body(0), BlockPath.Of(0), 5), extend: false);
        session.InsertTable(2, 2, Twips.FromInches(6));
        session.InsertText("cell");
        byte[] bytes = DocxWriter.ToBytes(session.Document);
        using (var stream = new MemoryStream(bytes))
        using (WordprocessingDocument package = WordprocessingDocument.Open(stream, false))
        {
            List<ValidationErrorInfo> errors = new OpenXmlValidator(FileFormatVersions.Office2019).Validate(package).ToList();
            Assert.True(errors.Count == 0, string.Join("\n", errors.Select(e => e.Description + " @ " + e.Path?.XPath)));
        }

        LoadResult reread = DocxReader.Read(new MemoryStream(bytes));
        var read = Assert.IsType<Table>(reread.Document.Sections[0].Body[1]);
        Assert.Equal(2, read.Rows.Length);
        Assert.Equal(2, read.ColumnCount);
        Assert.Equal("cell", ((Paragraph)read.Rows[0].Cells[0].Blocks[0]).FlatText);
        Assert.Equal(Twips.FromInches(3).Value, read.ColumnWidths[0].Value);
        Assert.Equal(3, reread.Document.Sections[0].Body.Count);
    }
}
