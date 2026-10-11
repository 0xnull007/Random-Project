using System.Collections.Immutable;
using Quill.Core.Units;

namespace Quill.Core.Model;

public enum VerticalMerge
{
    None,
    Restart,
    Continue,
}

/// <summary>Where a table narrower than the text column sits (Word's table alignment, w:jc).</summary>
public enum TableAlignment
{
    Left,
    Center,
    Right,
}

/// <summary>A table cell: block content plus the grid properties that matter for display.</summary>
public sealed class TableCell
{
    public TableCell(ImmutableList<Block> blocks, int gridSpan = 1, VerticalMerge verticalMerge = VerticalMerge.None, DocColor? shading = null)
    {
        ArgumentNullException.ThrowIfNull(blocks);
        Blocks = blocks.IsEmpty ? ImmutableList.Create<Block>(Paragraph.Empty()) : blocks;
        GridSpan = Math.Max(1, gridSpan);
        VerticalMerge = verticalMerge;
        Shading = shading;
    }

    public ImmutableList<Block> Blocks { get; }

    public int GridSpan { get; }

    public VerticalMerge VerticalMerge { get; }

    /// <summary>Background fill, or null for none.</summary>
    public DocColor? Shading { get; }
}

public sealed class TableRow
{
    public TableRow(ImmutableArray<TableCell> cells, Twips? height = null, bool exactHeight = false, bool isHeader = false)
    {
        Cells = cells.IsDefault ? ImmutableArray<TableCell>.Empty : cells;
        Height = height;
        ExactHeight = exactHeight;
        IsHeader = isHeader;
    }

    public ImmutableArray<TableCell> Cells { get; }

    /// <summary>Minimum (or, with <see cref="ExactHeight"/>, fixed) row height.</summary>
    public Twips? Height { get; }

    public bool ExactHeight { get; }

    public bool IsHeader { get; }

    public int GridWidth => Cells.Sum(c => c.GridSpan);
}

/// <summary>
/// A table, shown read-only. <see cref="OuterXml"/> keeps the original WordprocessingML so saving writes the
/// table back exactly as it was read.
/// </summary>
public sealed class Table : Block
{
    public Table(ImmutableArray<TableRow> rows, ImmutableArray<Twips> columnWidths, bool hasBorders, Twips indent, string? outerXml = null, TableAlignment alignment = TableAlignment.Left)
    {
        Alignment = alignment;
        Rows = rows.IsDefault ? ImmutableArray<TableRow>.Empty : rows;
        ColumnWidths = columnWidths.IsDefault ? ImmutableArray<Twips>.Empty : columnWidths;
        HasBorders = hasBorders;
        Indent = indent;
        OuterXml = outerXml;
    }

    public ImmutableArray<TableRow> Rows { get; }

    /// <summary>Grid column widths (<c>w:tblGrid</c>); may be empty when the file has none.</summary>
    public ImmutableArray<Twips> ColumnWidths { get; }

    /// <summary>True when the table or its style draws cell borders.</summary>
    public bool HasBorders { get; }

    /// <summary>Distance from the text column's left edge.</summary>
    public Twips Indent { get; }

    public string? OuterXml { get; }

    /// <summary>Left, centred or right within the text column (applies when the table is narrower than the column).</summary>
    public TableAlignment Alignment { get; }

    public int ColumnCount => Math.Max(ColumnWidths.Length, Rows.IsEmpty ? 0 : Rows.Max(r => r.GridWidth));

    /// <summary>Every paragraph inside the table, nested tables included.</summary>
    public IEnumerable<Paragraph> AllParagraphs()
    {
        foreach (TableRow row in Rows)
        {
            foreach (TableCell cell in row.Cells)
            {
                foreach (Block block in cell.Blocks)
                {
                    switch (block)
                    {
                        case Paragraph paragraph:
                            yield return paragraph;
                            break;
                        case Table nested:
                            foreach (Paragraph inner in nested.AllParagraphs())
                            {
                                yield return inner;
                            }

                            break;
                    }
                }
            }
        }
    }

    /// <summary>A copy with one cell's content replaced; the original XML is dropped because the table changed.</summary>
    public Table WithCellBlocks(int row, int cell, ImmutableList<Block> blocks)
    {
        ArgumentNullException.ThrowIfNull(blocks);
        TableRow target = Rows[row];
        TableCell old = target.Cells[cell];
        var updatedCell = new TableCell(blocks, old.GridSpan, old.VerticalMerge, old.Shading);
        var updatedRow = new TableRow(target.Cells.SetItem(cell, updatedCell), target.Height, target.ExactHeight, target.IsHeader);
        return new Table(Rows.SetItem(row, updatedRow), ColumnWidths, HasBorders, Indent, outerXml: null, Alignment);
    }

    public Table WithRows(ImmutableArray<TableRow> rows) => new(rows, ColumnWidths, HasBorders, Indent, outerXml: null, Alignment);

    public Table WithColumnWidths(ImmutableArray<Twips> widths) => new(Rows, widths, HasBorders, Indent, outerXml: null, Alignment);

    public Table WithAlignment(TableAlignment alignment) => new(Rows, ColumnWidths, HasBorders, Indent, outerXml: null, alignment);

    public Table WithBorders(bool hasBorders) => new(Rows, ColumnWidths, hasBorders, Indent, outerXml: null, Alignment);

    public override string ToString() => $"Table({Rows.Length}x{ColumnCount})";

}
