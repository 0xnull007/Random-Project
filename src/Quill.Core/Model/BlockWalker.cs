using System.Collections.Immutable;
using Quill.Core.Text;

namespace Quill.Core.Model;

/// <summary>Walks block lists in document order, descending into table cells, with the path of every paragraph.</summary>
public static class BlockWalker
{
    /// <summary>Every paragraph in <paramref name="blocks"/> and in the cells of its tables, with its (possibly nested) path.</summary>
    public static IEnumerable<(BlockPath Path, Paragraph Paragraph)> Paragraphs(ImmutableList<Block> blocks, BlockPath? parent = null)
    {
        ArgumentNullException.ThrowIfNull(blocks);
        for (int i = 0; i < blocks.Count; i++)
        {
            BlockPath path = parent?.WithLast(i) ?? BlockPath.Of(i);
            switch (blocks[i])
            {
                case Paragraph paragraph:
                    yield return (path, paragraph);
                    break;
                case Table table:
                    for (int r = 0; r < table.Rows.Length; r++)
                    {
                        for (int c = 0; c < table.Rows[r].Cells.Length; c++)
                        {
                            foreach ((BlockPath, Paragraph) inner in Paragraphs(table.Rows[r].Cells[c].Blocks, path.Child(r, c, 0)))
                            {
                                yield return inner;
                            }
                        }
                    }

                    break;
            }
        }
    }

    /// <summary>Applies <paramref name="map"/> to every paragraph, table cells included; unchanged blocks are shared.</summary>
    public static ImmutableList<Block> MapParagraphs(ImmutableList<Block> blocks, Func<Paragraph, Paragraph> map)
    {
        ArgumentNullException.ThrowIfNull(blocks);
        ArgumentNullException.ThrowIfNull(map);
        ImmutableList<Block> result = blocks;
        for (int i = 0; i < blocks.Count; i++)
        {
            switch (blocks[i])
            {
                case Paragraph paragraph:
                    Paragraph mapped = map(paragraph);
                    if (!ReferenceEquals(mapped, paragraph))
                    {
                        result = result.SetItem(i, mapped);
                    }

                    break;
                case Table table:
                    Table mappedTable = table;
                    for (int r = 0; r < table.Rows.Length; r++)
                    {
                        for (int c = 0; c < table.Rows[r].Cells.Length; c++)
                        {
                            ImmutableList<Block> cellBlocks = table.Rows[r].Cells[c].Blocks;
                            ImmutableList<Block> mappedCell = MapParagraphs(cellBlocks, map);
                            if (!ReferenceEquals(mappedCell, cellBlocks))
                            {
                                mappedTable = mappedTable.WithCellBlocks(r, c, mappedCell);
                            }
                        }
                    }

                    if (!ReferenceEquals(mappedTable, table))
                    {
                        result = result.SetItem(i, mappedTable);
                    }

                    break;
            }
        }

        return result;
    }
}
