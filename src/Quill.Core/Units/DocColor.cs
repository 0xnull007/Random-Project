using System.Globalization;

namespace Quill.Core.Units;

/// <summary>An opaque document color. <see cref="IsAuto"/> means "automatic" (black on a light page).</summary>
public readonly record struct DocColor(uint Argb, bool IsAuto = false)
{
    public static readonly DocColor Auto = new(0xFF000000u, IsAuto: true);
    public static readonly DocColor Black = FromRgb(0, 0, 0);
    public static readonly DocColor White = FromRgb(255, 255, 255);

    public static DocColor FromRgb(byte r, byte g, byte b) => new(0xFF000000u | ((uint)r << 16) | ((uint)g << 8) | b);

    /// <summary>Parses "RRGGBB" (optionally prefixed with '#'); "auto" yields <see cref="Auto"/>.</summary>
    public static DocColor Parse(string hex)
    {
        ArgumentNullException.ThrowIfNull(hex);
        if (hex.Equals("auto", StringComparison.OrdinalIgnoreCase))
        {
            return Auto;
        }

        ReadOnlySpan<char> span = hex.AsSpan().TrimStart('#');
        if (span.Length != 6 || !uint.TryParse(span, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint rgb))
        {
            throw new FormatException($"'{hex}' is not an RRGGBB color.");
        }

        return new DocColor(0xFF000000u | rgb);
    }

    public byte A => (byte)(Argb >> 24);

    public byte R => (byte)(Argb >> 16);

    public byte G => (byte)(Argb >> 8);

    public byte B => (byte)Argb;

    public string ToHex() => $"{R:X2}{G:X2}{B:X2}";

    public override string ToString() => IsAuto ? "auto" : "#" + ToHex();
}
