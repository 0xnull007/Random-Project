using System.Collections.Immutable;
using Quill.Core.Units;

namespace Quill.Core.Model;

/// <summary>A built-in table look (Word's table style gallery), applied as direct formatting: borders, header row fill and text, banded rows.</summary>
public sealed record TableStylePreset(string Name, bool Borders, DocColor? HeaderFill, DocColor? HeaderText, bool HeaderBold, DocColor? BandFill)
{
    public static readonly ImmutableArray<TableStylePreset> All =
    [
        new("Plain Table", Borders: false, HeaderFill: null, HeaderText: null, HeaderBold: false, BandFill: null),
        new("Table Grid", Borders: true, HeaderFill: null, HeaderText: null, HeaderBold: false, BandFill: null),
        new("Grid Table Light", Borders: true, HeaderFill: null, HeaderText: null, HeaderBold: true, BandFill: null),
        new("Grid Table Blue", Borders: true, HeaderFill: DocColor.Parse("4472C4"), HeaderText: DocColor.Parse("FFFFFF"), HeaderBold: true, BandFill: DocColor.Parse("D9E2F3")),
        new("Grid Table Orange", Borders: true, HeaderFill: DocColor.Parse("ED7D31"), HeaderText: DocColor.Parse("FFFFFF"), HeaderBold: true, BandFill: DocColor.Parse("FBE4D5")),
        new("Grid Table Green", Borders: true, HeaderFill: DocColor.Parse("70AD47"), HeaderText: DocColor.Parse("FFFFFF"), HeaderBold: true, BandFill: DocColor.Parse("E2EFD9")),
        new("Grid Table Gray", Borders: true, HeaderFill: DocColor.Parse("7F7F7F"), HeaderText: DocColor.Parse("FFFFFF"), HeaderBold: true, BandFill: DocColor.Parse("EDEDED")),
        new("List Table Blue", Borders: false, HeaderFill: DocColor.Parse("4472C4"), HeaderText: DocColor.Parse("FFFFFF"), HeaderBold: true, BandFill: DocColor.Parse("D9E2F3")),
    ];

    public static TableStylePreset? Find(string name) =>
        All.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>The table restyled: first row as header (filled, bold, coloured text), every second body row banded, borders on or off.</summary>
    public Table Apply(Table table)
    {
        ArgumentNullException.ThrowIfNull(table);
        var rows = ImmutableArray.CreateBuilder<TableRow>(table.Rows.Length);
        for (int r = 0; r < table.Rows.Length; r++)
        {
            TableRow row = table.Rows[r];
            bool header = r == 0 && (HeaderFill is not null || HeaderBold);
            DocColor? fill = header ? HeaderFill : (r % 2 == 0 ? BandFill : null);
            var cells = ImmutableArray.CreateBuilder<TableCell>(row.Cells.Length);
            foreach (TableCell cell in row.Cells)
            {
                ImmutableList<Block> blocks = BlockWalker.MapParagraphs(cell.Blocks, p => Restyle(p, header));
                cells.Add(new TableCell(blocks, cell.GridSpan, cell.VerticalMerge, fill));
            }

            rows.Add(new TableRow(cells.MoveToImmutable(), row.Height, row.ExactHeight, isHeader: header));
        }

        return new Table(rows.MoveToImmutable(), table.ColumnWidths, Borders, table.Indent);
    }

    private Paragraph Restyle(Paragraph paragraph, bool header)
    {
        bool? bold = header && HeaderBold ? true : null;
        DocColor? color = header ? HeaderText : null;
        Paragraph updated = paragraph.WithInlines(paragraph.Inlines.Select(i => i.WithProperties(i.Properties with { Bold = bold, Color = color })).ToImmutableArray());
        return updated.WithMarkProperties(paragraph.MarkProperties with { Bold = bold, Color = color });
    }
}
