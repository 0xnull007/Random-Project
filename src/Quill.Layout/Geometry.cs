using System.Globalization;

namespace Quill.Layout;

/// <summary>A point in device-independent pixels (1/96 inch).</summary>
public readonly record struct PointD(double X, double Y)
{
    public static readonly PointD Zero = new(0, 0);

    public PointD Offset(double dx, double dy) => new(X + dx, Y + dy);

    public static PointD operator +(PointD a, PointD b) => new(a.X + b.X, a.Y + b.Y);

    public static PointD operator -(PointD a, PointD b) => new(a.X - b.X, a.Y - b.Y);

    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"({X:0.##}, {Y:0.##})");
}

public readonly record struct SizeD(double Width, double Height)
{
    public static readonly SizeD Empty = new(0, 0);

    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"{Width:0.##}x{Height:0.##}");
}

public readonly record struct RectD(double X, double Y, double Width, double Height)
{
    public static readonly RectD Empty = new(0, 0, 0, 0);

    public static RectD FromEdges(double left, double top, double right, double bottom) => new(left, top, right - left, bottom - top);

    public double Left => X;

    public double Top => Y;

    public double Right => X + Width;

    public double Bottom => Y + Height;

    public PointD TopLeft => new(X, Y);

    public SizeD Size => new(Width, Height);

    public bool IsEmpty => Width <= 0 || Height <= 0;

    public bool Contains(PointD point) => point.X >= X && point.X < Right && point.Y >= Y && point.Y < Bottom;

    public bool IntersectsWith(RectD other) => other.Left < Right && other.Right > Left && other.Top < Bottom && other.Bottom > Top;

    public RectD Offset(double dx, double dy) => new(X + dx, Y + dy, Width, Height);

    public RectD Offset(PointD by) => Offset(by.X, by.Y);

    public RectD Inflate(double dx, double dy) => new(X - dx, Y - dy, Width + 2 * dx, Height + 2 * dy);

    public RectD Union(RectD other)
    {
        if (IsEmpty)
        {
            return other;
        }

        if (other.IsEmpty)
        {
            return this;
        }

        return FromEdges(Math.Min(Left, other.Left), Math.Min(Top, other.Top), Math.Max(Right, other.Right), Math.Max(Bottom, other.Bottom));
    }

    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"[{X:0.##},{Y:0.##} {Width:0.##}x{Height:0.##}]");
}
