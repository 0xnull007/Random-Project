using System.Collections.Immutable;
using Quill.Core.Model;
using Quill.Core.Text;
using Quill.Core.Units;

namespace Quill.Core.Editing;

/// <summary>Table commands: insert a grid, add or remove rows and columns, delete the table.</summary>
public sealed partial class EditingSession
{
    private const int MaxTableRows = 200;
    private const int MaxTableColumns = 63;

    /// <summary>True while the caret sits inside a table cell (shows the contextual Table tab).</summary>
    public bool IsInTable => Selection.Active.Block.TablePath is not null;

    /// <summary>The innermost table holding the caret with the caret's row and cell; null outside tables.</summary>
    public (BlockPath Path, Table Table, int Row, int Cell)? TableAtCaret()
    {
        TextPosition at = Selection.Active;
        if (at.Block.TablePath is not { } path || at.Block.CellIndices is not { } cell || Document.TryGetBlock(at.Story, path) is not Table table)
        {
            return null;
        }

        return (path, table, cell.Row, cell.Cell);
    }

    /// <summary>
    /// Inserts an empty grid at the caret, as wide as <paramref name="columnWidth"/>. At the start of a paragraph the
    /// table goes before it; at the end, after it; in the middle the paragraph is split. The caret moves into the first cell.
    /// </summary>
    public void InsertTable(int rows, int columns, Twips columnWidth)
    {
        rows = Math.Clamp(rows, 1, MaxTableRows);
        columns = Math.Clamp(columns, 1, MaxTableColumns);
        (Document document, TextPosition at, _) = DeleteSelectionIfAny();
        Paragraph paragraph = document.GetParagraph(at);
        ImmutableList<Block> blocks = document.GetContainer(at.Story, at.Block);
        int index = at.Block.Last;

        var width = new Twips(Math.Max(144, Math.Max(columnWidth.Value, 1440) / columns));
        Paragraph cellParagraph = CellParagraphLike(paragraph);
        var row = new TableRow(Enumerable.Range(0, columns).Select(_ => new TableCell(ImmutableList.Create<Block>(cellParagraph))).ToImmutableArray());
        var table = new Table(Enumerable.Range(0, rows).Select(_ => row).ToImmutableArray(), Enumerable.Repeat(width, columns).ToImmutableArray(), hasBorders: true, Twips.Zero);

        ImmutableList<Block> updated;
        int tableIndex;
        if (at.Offset == 0)
        {
            updated = blocks.Insert(index, table);
            tableIndex = index;
        }
        else if (at.Offset >= paragraph.Length)
        {
            updated = blocks.Insert(index + 1, table);
            tableIndex = index + 1;
            if (tableIndex == updated.Count - 1)
            {
                updated = updated.Add(paragraph.WithInlines(ImmutableArray<Inline>.Empty));
            }
        }
        else
        {
            (ImmutableArray<Inline> head, ImmutableArray<Inline> tail) = InlineOps.Split(paragraph, at.Offset);
            updated = blocks.SetItem(index, paragraph.WithInlines(head)).Insert(index + 1, table).Insert(index + 2, paragraph.WithInlines(tail));
            tableIndex = index + 1;
        }

        BlockPath tablePath = at.Block.WithLast(tableIndex);
        Commit(
            new EditResult(document.WithContainer(at.Story, at.Block, updated), Selection.Caret(new TextPosition(at.Story, tablePath.Child(0, 0, 0), 0)), ChangeSet.Structural()),
            EditKind.Other,
            startsNewGroup: true);
    }

    /// <summary>Adds a row above or below the caret's row with the same cells and spans, all empty; the caret stays in its column.</summary>
    public void InsertRow(bool below)
    {
        if (TableAtCaret() is not { } at)
        {
            return;
        }

        TableRow source = at.Table.Rows[at.Row];
        TableRow? next = below && at.Row + 1 < at.Table.Rows.Length ? at.Table.Rows[at.Row + 1] : null;
        var cells = ImmutableArray.CreateBuilder<TableCell>(source.Cells.Length);
        int gridStart = 0;
        foreach (TableCell cell in source.Cells)
        {
            bool continuesMerge = below
                ? cell.VerticalMerge != VerticalMerge.None && next is not null && CellAtGrid(next, gridStart) is { VerticalMerge: VerticalMerge.Continue }
                : cell.VerticalMerge == VerticalMerge.Continue;
            cells.Add(new TableCell(ImmutableList.Create<Block>(EmptyCellParagraph(cell)), cell.GridSpan, continuesMerge ? VerticalMerge.Continue : VerticalMerge.None, cell.Shading));
            gridStart += cell.GridSpan;
        }

        var row = new TableRow(cells.MoveToImmutable(), source.Height, source.ExactHeight);
        int newRow = below ? at.Row + 1 : at.Row;
        Table updated = at.Table.WithRows(at.Table.Rows.Insert(newRow, row));
        CommitTable(at.Path, updated, at.Path.Child(newRow, at.Cell, 0));
    }

    /// <summary>
    /// Adds a column left or right of the caret's cell. Other columns shrink so the table keeps its width; a cell
    /// spanning the new grid line in another row just grows by one column.
    /// </summary>
    public void InsertColumn(bool right)
    {
        if (TableAtCaret() is not { } at)
        {
            return;
        }

        TableRow row = at.Table.Rows[at.Row];
        int boundary = GridStart(row, at.Cell) + (right ? row.Cells[at.Cell].GridSpan : 0);
        ImmutableArray<Twips> widths = GridWidths(at.Table);
        int total = widths.Sum(w => w.Value);
        int newWidth = Math.Max(144, total / (widths.Length + 1));
        double scale = (total - newWidth) / (double)total;
        List<Twips> scaled = widths.Select(w => new Twips(Math.Max(144, (int)Math.Round(w.Value * scale)))).ToList();
        scaled.Insert(Math.Min(boundary, scaled.Count), new Twips(newWidth));

        ImmutableArray<TableRow> rows = at.Table.Rows.Select(r => InsertCellAtGrid(r, boundary)).ToImmutableArray();
        var updated = new Table(rows, scaled.ToImmutableArray(), at.Table.HasBorders, at.Table.Indent, alignment: at.Table.Alignment);
        CommitTable(at.Path, updated, at.Path.Child(at.Row, right ? at.Cell + 1 : at.Cell, 0));
    }

    /// <summary>Removes the caret's row; removing the only row removes the table.</summary>
    public void DeleteRow()
    {
        if (TableAtCaret() is not { } at)
        {
            return;
        }

        if (at.Table.Rows.Length == 1)
        {
            DeleteTable();
            return;
        }

        ImmutableArray<TableRow> rows = RepairMerges(at.Table.Rows.RemoveAt(at.Row));
        int row = Math.Min(at.Row, rows.Length - 1);
        int cell = Math.Min(at.Cell, rows[row].Cells.Length - 1);
        CommitTable(at.Path, at.Table.WithRows(rows), at.Path.Child(row, cell, 0));
    }

    /// <summary>Removes the grid columns under the caret's cell; removing the last column removes the table.</summary>
    public void DeleteColumn()
    {
        if (TableAtCaret() is not { } at)
        {
            return;
        }

        TableRow row = at.Table.Rows[at.Row];
        int start = GridStart(row, at.Cell);
        int span = row.Cells[at.Cell].GridSpan;
        if (span >= at.Table.ColumnCount)
        {
            DeleteTable();
            return;
        }

        var rows = ImmutableArray.CreateBuilder<TableRow>();
        foreach (TableRow r in at.Table.Rows)
        {
            TableRow trimmed = RemoveGridRange(r, start, span);
            if (!trimmed.Cells.IsEmpty)
            {
                rows.Add(trimmed);
            }
        }

        ImmutableArray<Twips> widths = GridWidths(at.Table);
        if (start < widths.Length)
        {
            widths = widths.RemoveRange(start, Math.Min(span, widths.Length - start));
        }

        var updated = new Table(rows.ToImmutable(), widths, at.Table.HasBorders, at.Table.Indent, alignment: at.Table.Alignment);
        int newRow = Math.Min(at.Row, updated.Rows.Length - 1);
        int newCell = Math.Min(at.Cell, updated.Rows[newRow].Cells.Length - 1);
        CommitTable(at.Path, updated, at.Path.Child(newRow, newCell, 0));
    }

    /// <summary>Removes the table holding the caret; the caret moves to the paragraph after it (or before it).</summary>
    public void DeleteTable()
    {
        if (Selection.Active.Block.TablePath is not { } path)
        {
            return;
        }

        StoryId story = Selection.Story;
        ImmutableList<Block> blocks = Document.GetContainer(story, path);
        ImmutableList<Block> updated = blocks.RemoveAt(path.Last);
        if (!updated.Any(b => b is Paragraph))
        {
            updated = updated.Insert(Math.Min(path.Last, updated.Count), Paragraph.Empty());
        }

        int after = updated.FindIndex(Math.Min(path.Last, updated.Count - 1), b => b is Paragraph);
        TextPosition caret = after >= 0
            ? new TextPosition(story, path.WithLast(after), 0)
            : EndOf(story, path, updated, updated.FindLastIndex(b => b is Paragraph));
        Commit(new EditResult(Document.WithContainer(story, path, updated), Selection.Caret(caret), ChangeSet.Structural()), EditKind.Other, startsNewGroup: true);
    }

    /// <summary>Joins the caret's cell with the one to its right (contents one after the other); nothing at the row's end.</summary>
    public void MergeCellRight()
    {
        if (TableAtCaret() is not { } at || at.Cell + 1 >= at.Table.Rows[at.Row].Cells.Length)
        {
            return;
        }

        TableRow row = at.Table.Rows[at.Row];
        TableCell left = row.Cells[at.Cell];
        TableCell right = row.Cells[at.Cell + 1];
        if (left.VerticalMerge != right.VerticalMerge)
        {
            return; // one of them is part of a vertical merge the other is not
        }

        var merged = new TableCell(JoinBlocks(left.Blocks, right.Blocks), left.GridSpan + right.GridSpan, left.VerticalMerge, left.Shading);
        TableRow updated = WithCells(row, row.Cells.RemoveAt(at.Cell + 1).SetItem(at.Cell, merged));
        CommitTable(at.Path, at.Table.WithRows(at.Table.Rows.SetItem(at.Row, updated)), at.Path.Child(at.Row, at.Cell, 0));
    }

    /// <summary>Joins the caret's cell with the cell beneath it (a vertical merge); nothing in the last row or over unequal columns.</summary>
    public void MergeCellDown()
    {
        if (TableAtCaret() is not { } at || at.Row + 1 >= at.Table.Rows.Length)
        {
            return;
        }

        TableRow row = at.Table.Rows[at.Row];
        TableCell cell = row.Cells[at.Cell];
        if (cell.VerticalMerge == VerticalMerge.Continue)
        {
            return;
        }

        int gridStart = GridStart(row, at.Cell);
        TableRow next = at.Table.Rows[at.Row + 1];
        int belowIndex = -1;
        int position = 0;
        for (int c = 0; c < next.Cells.Length; c++)
        {
            if (position == gridStart && next.Cells[c].GridSpan == cell.GridSpan)
            {
                belowIndex = c;
                break;
            }

            position += next.Cells[c].GridSpan;
        }

        if (belowIndex < 0 || next.Cells[belowIndex].VerticalMerge == VerticalMerge.Continue)
        {
            return;
        }

        TableCell below = next.Cells[belowIndex];
        var top = new TableCell(JoinBlocks(cell.Blocks, below.Blocks), cell.GridSpan, VerticalMerge.Restart, cell.Shading);
        var continuation = new TableCell(ImmutableList.Create<Block>(EmptyCellParagraph(below)), below.GridSpan, VerticalMerge.Continue, below.Shading);
        ImmutableArray<TableRow> rows = at.Table.Rows
            .SetItem(at.Row, WithCells(row, row.Cells.SetItem(at.Cell, top)))
            .SetItem(at.Row + 1, WithCells(next, next.Cells.SetItem(belowIndex, continuation)));
        CommitTable(at.Path, at.Table.WithRows(rows), at.Path.Child(at.Row, at.Cell, 0));
    }

    /// <summary>Undoes a merge: a cell spanning columns becomes one cell per column, a vertically merged cell gets its rows back; contents stay in the first.</summary>
    public void SplitCell()
    {
        if (TableAtCaret() is not { } at)
        {
            return;
        }

        TableRow row = at.Table.Rows[at.Row];
        TableCell cell = row.Cells[at.Cell];
        if (cell.GridSpan > 1)
        {
            var parts = new List<TableCell> { new(cell.Blocks, 1, cell.VerticalMerge, cell.Shading) };
            for (int i = 1; i < cell.GridSpan; i++)
            {
                parts.Add(new TableCell(ImmutableList.Create<Block>(EmptyCellParagraph(cell)), 1, cell.VerticalMerge, cell.Shading));
            }

            TableRow updated = WithCells(row, row.Cells.RemoveAt(at.Cell).InsertRange(at.Cell, parts));
            CommitTable(at.Path, at.Table.WithRows(at.Table.Rows.SetItem(at.Row, updated)), at.Path.Child(at.Row, at.Cell, 0));
            return;
        }

        if (cell.VerticalMerge == VerticalMerge.Restart)
        {
            int gridStart = GridStart(row, at.Cell);
            ImmutableArray<TableRow>.Builder rows = at.Table.Rows.ToBuilder();
            rows[at.Row] = WithCells(row, row.Cells.SetItem(at.Cell, new TableCell(cell.Blocks, cell.GridSpan, VerticalMerge.None, cell.Shading)));
            for (int r = at.Row + 1; r < rows.Count; r++)
            {
                TableRow lower = rows[r];
                int index = IndexAtGrid(lower, gridStart);
                if (index < 0 || lower.Cells[index].VerticalMerge != VerticalMerge.Continue)
                {
                    break;
                }

                TableCell part = lower.Cells[index];
                rows[r] = WithCells(lower, lower.Cells.SetItem(index, new TableCell(part.Blocks, part.GridSpan, VerticalMerge.None, part.Shading)));
            }

            CommitTable(at.Path, at.Table.WithRows(rows.ToImmutable()), at.Path.Child(at.Row, at.Cell, 0));
        }
    }

    /// <summary>Contents of two cells one after the other, dropping an empty paragraph that would only separate them.</summary>
    private static ImmutableList<Block> JoinBlocks(ImmutableList<Block> first, ImmutableList<Block> second)
    {
        bool firstEmpty = first.Count == 1 && first[0] is Paragraph { IsEmpty: true };
        bool secondEmpty = second.Count == 1 && second[0] is Paragraph { IsEmpty: true };
        if (secondEmpty)
        {
            return first;
        }

        return firstEmpty ? second : first.AddRange(second);
    }

    private static int IndexAtGrid(TableRow row, int gridStart)
    {
        int position = 0;
        for (int c = 0; c < row.Cells.Length; c++)
        {
            if (position == gridStart)
            {
                return c;
            }

            position += row.Cells[c].GridSpan;
        }

        return -1;
    }

    /// <summary>Left, centre or right placement of the caret's table within the text column.</summary>
    public void SetTableAlignment(TableAlignment alignment)
    {
        if (TableAtCaret() is { } at && at.Table.Alignment != alignment)
        {
            CommitTableFormat(at.Path, at.Table.WithAlignment(alignment));
        }
    }

    /// <summary>Gives every column the same width, keeping the table's total width.</summary>
    public void DistributeColumns()
    {
        if (TableAtCaret() is not { } at)
        {
            return;
        }

        ImmutableArray<Twips> widths = GridWidths(at.Table);
        var each = new Twips(Math.Max(144, widths.Sum(w => w.Value) / widths.Length));
        CommitTableFormat(at.Path, at.Table.WithColumnWidths(Enumerable.Repeat(each, widths.Length).ToImmutableArray()));
    }

    /// <summary>Scales the columns so the table spans the text column (Word's AutoFit Window).</summary>
    public void AutoFitTable(Twips columnWidth)
    {
        if (TableAtCaret() is not { } at)
        {
            return;
        }

        ImmutableArray<Twips> widths = GridWidths(at.Table);
        int total = Math.Max(1, widths.Sum(w => w.Value));
        int target = Math.Max(widths.Length * 144, columnWidth.Value - Math.Max(0, at.Table.Indent.Value));
        ImmutableArray<Twips> scaled = widths.Select(w => new Twips((int)Math.Round(w.Value * (double)target / total))).ToImmutableArray();
        CommitTableFormat(at.Path, at.Table.WithColumnWidths(scaled));
    }

    /// <summary>Replaces the caret's table with one paragraph per row, cells separated by tabs (Word's Convert to Text).</summary>
    public void ConvertTableToText()
    {
        if (TableAtCaret() is not { } at)
        {
            return;
        }

        var paragraphs = new List<Block>();
        foreach (TableRow row in at.Table.Rows)
        {
            var inlines = new List<Inline>();
            for (int c = 0; c < row.Cells.Length; c++)
            {
                if (c > 0)
                {
                    inlines.Add(new Run("\t"));
                }

                bool first = true;
                foreach (Paragraph paragraph in row.Cells[c].Blocks.OfType<Paragraph>())
                {
                    if (!first && paragraph.Length > 0)
                    {
                        inlines.Add(new Run(" "));
                    }

                    inlines.AddRange(paragraph.Inlines);
                    first = false;
                }
            }

            paragraphs.Add(new Paragraph(InlineOps.Normalize(inlines)));
        }

        if (paragraphs.Count == 0)
        {
            paragraphs.Add(Paragraph.Empty());
        }

        StoryId story = Selection.Story;
        ImmutableList<Block> blocks = Document.GetContainer(story, at.Path);
        ImmutableList<Block> updated = blocks.RemoveAt(at.Path.Last).InsertRange(at.Path.Last, paragraphs);
        Commit(new EditResult(Document.WithContainer(story, at.Path, updated), Selection.Caret(new TextPosition(story, at.Path, 0)), ChangeSet.Structural()), EditKind.Other, startsNewGroup: true);
    }

    /// <summary>
    /// Turns the selected paragraphs (the caret's paragraph when nothing is selected) into a table: one row per
    /// paragraph, cells split at tabs, or at commas when no paragraph has a tab (Word's Convert Text to Table).
    /// </summary>
    public void ConvertTextToTable(Twips columnWidth)
    {
        TextRange range = Selection.Range;
        if (!range.Start.Block.SameContainer(range.End.Block))
        {
            return;
        }

        StoryId story = range.Story;
        ImmutableList<Block> blocks = Document.GetContainer(story, range.Start.Block);
        int first = range.Start.Block.Last;
        int last = range.End.Block.Last;
        if (last > first && range.End.Offset == 0)
        {
            last--;
        }

        var sources = new List<Paragraph>();
        for (int i = first; i <= last; i++)
        {
            if (blocks[i] is not Paragraph paragraph)
            {
                return;
            }

            sources.Add(paragraph);
        }

        char separator = sources.Any(p => p.FlatText.Contains('\t', StringComparison.Ordinal)) ? '\t' : ',';
        List<string[]> rowsText = sources.Select(p => p.FlatText.Split(separator)).ToList();
        int columns = Math.Clamp(rowsText.Max(r => r.Length), 1, MaxTableColumns);
        var width = new Twips(Math.Max(144, Math.Max(columnWidth.Value, 1440) / columns));
        Paragraph template = CellParagraphLike(sources[0]);
        var rows = ImmutableArray.CreateBuilder<TableRow>(rowsText.Count);
        foreach (string[] cells in rowsText)
        {
            rows.Add(new TableRow(Enumerable.Range(0, columns)
                .Select(c => new TableCell(ImmutableList.Create<Block>(template.WithInlines(c < cells.Length && cells[c].Trim().Length > 0 ? [new Run(cells[c].Trim())] : ImmutableArray<Inline>.Empty))))
                .ToImmutableArray()));
        }

        var table = new Table(rows.MoveToImmutable(), Enumerable.Repeat(width, columns).ToImmutableArray(), hasBorders: true, Twips.Zero);
        ImmutableList<Block> updated = blocks.RemoveRange(first, last - first + 1).Insert(first, table);
        if (first == updated.Count - 1)
        {
            updated = updated.Add(Paragraph.Empty());
        }

        BlockPath tablePath = range.Start.Block.WithLast(first);
        Commit(new EditResult(Document.WithContainer(story, range.Start.Block, updated), Selection.Caret(new TextPosition(story, tablePath.Child(0, 0, 0), 0)), ChangeSet.Structural()), EditKind.Other, startsNewGroup: true);
    }

    /// <summary>Applies one of the built-in table looks to the caret's table; one undo step.</summary>

    public void ApplyTableStyle(string presetName)
    {
        if (TableAtCaret() is not { } at || TableStylePreset.Find(presetName) is not { } preset)
        {
            return;
        }

        CommitTableFormat(at.Path, preset.Apply(at.Table));
    }

    /// <summary>Switches the single black grid borders on or off for the caret's table.</summary>


    public void ToggleTableBorders()
    {
        if (TableAtCaret() is not { } at)
        {
            return;
        }

        CommitTableFormat(at.Path, at.Table.WithBorders(!at.Table.HasBorders));
    }

    /// <summary>True when the caret's row is a header row (repeated at the top of every page the table continues on).</summary>
    public bool IsHeaderRowAtCaret => TableAtCaret() is { } at && at.Table.Rows[at.Row].IsHeader;

    /// <summary>Background of the caret's cell, or null for none.</summary>
    public DocColor? CellShadingAtCaret => TableAtCaret() is { } at ? at.Table.Rows[at.Row].Cells[at.Cell].Shading : null;

    public void ToggleHeaderRow()
    {
        if (TableAtCaret() is not { } at)
        {
            return;
        }

        TableRow row = at.Table.Rows[at.Row];
        var updated = new TableRow(row.Cells, row.Height, row.ExactHeight, !row.IsHeader);
        CommitTableFormat(at.Path, at.Table.WithRows(at.Table.Rows.SetItem(at.Row, updated)));
    }

    /// <summary>Fills the caret's cell with a colour (null clears it).</summary>
    public void SetCellShading(DocColor? color)
    {
        if (TableAtCaret() is not { } at)
        {
            return;
        }

        TableRow row = at.Table.Rows[at.Row];
        TableCell cell = row.Cells[at.Cell];
        var updatedCell = new TableCell(cell.Blocks, cell.GridSpan, cell.VerticalMerge, color is { IsAuto: true } ? null : color);
        CommitTableFormat(at.Path, at.Table.WithRows(at.Table.Rows.SetItem(at.Row, WithCells(row, row.Cells.SetItem(at.Cell, updatedCell)))));
    }

    /// <summary>Sets the grid column widths of a table (used when a border is dragged).</summary>
    public void SetColumnWidths(StoryId story, BlockPath tablePath, ImmutableArray<Twips> widths)
    {
        if (Document.TryGetBlock(story, tablePath) is not Table table)
        {
            return;
        }

        Commit(new EditResult(DocumentEditor.ReplaceBlock(Document, story, tablePath, table.WithColumnWidths(widths)), Selection, ChangeSet.Structural()), EditKind.Formatting, startsNewGroup: true);
    }

    private Document? _resizeBase;

    /// <summary>Live preview while a column border is dragged: the document updates without an undo step.</summary>
    public void PreviewColumnWidths(StoryId story, BlockPath tablePath, ImmutableArray<Twips> widths)
    {
        if (Document.TryGetBlock(story, tablePath) is not Table table)
        {
            return;
        }

        _resizeBase ??= Document;
        ApplyState(DocumentEditor.ReplaceBlock(Document, story, tablePath, table.WithColumnWidths(widths)), Selection, ChangeSet.Structural());
    }

    /// <summary>Ends a drag: the whole resize becomes one undo step.</summary>
    public void EndColumnResize()
    {
        if (_resizeBase is not { } baseDocument)
        {
            return;
        }

        Document final = Document;
        _resizeBase = null;
        if (ReferenceEquals(final, baseDocument))
        {
            return;
        }

        ApplyState(baseDocument, Selection, ChangeSet.Structural());
        Commit(new EditResult(final, Selection, ChangeSet.Structural()), EditKind.Formatting, startsNewGroup: true);
    }

    private void CommitTableFormat(BlockPath path, Table table) =>
        Commit(new EditResult(DocumentEditor.ReplaceBlock(Document, Selection.Story, path, table), Selection, ChangeSet.Structural()), EditKind.Formatting, startsNewGroup: true);


    private void CommitTable(BlockPath path, Table table, BlockPath caretBlock)
    {
        Document document = DocumentEditor.ReplaceBlock(Document, Selection.Story, path, table);
        TextPosition caret = TextNavigation.CellStart(document, Selection.Story, path, caretBlock.CellIndices!.Value.Row, caretBlock.CellIndices!.Value.Cell)
            ?? new TextPosition(Selection.Story, path, 0);
        Commit(new EditResult(document, Selection.Caret(caret), ChangeSet.Structural()), EditKind.Other, startsNewGroup: true);
    }

    private static TextPosition EndOf(StoryId story, BlockPath path, ImmutableList<Block> blocks, int index) =>
        new(story, path.WithLast(index), ((Paragraph)blocks[index]).Length);

    /// <summary>An empty cell paragraph in the caret paragraph's font, single spaced with no space after (Word's Table Grid look).</summary>
    private static Paragraph CellParagraphLike(Paragraph paragraph) =>
        new Paragraph(ImmutableArray<Inline>.Empty, paragraph.StyleId, new ParagraphProperties { SpaceBefore = Twips.Zero, SpaceAfter = Twips.Zero, LineSpacing = LineSpacing.Single, List = null }, paragraph.MarkProperties);

    private static Paragraph EmptyCellParagraph(TableCell cell) =>
        (cell.Blocks.OfType<Paragraph>().FirstOrDefault() ?? Paragraph.Empty()).WithInlines(ImmutableArray<Inline>.Empty);

    private static int GridStart(TableRow row, int cell)
    {
        int start = 0;
        for (int i = 0; i < cell; i++)
        {
            start += row.Cells[i].GridSpan;
        }

        return start;
    }

    private static TableCell? CellAtGrid(TableRow row, int gridStart)
    {
        int start = 0;
        foreach (TableCell cell in row.Cells)
        {
            if (start == gridStart)
            {
                return cell;
            }

            start += cell.GridSpan;
        }

        return null;
    }

    private static ImmutableArray<Twips> GridWidths(Table table)
    {
        int count = Math.Max(1, table.ColumnCount);
        if (table.ColumnWidths.Length >= count)
        {
            return table.ColumnWidths;
        }

        int known = table.ColumnWidths.Sum(w => w.Value);
        int missing = count - table.ColumnWidths.Length;
        var each = new Twips(Math.Max(144, (9360 - known) / missing));
        return table.ColumnWidths.AddRange(Enumerable.Repeat(each, missing));
    }

    private static TableRow InsertCellAtGrid(TableRow row, int boundary)
    {
        int start = 0;
        for (int i = 0; i < row.Cells.Length; i++)
        {
            TableCell cell = row.Cells[i];
            if (start == boundary)
            {
                return WithCells(row, row.Cells.Insert(i, NewCellLike(cell)));
            }

            if (start < boundary && boundary < start + cell.GridSpan)
            {
                return WithCells(row, row.Cells.SetItem(i, new TableCell(cell.Blocks, cell.GridSpan + 1, cell.VerticalMerge, cell.Shading)));
            }

            start += cell.GridSpan;
        }

        return WithCells(row, row.Cells.Add(NewCellLike(row.Cells.IsEmpty ? null : row.Cells[^1])));
    }

    private static TableCell NewCellLike(TableCell? template) =>
        new(ImmutableList.Create<Block>(template is null ? Paragraph.Empty() : EmptyCellParagraph(template)), 1, VerticalMerge.None, template?.Shading);

    private static TableRow RemoveGridRange(TableRow row, int start, int span)
    {
        int end = start + span;
        var cells = ImmutableArray.CreateBuilder<TableCell>();
        int position = 0;
        foreach (TableCell cell in row.Cells)
        {
            int cellEnd = position + cell.GridSpan;
            int overlap = Math.Max(0, Math.Min(cellEnd, end) - Math.Max(position, start));
            if (overlap == 0)
            {
                cells.Add(cell);
            }
            else if (overlap < cell.GridSpan)
            {
                cells.Add(new TableCell(cell.Blocks, cell.GridSpan - overlap, cell.VerticalMerge, cell.Shading));
            }

            position = cellEnd;
        }

        return WithCells(row, cells.ToImmutable());
    }

    /// <summary>After removing a row, a "continue" cell without a merge above it starts its own merge.</summary>
    private static ImmutableArray<TableRow> RepairMerges(ImmutableArray<TableRow> rows)
    {
        var result = rows.ToBuilder();
        for (int r = 0; r < result.Count; r++)
        {
            TableRow row = result[r];
            bool changed = false;
            var cells = row.Cells.ToBuilder();
            int start = 0;
            for (int c = 0; c < cells.Count; c++)
            {
                TableCell cell = cells[c];
                if (cell.VerticalMerge == VerticalMerge.Continue && (r == 0 || CellAtGrid(result[r - 1], start) is null or { VerticalMerge: VerticalMerge.None }))
                {
                    cells[c] = new TableCell(cell.Blocks, cell.GridSpan, VerticalMerge.Restart, cell.Shading);
                    changed = true;
                }

                start += cell.GridSpan;
            }

            if (changed)
            {
                result[r] = WithCells(row, cells.ToImmutable());
            }
        }

        return result.ToImmutable();
    }

    private static TableRow WithCells(TableRow row, ImmutableArray<TableCell> cells) => new(cells, row.Height, row.ExactHeight, row.IsHeader);
}
