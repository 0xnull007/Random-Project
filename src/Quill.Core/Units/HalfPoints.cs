using System.Globalization;

namespace Quill.Core.Units;

/// <summary>
/// A font size in half-points, the unit of the WordprocessingML <c>w:sz</c> element.
/// Represents Word's 0.5 pt granularity exactly.
/// </summary>
public readonly record struct HalfPoints(int Value) : IComparable<HalfPoints>
{
    public static HalfPoints FromPoints(double points) => new((int)Math.Round(points * 2));

    public double ToPoints() => Value / 2.0;

    /// <summary>Font em size in device-independent pixels (1/96 inch).</summary>
    public double ToDips() => Value * 2.0 / 3.0;

    public int CompareTo(HalfPoints other) => Value.CompareTo(other.Value);

    public static bool operator <(HalfPoints left, HalfPoints right) => left.Value < right.Value;

    public static bool operator >(HalfPoints left, HalfPoints right) => left.Value > right.Value;

    public static bool operator <=(HalfPoints left, HalfPoints right) => left.Value <= right.Value;

    public static bool operator >=(HalfPoints left, HalfPoints right) => left.Value >= right.Value;

    public override string ToString() => ToPoints().ToString(CultureInfo.InvariantCulture) + "pt";
}
