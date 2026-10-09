using System.Collections.Immutable;
using Quill.Core.Units;

namespace Quill.Core.Model;

/// <summary>How a list level is numbered (WordprocessingML <c>w:numFmt</c>).</summary>
public enum NumberFormat
{
    Bullet,
    Decimal,
    LowerLetter,
    UpperLetter,
    LowerRoman,
    UpperRoman,
    None,
}

/// <summary>One level (0-8) of a list definition (<c>w:lvl</c>).</summary>
public sealed record ListLevel
{
    public NumberFormat Format { get; init; } = NumberFormat.Decimal;

    /// <summary>Marker template: "%1." uses the level-0 counter, "%1.%2." nests; for bullets the literal bullet text.</summary>
    public string Text { get; init; } = "%1.";

    public int Start { get; init; } = 1;

    /// <summary>Left indent of the paragraph text.</summary>
    public Twips LeftIndent { get; init; } = Twips.FromInches(0.5);

    /// <summary>How far the marker hangs to the left of the text.</summary>
    public Twips Hanging { get; init; } = Twips.FromInches(0.25);

    /// <summary>Font for the marker (bullets drawn from symbol fonts); null = the paragraph font.</summary>
    public string? MarkerFont { get; init; }

    public Alignment Alignment { get; init; } = Alignment.Left;

    public bool IsBullet => Format == NumberFormat.Bullet;
}

/// <summary>A list definition: nine levels of formatting (<c>w:abstractNum</c>).</summary>
public sealed record ListDefinition(int Id, ImmutableArray<ListLevel> Levels)
{
    public const int LevelCount = 9;

    public ListLevel? GetLevel(int level) => level >= 0 && level < Levels.Length ? Levels[level] : null;

    /// <summary>True when the first level is a bullet (used to tell bullet lists from numbered ones in the UI).</summary>
    public bool IsBulleted => Levels.Length > 0 && Levels[0].IsBullet;
}

/// <summary>A list instance paragraphs refer to (<c>w:num</c>). Instances may restart levels of their definition.</summary>
public sealed record ListInstance(int Id, int DefinitionId, ImmutableDictionary<int, int>? StartOverrides = null)
{
    public int? StartOverride(int level) =>
        StartOverrides is not null && StartOverrides.TryGetValue(level, out int start) ? start : null;
}

/// <summary>A paragraph's list membership (<c>w:numPr</c>). <see cref="NumberingId"/> 0 means "not in a list" (used to switch a list off).</summary>
public readonly record struct ListFormat(int NumberingId, int Level)
{
    public static readonly ListFormat None = new(0, 0);

    public bool IsNone => NumberingId <= 0;
}

/// <summary>All list definitions and instances of a document (<c>numbering.xml</c>). Immutable.</summary>
public sealed class ListStore
{
    public static readonly ListStore Empty = new(ImmutableDictionary<int, ListDefinition>.Empty, ImmutableDictionary<int, ListInstance>.Empty);

    public ListStore(ImmutableDictionary<int, ListDefinition> definitions, ImmutableDictionary<int, ListInstance> instances)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        ArgumentNullException.ThrowIfNull(instances);
        Definitions = definitions;
        Instances = instances;
    }

    public ImmutableDictionary<int, ListDefinition> Definitions { get; }

    public ImmutableDictionary<int, ListInstance> Instances { get; }

    public bool IsEmpty => Instances.IsEmpty;

    public int NextDefinitionId => Definitions.IsEmpty ? 0 : Definitions.Keys.Max() + 1;

    public int NextInstanceId => Instances.IsEmpty ? 1 : Instances.Keys.Max() + 1;

    public ListInstance? GetInstance(int numberingId) =>
        Instances.TryGetValue(numberingId, out ListInstance? instance) ? instance : null;

    public ListDefinition? GetDefinition(int numberingId) =>
        GetInstance(numberingId) is { } instance && Definitions.TryGetValue(instance.DefinitionId, out ListDefinition? definition) ? definition : null;

    /// <summary>The level formatting a paragraph in list <paramref name="numberingId"/> at <paramref name="level"/> uses.</summary>
    public ListLevel? GetLevel(int numberingId, int level) => GetDefinition(numberingId)?.GetLevel(level);

    public ListStore With(ListDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        return new ListStore(Definitions.SetItem(definition.Id, definition), Instances);
    }

    public ListStore With(ListInstance instance)
    {
        ArgumentNullException.ThrowIfNull(instance);
        return new ListStore(Definitions, Instances.SetItem(instance.Id, instance));
    }

    /// <summary>Adds a definition and a fresh instance of it; returns the new store and the instance id.</summary>
    public (ListStore Store, int NumberingId) AddList(ImmutableArray<ListLevel> levels)
    {
        var definition = new ListDefinition(NextDefinitionId, levels);
        int numberingId = NextInstanceId;
        ListStore store = With(definition).With(new ListInstance(numberingId, definition.Id));
        return (store, numberingId);
    }
}

/// <summary>Word's default bullet and numbered list definitions.</summary>
public static class DefaultLists
{
    private static readonly string[] BulletCycle = ["•", "o", "▪"];

    public static ImmutableArray<ListLevel> BulletLevels()
    {
        var levels = ImmutableArray.CreateBuilder<ListLevel>(ListDefinition.LevelCount);
        for (int i = 0; i < ListDefinition.LevelCount; i++)
        {
            levels.Add(new ListLevel
            {
                Format = NumberFormat.Bullet,
                Text = BulletCycle[i % BulletCycle.Length],
                LeftIndent = Twips.FromInches(0.5 * (i + 1)),
                Hanging = Twips.FromInches(0.25),
            });
        }

        return levels.MoveToImmutable();
    }

    public static ImmutableArray<ListLevel> NumberedLevels()
    {
        NumberFormat[] cycle = [NumberFormat.Decimal, NumberFormat.LowerLetter, NumberFormat.LowerRoman];
        var levels = ImmutableArray.CreateBuilder<ListLevel>(ListDefinition.LevelCount);
        for (int i = 0; i < ListDefinition.LevelCount; i++)
        {
            NumberFormat format = cycle[i % cycle.Length];
            levels.Add(new ListLevel
            {
                Format = format,
                Text = "%" + (i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture) + ".",
                LeftIndent = Twips.FromInches(0.5 * (i + 1)),
                Hanging = Twips.FromInches(0.25),
                Alignment = format == NumberFormat.LowerRoman ? Alignment.Right : Alignment.Left,
            });
        }

        return levels.MoveToImmutable();
    }
}
