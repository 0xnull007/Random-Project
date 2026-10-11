using System.Collections.Immutable;
using System.Globalization;
using Quill.Core.Units;

namespace Quill.Core.Model;

/// <summary>Word's multilevel list library: a name and the nine level definitions.</summary>
public sealed record MultilevelListPreset(string Name, ImmutableArray<ListLevel> Levels);

public static class MultilevelLists
{
    public static readonly ImmutableArray<MultilevelListPreset> Presets =
    [
        new("1. a. i.", DefaultLists.NumberedLevels()),
        new("1 1.1 1.1.1", Legal()),
        new("I. A. 1. a) (1)", Outline()),
        new("• o ▪ bullets", DefaultLists.BulletLevels()),
        new("➢ • ○ bullets", Bullets(["➢", "•", "○"])),
        new("❖ ➢ ▪ bullets", Bullets(["❖", "➢", "▪"])),
    ];

    public static MultilevelListPreset? Find(string name) =>
        Presets.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>1, 1.1, 1.1.1...: every level repeats its parents' numbers.</summary>
    private static ImmutableArray<ListLevel> Legal()
    {
        var levels = ImmutableArray.CreateBuilder<ListLevel>(ListDefinition.LevelCount);
        for (int i = 0; i < ListDefinition.LevelCount; i++)
        {
            string text = string.Join(".", Enumerable.Range(1, i + 1).Select(n => "%" + n.ToString(CultureInfo.InvariantCulture)));
            levels.Add(new ListLevel
            {
                Format = NumberFormat.Decimal,
                Text = text,
                LeftIndent = Twips.FromInches(0.5 + 0.25 * i),
                Hanging = Twips.FromInches(Math.Min(0.75, 0.25 + 0.1 * i)),
            });
        }

        return levels.MoveToImmutable();
    }

    /// <summary>Word's classic outline: I. A. 1. a) (1) (a) i) (a) (i).</summary>
    private static ImmutableArray<ListLevel> Outline()
    {
        (NumberFormat Format, string Text)[] shape =
        [
            (NumberFormat.UpperRoman, "%1."), (NumberFormat.UpperLetter, "%2."), (NumberFormat.Decimal, "%3."),
            (NumberFormat.LowerLetter, "%4)"), (NumberFormat.Decimal, "(%5)"), (NumberFormat.LowerLetter, "(%6)"),
            (NumberFormat.LowerRoman, "%7)"), (NumberFormat.LowerLetter, "(%8)"), (NumberFormat.LowerRoman, "(%9)"),
        ];
        var levels = ImmutableArray.CreateBuilder<ListLevel>(ListDefinition.LevelCount);
        for (int i = 0; i < ListDefinition.LevelCount; i++)
        {
            levels.Add(new ListLevel
            {
                Format = shape[i].Format,
                Text = shape[i].Text,
                LeftIndent = Twips.FromInches(0.5 * (i + 1)),
                Hanging = Twips.FromInches(0.5),
                Alignment = shape[i].Format is NumberFormat.UpperRoman or NumberFormat.LowerRoman ? Alignment.Right : Alignment.Left,
            });
        }

        return levels.MoveToImmutable();
    }

    private static ImmutableArray<ListLevel> Bullets(string[] cycle)
    {
        var levels = ImmutableArray.CreateBuilder<ListLevel>(ListDefinition.LevelCount);
        for (int i = 0; i < ListDefinition.LevelCount; i++)
        {
            levels.Add(new ListLevel
            {
                Format = NumberFormat.Bullet,
                Text = cycle[i % cycle.Length],
                LeftIndent = Twips.FromInches(0.5 * (i + 1)),
                Hanging = Twips.FromInches(0.25),
            });
        }

        return levels.MoveToImmutable();
    }
}
