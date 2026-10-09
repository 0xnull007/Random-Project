using System.Windows;
using System.Windows.Media;
using System.Windows.Media.TextFormatting;
using Quill.Core.Model;

namespace Quill.Layout.Wpf;

/// <summary>An inline picture as the TextFormatter sees it: a one-character box standing on the baseline.</summary>
internal sealed class ImageEmbeddedObject : TextEmbeddedObject
{
    private static readonly Brush PlaceholderFill = Frozen(new SolidColorBrush(Color.FromRgb(0xF3, 0xF3, 0xF3)));
    private static readonly Pen PlaceholderPen = Frozen(new Pen(Frozen(new SolidColorBrush(Color.FromRgb(0xA0, 0xA0, 0xA0))), 1));

    public ImageEmbeddedObject(RunSpan run, TextRunProperties properties)
    {
        Run = run;
        Properties = properties;
        Width = Math.Max(1, run.Image?.Width.ToDips() ?? 1);
        Height = Math.Max(1, run.Image?.Height.ToDips() ?? 1);
    }

    public RunSpan Run { get; }

    public double Width { get; }

    public double Height { get; }

    public override LineBreakCondition BreakBefore => LineBreakCondition.BreakDesired;

    public override LineBreakCondition BreakAfter => LineBreakCondition.BreakDesired;

    public override bool HasFixedSize => true;

    public override CharacterBufferReference CharacterBufferReference => default;

    public override int Length => 1;

    public override TextRunProperties Properties { get; }

    public override TextEmbeddedObjectMetrics Format(double remainingParagraphWidth) => new(Width, Height, Height);

    public override Rect ComputeBoundingBox(bool rightToLeft, bool sideways) => new(0, -Height, Width, Height);

    /// <summary><paramref name="origin"/> is the pen position on the baseline.</summary>
    public override void Draw(DrawingContext drawingContext, Point origin, bool rightToLeft, bool sideways) =>
        DrawInto(drawingContext, Run.ImageData, new Rect(origin.X, origin.Y - Height, Width, Height));

    /// <summary>Draws the picture, or a grey placeholder box when the bytes are missing or undecodable.</summary>
    internal static void DrawInto(DrawingContext context, ImageData? data, Rect bounds)
    {
        if (data is not null && ImageCache.Get(data) is { } bitmap)
        {
            context.DrawImage(bitmap, bounds);
        }
        else
        {
            context.DrawRectangle(PlaceholderFill, PlaceholderPen, bounds);
        }
    }

    private static T Frozen<T>(T freezable)
        where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }
}
