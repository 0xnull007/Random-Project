using System.Globalization;

namespace Quill.Core.Units;

/// <summary>Units a user types or sees in dialogs.</summary>
public enum LengthUnit
{
    Inches,
    Centimeters,
    Millimeters,
    Points,
}

/// <summary>
/// A length in twips (twentieths of a point, 1/1440 inch): the native unit of WordprocessingML.
/// Integer-exact, so values round-trip through .docx without drift. Converted to DIPs or points
/// only at the layout boundary.
/// </summary>
public readonly record struct Twips(int Value) : IComparable<Twips>
{
    public const int PerInch = 1440;
    public const int PerPoint = 20;

    /// <summary>Twips per device-independent pixel (1/96 inch).</summary>
    public const double PerDip = 15.0;

    private const double PerCentimeter = PerInch / 2.54;
    private const long EmuPerTwip = 635;

    // Longer suffixes first so "inches" is not read as "in" + garbage.
    private static readonly (string Suffix, LengthUnit Unit)[] UnitSuffixes =
    [
        ("inches", LengthUnit.Inches),
        ("inch", LengthUnit.Inches),
        ("in", LengthUnit.Inches),
        ("\"", LengthUnit.Inches),
        ("cm", LengthUnit.Centimeters),
        ("mm", LengthUnit.Millimeters),
        ("pt", LengthUnit.Points),
    ];

    public static readonly Twips Zero = new(0);

    /// <summary>
    /// Parses user input such as "1.25", "1.25 in", "3cm", "30 mm" or "12pt". A bare number takes
    /// <paramref name="defaultUnit"/>; a decimal comma is accepted.
    /// </summary>
    public static bool TryParse(string? text, LengthUnit defaultUnit, out Twips value)
    {
        value = Zero;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        ReadOnlySpan<char> span = text.AsSpan().Trim();
        LengthUnit unit = defaultUnit;
        foreach ((string suffix, LengthUnit candidate) in UnitSuffixes)
        {
            if (span.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                unit = candidate;
                span = span[..^suffix.Length].TrimEnd();
                break;
            }
        }

        string number = span.ToString().Replace(',', '.');
        if (!double.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out double amount) || !double.IsFinite(amount))
        {
            return false;
        }

        value = From(amount, unit);
        return true;
    }

    public static Twips From(double amount, LengthUnit unit) => unit switch
    {
        LengthUnit.Centimeters => FromCentimeters(amount),
        LengthUnit.Millimeters => FromCentimeters(amount / 10),
        LengthUnit.Points => FromPoints(amount),
        _ => FromInches(amount),
    };

    public double To(LengthUnit unit) => unit switch
    {
        LengthUnit.Centimeters => ToCentimeters(),
        LengthUnit.Millimeters => ToCentimeters() * 10,
        LengthUnit.Points => ToPoints(),
        _ => ToInches(),
    };

    /// <summary>Formats for display in <paramref name="unit"/> without a suffix (e.g. "1.25").</summary>
    public string Format(LengthUnit unit)
    {
        string pattern = unit is LengthUnit.Millimeters or LengthUnit.Points ? "0.#" : "0.##";
        return To(unit).ToString(pattern, CultureInfo.InvariantCulture);
    }

    public static Twips FromInches(double inches) => new((int)Math.Round(inches * PerInch));

    public static Twips FromPoints(double points) => new((int)Math.Round(points * PerPoint));

    public static Twips FromCentimeters(double centimeters) => new((int)Math.Round(centimeters * PerCentimeter));

    public static Twips FromDips(double dips) => new((int)Math.Round(dips * PerDip));

    public static Twips FromEmu(long emu) => new((int)(emu / EmuPerTwip));

    public double ToInches() => Value / (double)PerInch;

    public double ToPoints() => Value / (double)PerPoint;

    public double ToCentimeters() => Value / PerCentimeter;

    public double ToDips() => Value / PerDip;

    public long ToEmu() => Value * EmuPerTwip;

    public bool IsNegative => Value < 0;

    public int CompareTo(Twips other) => Value.CompareTo(other.Value);

    public static Twips operator +(Twips left, Twips right) => new(left.Value + right.Value);

    public static Twips operator -(Twips left, Twips right) => new(left.Value - right.Value);

    public static Twips operator -(Twips value) => new(-value.Value);

    public static Twips operator *(Twips value, int factor) => new(value.Value * factor);

    public static Twips operator *(Twips value, double factor) => new((int)Math.Round(value.Value * factor));

    public static bool operator <(Twips left, Twips right) => left.Value < right.Value;

    public static bool operator >(Twips left, Twips right) => left.Value > right.Value;

    public static bool operator <=(Twips left, Twips right) => left.Value <= right.Value;

    public static bool operator >=(Twips left, Twips right) => left.Value >= right.Value;

    public static Twips Max(Twips a, Twips b) => a.Value >= b.Value ? a : b;

    public static Twips Min(Twips a, Twips b) => a.Value <= b.Value ? a : b;

    public override string ToString() => Value.ToString(CultureInfo.InvariantCulture) + "tw";
}
