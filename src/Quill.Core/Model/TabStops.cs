using System.Collections;
using System.Collections.Immutable;
using Quill.Core.Units;

namespace Quill.Core.Model;

/// <summary>A custom tab stop. <see cref="IsCleared"/> cancels a stop inherited from a style at the same position.</summary>
public readonly record struct TabStop(
    Twips Position,
    TabAlignment Alignment = TabAlignment.Left,
    TabLeader Leader = TabLeader.None,
    bool IsCleared = false);

/// <summary>
/// An immutable, position-sorted set of tab stops with structural equality.
/// Merging follows WordprocessingML: stops accumulate along the style chain, and a cleared stop removes
/// an inherited stop at the same position.
/// </summary>
public sealed class TabStops : IEquatable<TabStops>, IReadOnlyList<TabStop>
{
    public static readonly TabStops Empty = new(ImmutableArray<TabStop>.Empty);

    private readonly ImmutableArray<TabStop> _stops;

    private TabStops(ImmutableArray<TabStop> sortedStops)
    {
        _stops = sortedStops;
    }

    public static TabStops Create(params ReadOnlySpan<TabStop> stops) => Create((IEnumerable<TabStop>)stops.ToArray());

    public static TabStops Create(IEnumerable<TabStop> stops)
    {
        ArgumentNullException.ThrowIfNull(stops);
        var byPosition = new SortedDictionary<int, TabStop>();
        foreach (TabStop stop in stops)
        {
            byPosition[stop.Position.Value] = stop; // last definition at a position wins
        }

        return byPosition.Count == 0 ? Empty : new TabStops(byPosition.Values.ToImmutableArray());
    }

    public int Count => _stops.Length;

    public TabStop this[int index] => _stops[index];

    /// <summary>Applies <paramref name="overrides"/> on top of this set.</summary>
    public TabStops Merge(TabStops? overrides)
    {
        if (overrides is null || overrides.Count == 0)
        {
            return this;
        }

        if (Count == 0)
        {
            return overrides;
        }

        return Create(_stops.Concat(overrides._stops));
    }

    /// <summary>Drops cleared entries; the result is what layout consumes.</summary>
    public TabStops WithoutCleared()
    {
        if (_stops.All(s => !s.IsCleared))
        {
            return this;
        }

        return Create(_stops.Where(s => !s.IsCleared));
    }

    public bool Equals(TabStops? other) => other is not null && _stops.AsSpan().SequenceEqual(other._stops.AsSpan());

    public override bool Equals(object? obj) => Equals(obj as TabStops);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (TabStop stop in _stops)
        {
            hash.Add(stop);
        }

        return hash.ToHashCode();
    }

    public IEnumerator<TabStop> GetEnumerator() => ((IEnumerable<TabStop>)_stops).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
