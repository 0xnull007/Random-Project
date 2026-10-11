using System.Collections.Concurrent;
using System.Globalization;
using System.Reflection;

namespace Quill.Layout.Hyphenation;

/// <summary>
/// Liang's hyphenation algorithm (the one TeX uses) over a pattern set: every substring of ".word." looks up a
/// pattern whose digits vote on the gaps between letters; odd winning levels are hyphenation points. The English
/// patterns ship as an embedded resource; words are cached once analysed.
/// </summary>
public sealed class Hyphenator
{
    private static readonly Lazy<Hyphenator> s_english = new(() => LoadEmbedded("Quill.Layout.Hyphenation.hyph-en-us.txt", "en"), LazyThreadSafetyMode.ExecutionAndPublication);

    private readonly Dictionary<string, byte[]> _patterns = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int[]> _exceptions = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, int[]> _cache = new(StringComparer.Ordinal);
    private readonly string _language;
    private int _longestPattern;

    private Hyphenator(string language, int leftMin, int rightMin)
    {
        _language = language;
        LeftMin = leftMin;
        RightMin = rightMin;
    }

    /// <summary>American English (hyph-en-us), with TeX's minimums of two letters before and three after a hyphen.</summary>
    public static Hyphenator English => s_english.Value;

    /// <summary>Fewest letters allowed before the first hyphen.</summary>
    public int LeftMin { get; }

    /// <summary>Fewest letters allowed after the last hyphen.</summary>
    public int RightMin { get; }

    public int PatternCount => _patterns.Count;

    public bool IsCultureSupported(CultureInfo? culture) =>
        culture is not null && string.Equals(culture.TwoLetterISOLanguageName, _language, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Parses a resource in Quill's format: comment lines start with '#', patterns (TeX notation such as
    /// <c>hy3ph</c>) follow a <c># patterns</c> line and exceptions (<c>as-so-ciate</c>) a <c># exceptions</c> line.
    /// </summary>
    public static Hyphenator Parse(TextReader reader, string language = "en", int leftMin = 2, int rightMin = 3)
    {
        ArgumentNullException.ThrowIfNull(reader);
        var result = new Hyphenator(language, leftMin, rightMin);
        bool exceptions = false;
        while (reader.ReadLine() is { } line)
        {
            string trimmed = line.Trim();
            if (trimmed.Length == 0)
            {
                continue;
            }

            if (trimmed[0] == '#')
            {
                exceptions = trimmed.Contains("exceptions", StringComparison.OrdinalIgnoreCase);
                continue;
            }

            if (exceptions)
            {
                result.AddException(trimmed);
            }
            else
            {
                result.AddPattern(trimmed);
            }
        }

        return result;
    }

    private static Hyphenator LoadEmbedded(string resourceName, string language)
    {
        using Stream? stream = typeof(Hyphenator).Assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Missing embedded resource {resourceName}.");
        using var reader = new StreamReader(stream);
        return Parse(reader, language);
    }

    private void AddPattern(string pattern)
    {
        var letters = new char[pattern.Length];
        var levels = new byte[pattern.Length + 1];
        int count = 0;
        foreach (char c in pattern)
        {
            if (c is >= '0' and <= '9')
            {
                levels[count] = (byte)(c - '0');
            }
            else
            {
                letters[count++] = char.ToLowerInvariant(c);
            }
        }

        string key = new(letters, 0, count);
        Array.Resize(ref levels, count + 1);
        _patterns[key] = levels;
        _longestPattern = Math.Max(_longestPattern, count);
    }

    private void AddException(string entry)
    {
        var breaks = new List<int>();
        var word = new System.Text.StringBuilder(entry.Length);
        foreach (char c in entry)
        {
            if (c == '-')
            {
                breaks.Add(word.Length);
            }
            else
            {
                word.Append(char.ToLowerInvariant(c));
            }
        }

        _exceptions[word.ToString()] = [.. breaks];
    }

    /// <summary>Indices of <paramref name="word"/> before which a hyphen may be inserted, in increasing order; empty for short or non-alphabetic words.</summary>
    public IReadOnlyList<int> Opportunities(string word)
    {
        ArgumentNullException.ThrowIfNull(word);
        if (word.Length < LeftMin + RightMin)
        {
            return [];
        }

        foreach (char c in word)
        {
            if (!char.IsLetter(c))
            {
                return [];
            }
        }

        return _cache.GetOrAdd(word.ToLowerInvariant(), Compute);
    }

    private int[] Compute(string lower)
    {
        if (_exceptions.TryGetValue(lower, out int[]? listed))
        {
            return listed;
        }

        string wrapped = "." + lower + ".";
        var levels = new byte[wrapped.Length + 1];
        Dictionary<string, byte[]>.AlternateLookup<ReadOnlySpan<char>> lookup = _patterns.GetAlternateLookup<ReadOnlySpan<char>>();
        for (int start = 0; start < wrapped.Length; start++)
        {
            int maxLength = Math.Min(_longestPattern, wrapped.Length - start);
            for (int length = 1; length <= maxLength; length++)
            {
                if (lookup.TryGetValue(wrapped.AsSpan(start, length), out byte[]? pattern))
                {
                    for (int k = 0; k < pattern.Length; k++)
                    {
                        if (pattern[k] > levels[start + k])
                        {
                            levels[start + k] = pattern[k];
                        }
                    }
                }
            }
        }

        // levels[q] sits before wrapped[q]; wrapped[q] is lower[q - 1], so a hyphen before lower[n] reads levels[n + 1].
        var result = new List<int>();
        for (int n = LeftMin; n <= lower.Length - RightMin; n++)
        {
            if (levels[n + 1] % 2 == 1)
            {
                result.Add(n);
            }
        }

        return [.. result];
    }

    /// <summary>Hyphenation points inside every word of <paramref name="text"/>, as indices into the text (a hyphen may go before each).</summary>
    public IReadOnlyList<int> TextBreaks(ReadOnlySpan<char> text)
    {
        var breaks = new List<int>();
        int i = 0;
        while (i < text.Length)
        {
            if (!char.IsLetter(text[i]))
            {
                i++;
                continue;
            }

            int start = i;
            while (i < text.Length && char.IsLetter(text[i]))
            {
                i++;
            }

            foreach (int offset in Opportunities(text[start..i].ToString()))
            {
                breaks.Add(start + offset);
            }
        }

        return breaks;
    }

    /// <summary>The word with soft hyphens (U+00AD) at every opportunity, for display and tests.</summary>
    public string ShowHyphens(string word, char hyphen = '-')
    {
        ArgumentNullException.ThrowIfNull(word);
        IReadOnlyList<int> breaks = Opportunities(word);
        if (breaks.Count == 0)
        {
            return word;
        }

        var builder = new System.Text.StringBuilder(word.Length + breaks.Count);
        int previous = 0;
        foreach (int index in breaks)
        {
            builder.Append(word, previous, index - previous).Append(hyphen);
            previous = index;
        }

        return builder.Append(word, previous, word.Length - previous).ToString();
    }
}
