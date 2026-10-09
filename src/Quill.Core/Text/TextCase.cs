using System.Globalization;
using System.Text;

namespace Quill.Core.Text;

public enum CaseChange
{
    Lower,
    Upper,
    TitleCase,
    Sentence,
    Toggle,
}

/// <summary>Case transformations that keep the text length, so run boundaries and the selection stay valid.</summary>
public static class TextCase
{
    public static string Apply(string text, CaseChange change, CultureInfo? culture = null)
    {
        ArgumentNullException.ThrowIfNull(text);
        culture ??= CultureInfo.CurrentCulture;
        TextInfo info = culture.TextInfo;
        return change switch
        {
            CaseChange.Lower => info.ToLower(text),
            CaseChange.Upper => info.ToUpper(text),
            CaseChange.TitleCase => CapitalizeWords(text, info),
            CaseChange.Sentence => SentenceCase(text, info),
            CaseChange.Toggle => Toggle(text, info),
            _ => text,
        };
    }

    /// <summary>Word's Shift+F3 cycle: lowercase, then UPPERCASE, then Capitalize Each Word, then lowercase again.</summary>
    public static CaseChange NextInCycle(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        bool anyLetter = false;
        bool allLower = true;
        bool allUpper = true;
        foreach (char c in text)
        {
            if (!char.IsLetter(c))
            {
                continue;
            }

            anyLetter = true;
            allLower &= char.IsLower(c);
            allUpper &= char.IsUpper(c);
        }

        if (!anyLetter || allLower)
        {
            return CaseChange.Upper;
        }

        if (allUpper)
        {
            return CaseChange.TitleCase;
        }

        bool capitalized = string.Equals(text, CapitalizeWords(text, CultureInfo.CurrentCulture.TextInfo), StringComparison.Ordinal);
        return capitalized ? CaseChange.Lower : CaseChange.Upper;
    }

    private static string CapitalizeWords(string text, TextInfo info)
    {
        var result = new StringBuilder(text.Length);
        bool wordStart = true;
        foreach (char c in text)
        {
            if (char.IsLetter(c))
            {
                result.Append(wordStart ? info.ToUpper(c) : info.ToLower(c));
                wordStart = false;
            }
            else
            {
                result.Append(c);
                wordStart = !char.IsDigit(c) && c != '\'' && c != '’';
            }
        }

        return result.ToString();
    }

    private static string SentenceCase(string text, TextInfo info)
    {
        var result = new StringBuilder(text.Length);
        bool sentenceStart = true;
        foreach (char c in text)
        {
            if (char.IsLetter(c))
            {
                result.Append(sentenceStart ? info.ToUpper(c) : info.ToLower(c));
                sentenceStart = false;
            }
            else
            {
                result.Append(c);
                if (c is '.' or '!' or '?' or '\n' or '\r' or '\f' or '…')
                {
                    sentenceStart = true;
                }
                else if (char.IsDigit(c))
                {
                    sentenceStart = false;
                }
            }
        }

        return result.ToString();
    }

    private static string Toggle(string text, TextInfo info)
    {
        var result = new StringBuilder(text.Length);
        foreach (char c in text)
        {
            result.Append(char.IsUpper(c) ? info.ToLower(c) : char.IsLower(c) ? info.ToUpper(c) : c);
        }

        return result.ToString();
    }
}
