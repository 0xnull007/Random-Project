using System.Collections.Immutable;
using System.Text;
using Quill.Core.Styles;
using Quill.Core.Text;

namespace Quill.Core.Model;

/// <summary>The marker shown in front of a list paragraph.</summary>
public readonly record struct ListMarker(string Text, string? Font, Alignment Alignment);

/// <summary>
/// Computes list markers for a story: counters run per list definition across the story, a level increments
/// its own counter and resets deeper ones, and templates like "%1.%2." are filled from the counters.
/// </summary>
public static class ListNumbering
{
    public static IReadOnlyDictionary<int, ListMarker> Compute(ImmutableList<Block> blocks, ListStore lists, StyleResolver resolver)
    {
        ArgumentNullException.ThrowIfNull(blocks);
        ArgumentNullException.ThrowIfNull(lists);
        ArgumentNullException.ThrowIfNull(resolver);

        var markers = new Dictionary<int, ListMarker>();
        var counters = new Dictionary<int, int[]>(); // definition id -> counter per level
        var seenInstances = new HashSet<int>();
        for (int i = 0; i < blocks.Count; i++)
        {
            if (blocks[i] is not Paragraph paragraph)
            {
                continue;
            }

            ResolvedParagraphProperties props = resolver.ResolveParagraph(paragraph);
            if (props.List is not { IsNone: false } format)
            {
                continue;
            }

            ListInstance? instance = lists.GetInstance(format.NumberingId);
            ListDefinition? definition = lists.GetDefinition(format.NumberingId);
            if (instance is null || definition is null)
            {
                continue;
            }

            int level = Math.Clamp(format.Level, 0, ListDefinition.LevelCount - 1);
            ListLevel? levelDef = definition.GetLevel(level);
            if (levelDef is null)
            {
                continue;
            }

            if (!counters.TryGetValue(definition.Id, out int[]? counter))
            {
                counter = new int[ListDefinition.LevelCount];
                for (int l = 0; l < counter.Length; l++)
                {
                    counter[l] = (definition.GetLevel(l)?.Start ?? 1) - 1;
                }

                counters[definition.Id] = counter;
            }

            // An instance with start overrides restarts those levels the first time it appears.
            if (seenInstances.Add(instance.Id) && instance.StartOverrides is not null)
            {
                foreach ((int overriddenLevel, int start) in instance.StartOverrides)
                {
                    if (overriddenLevel >= 0 && overriddenLevel < counter.Length)
                    {
                        counter[overriddenLevel] = start - 1;
                    }
                }
            }

            counter[level]++;
            for (int deeper = level + 1; deeper < counter.Length; deeper++)
            {
                counter[deeper] = (definition.GetLevel(deeper)?.Start ?? 1) - 1;
            }

            markers[i] = new ListMarker(Expand(levelDef, definition, counter), levelDef.MarkerFont, levelDef.Alignment);
        }

        return markers;
    }

    private static string Expand(ListLevel level, ListDefinition definition, int[] counter)
    {
        if (level.IsBullet)
        {
            return level.Text;
        }

        var text = new StringBuilder(level.Text.Length + 4);
        for (int i = 0; i < level.Text.Length; i++)
        {
            char c = level.Text[i];
            if (c == '%' && i + 1 < level.Text.Length && char.IsDigit(level.Text[i + 1]))
            {
                int placeholderLevel = level.Text[i + 1] - '1';
                if (placeholderLevel >= 0 && placeholderLevel < counter.Length)
                {
                    NumberFormat format = definition.GetLevel(placeholderLevel)?.Format ?? NumberFormat.Decimal;
                    text.Append(NumberText.Format(Math.Max(1, counter[placeholderLevel]), format));
                }

                i++;
                continue;
            }

            text.Append(c);
        }

        return text.ToString();
    }
}
