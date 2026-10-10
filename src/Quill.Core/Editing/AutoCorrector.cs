namespace Quill.Core.Editing;

/// <summary>Pure text rules behind AutoCorrect; <see cref="EditingSession.TypeText"/> applies them to the document.</summary>
public static class AutoCorrector
{
    /// <summary>A rewrite of text before the caret: where it starts in the paragraph, how many characters it replaces, with what, and whether the typed character is swallowed.</summary>
    public readonly record struct Rewrite(int Offset, int Length, string Replacement, bool ConsumesTyped);

    // Completed by their own last character: typing ')' after "(c" gives the symbol.
    private static readonly (string Pattern, string Replacement)[] Immediate =
    [
        ("(c)", "©"),
        ("(r)", "®"),
        ("(tm)", "™"),
        ("(e)", "€"),
        ("...", "…"),
        ("-->", "→"),
        ("<--", "←"),
        ("==>", "⇒"),
        ("<==", "⇐"),
        ("<=>", "⇔"),
    ];

    // Completed by a following space or punctuation, so "1/25" stays a date.
    private static readonly (string Pattern, string Replacement)[] Delimited =
    [
        ("1/2", "½"),
        ("1/4", "¼"),
        ("3/4", "¾"),
    ];

    /// <summary>The curly quote to type instead of a straight one, from what precedes the caret.</summary>
    public static char SmartQuote(char typed, string before)
    {
        ArgumentNullException.ThrowIfNull(before);
        char previous = before.Length > 0 ? before[^1] : ' ';
        bool opening = before.Length == 0 || char.IsWhiteSpace(previous) || previous is '(' or '[' or '{' or '<' or '‘' or '“' or '«';
        return typed switch
        {
            '"' => opening ? '“' : '”',
            '\'' => opening ? '‘' : '’',
            _ => typed,
        };
    }

    /// <summary>A symbol or dash rewrite triggered by <paramref name="typed"/>, if any.</summary>
    public static Rewrite? Replacement(string before, char typed, AutoCorrectOptions options)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(options);
        if (options.Symbols)
        {
            string withTyped = before + typed;
            foreach ((string pattern, string replacement) in Immediate)
            {
                if (withTyped.EndsWith(pattern, StringComparison.OrdinalIgnoreCase))
                {
                    return new Rewrite(before.Length - (pattern.Length - 1), pattern.Length - 1, replacement, ConsumesTyped: true);
                }
            }
        }

        bool delimiter = char.IsWhiteSpace(typed) || typed is '.' or ',' or ';' or ':' or '!' or '?' or ')';
        if (!delimiter)
        {
            return null;
        }

        if (options.Symbols)
        {
            foreach ((string pattern, string replacement) in Delimited)
            {
                if (before.EndsWith(pattern, StringComparison.Ordinal) && !PrecededByWordChar(before, pattern.Length))
                {
                    return new Rewrite(before.Length - pattern.Length, pattern.Length, replacement, ConsumesTyped: false);
                }
            }
        }

        if (options.Dashes && char.IsWhiteSpace(typed))
        {
            // "word -- word" gives an en dash when the second space is typed; "word--word " an em dash.
            if (before.EndsWith("--", StringComparison.Ordinal) && !before.EndsWith("---", StringComparison.Ordinal))
            {
                bool spaced = before.Length > 2 && char.IsWhiteSpace(before[^3]);
                return new Rewrite(before.Length - 2, 2, spaced ? "–" : "—", ConsumesTyped: false);
            }

            int tokenStart = before.Length;
            while (tokenStart > 0 && !char.IsWhiteSpace(before[tokenStart - 1]))
            {
                tokenStart--;
            }

            int dash = before.LastIndexOf("--", StringComparison.Ordinal);
            if (dash > tokenStart && dash + 2 < before.Length && before[dash + 2] != '-' && before[dash - 1] != '-')
            {
                return new Rewrite(dash, 2, "—", ConsumesTyped: false);
            }
        }

        return null;
    }

    /// <summary>Index of the first letter to capitalize when the word before the caret starts a sentence; null otherwise.</summary>
    public static int? SentenceStartToCapitalize(string before, char typed)
    {
        ArgumentNullException.ThrowIfNull(before);
        if (!(char.IsWhiteSpace(typed) || typed is '.' or ',' or ';' or ':' or '!' or '?' or ')'))
        {
            return null;
        }

        int wordStart = before.Length;
        while (wordStart > 0 && !char.IsWhiteSpace(before[wordStart - 1]))
        {
            wordStart--;
        }

        string word = before[wordStart..];
        if (word.Length == 0 || !char.IsLower(word[0]) || (typed == '.' && word.Length <= 2))
        {
            return null;
        }

        foreach (char c in word)
        {
            if (char.IsUpper(c) || c is '.' or '@' or '/' or ':' or '\\' or '#')
            {
                return null; // acronyms, URLs, e-mail addresses, paths
            }
        }

        int p = wordStart - 1;
        while (p >= 0 && char.IsWhiteSpace(before[p]))
        {
            p--;
        }

        bool sentenceStart = p < 0 || (before[p] is '.' or '!' or '?' && p < wordStart - 1);
        return sentenceStart ? wordStart : null;
    }

    /// <summary>True for a bullet marker, false for a numbered one, null when the paragraph text is not a list trigger.</summary>
    public static bool? AutoListKind(string paragraphText)
    {
        ArgumentNullException.ThrowIfNull(paragraphText);
        return paragraphText switch
        {
            "*" or "-" or "•" or "–" or ">" => true,
            "1." or "1)" => false,
            _ => null,
        };
    }

    private static bool PrecededByWordChar(string text, int patternLength)
    {
        int index = text.Length - patternLength - 1;
        return index >= 0 && char.IsLetterOrDigit(text[index]);
    }
}
