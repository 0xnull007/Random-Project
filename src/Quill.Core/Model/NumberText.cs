using System.Globalization;
using System.Text;

namespace Quill.Core.Model;

/// <summary>Formats counters as decimal, letters or roman numerals, as Word does for lists and page numbers.</summary>
public static class NumberText
{
    private static readonly (int Value, string Symbol)[] RomanTable =
    [
        (1000, "M"), (900, "CM"), (500, "D"), (400, "CD"), (100, "C"), (90, "XC"),
        (50, "L"), (40, "XL"), (10, "X"), (9, "IX"), (5, "V"), (4, "IV"), (1, "I"),
    ];

    public static string Format(int number, NumberFormat format) => format switch
    {
        NumberFormat.LowerLetter => Letters(number).ToLowerInvariant(),
        NumberFormat.UpperLetter => Letters(number),
        NumberFormat.LowerRoman => Roman(number).ToLowerInvariant(),
        NumberFormat.UpperRoman => Roman(number),
        NumberFormat.Bullet or NumberFormat.None => string.Empty,
        _ => number.ToString(CultureInfo.InvariantCulture),
    };

    public static string Format(int number, PageNumberFormat format) => Format(number, format switch
    {
        PageNumberFormat.LowerRoman => NumberFormat.LowerRoman,
        PageNumberFormat.UpperRoman => NumberFormat.UpperRoman,
        PageNumberFormat.LowerLetter => NumberFormat.LowerLetter,
        PageNumberFormat.UpperLetter => NumberFormat.UpperLetter,
        _ => NumberFormat.Decimal,
    });

    public static string Roman(int number)
    {
        if (number <= 0 || number >= 4000)
        {
            return number.ToString(CultureInfo.InvariantCulture);
        }

        var result = new StringBuilder();
        foreach ((int value, string symbol) in RomanTable)
        {
            while (number >= value)
            {
                result.Append(symbol);
                number -= value;
            }
        }

        return result.ToString();
    }

    /// <summary>A, B, ... Z, then AA, BB (Word repeats the letter past Z).</summary>
    public static string Letters(int number)
    {
        if (number <= 0)
        {
            return number.ToString(CultureInfo.InvariantCulture);
        }

        int letter = (number - 1) % 26;
        int repeat = (number - 1) / 26 + 1;
        return new string((char)('A' + letter), repeat);
    }
}
