using System.Collections.Immutable;

namespace Quill.Core.Text;

/// <summary>
/// The path to a block within a story. Today a single index; nested indices (table, row, cell, block)
/// are reserved for tables. Structural equality and lexicographic ordering.
/// </summary>
public readonly struct BlockPath : IEquatable<BlockPath>, IComparable<BlockPath>
{
    private readonly ImmutableArray<int> _indices;

    public BlockPath(ImmutableArray<int> indices)
    {
        if (indices.IsDefaultOrEmpty)
        {
            throw new ArgumentException("A block path needs at least one index.", nameof(indices));
        }

        _indices = indices;
    }

    public static BlockPath Of(int index) => new(ImmutableArray.Create(index));

    public ImmutableArray<int> Indices => _indices.IsDefault ? ImmutableArray<int>.Empty : _indices;

    public int Depth => Indices.Length;

    public bool IsTopLevel => Depth == 1;

    /// <summary>Index into the story's block list.</summary>
    public int TopIndex => Indices[0];

    /// <summary>Index of the innermost block within its container.</summary>
    public int Last => Indices[^1];

    public BlockPath WithLast(int index) => new(Indices.SetItem(Depth - 1, index));

    public BlockPath Next() => WithLast(Last + 1);

    public BlockPath Previous() => WithLast(Last - 1);

    /// <summary>The indices of the container (empty for a top-level block).</summary>
    public ImmutableArray<int> ContainerIndices => Indices[..^1];

    /// <summary>True when both blocks sit in the same list: the same story level, or the same table cell.</summary>
    public bool SameContainer(BlockPath other) => ContainerIndices.AsSpan().SequenceEqual(other.ContainerIndices.AsSpan());

    /// <summary>Path of block <paramref name="block"/> inside cell <paramref name="cell"/> of row <paramref name="row"/> of the table at this path.</summary>
    public BlockPath Child(int row, int cell, int block) => new(Indices.Add(row).Add(cell).Add(block));

    /// <summary>For a block inside a table cell: the path of that table (null at the top level).</summary>
    public BlockPath? TablePath => Depth >= 4 ? new BlockPath(Indices[..^3]) : null;

    /// <summary>For a block inside a table cell: the row and cell indices.</summary>
    public (int Row, int Cell)? CellIndices => Depth >= 4 ? (Indices[^3], Indices[^2]) : null;


    public bool Equals(BlockPath other) => Indices.AsSpan().SequenceEqual(other.Indices.AsSpan());

    public override bool Equals(object? obj) => obj is BlockPath other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (int index in Indices)
        {
            hash.Add(index);
        }

        return hash.ToHashCode();
    }

    public int CompareTo(BlockPath other)
    {
        ImmutableArray<int> a = Indices;
        ImmutableArray<int> b = other.Indices;
        int common = Math.Min(a.Length, b.Length);
        for (int i = 0; i < common; i++)
        {
            int c = a[i].CompareTo(b[i]);
            if (c != 0)
            {
                return c;
            }
        }

        return a.Length.CompareTo(b.Length);
    }

    public static bool operator ==(BlockPath left, BlockPath right) => left.Equals(right);

    public static bool operator !=(BlockPath left, BlockPath right) => !left.Equals(right);

    public static bool operator <(BlockPath left, BlockPath right) => left.CompareTo(right) < 0;

    public static bool operator >(BlockPath left, BlockPath right) => left.CompareTo(right) > 0;

    public static bool operator <=(BlockPath left, BlockPath right) => left.CompareTo(right) <= 0;

    public static bool operator >=(BlockPath left, BlockPath right) => left.CompareTo(right) >= 0;

    public override string ToString() => "[" + string.Join(".", Indices) + "]";
}
