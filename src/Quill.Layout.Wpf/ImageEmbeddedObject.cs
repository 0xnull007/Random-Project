using System.Windows;
using System.Windows.Media;
using System.Windows.Media.TextFormatting;
using Quill.Core.Model;

namespace Quill.Layout.Wpf;

/// <summary>
/// An inline picture as the TextFormatter sees it: a one-character box standing on the baseline. A floating
/// picture's anchor is the same object with no size: it keeps the character (and caret stop) in the line while
/// the paginator draws the picture elsewhere.
/// </summary>
internal sealed class ImageEmbeddedObject : TextEmbeddedObject
{
    private static readonly Brush PlaceholderFill = Frozen(new SolidColorBrush(Color.FromRgb(0xF3, 0xF3, 0xF3)));
    private static readonly Pen PlaceholderPen = Frozen(new Pen(Frozen(new SolidColorBrush(Color.FromRgb(0xA0, 0xA0, 0xA0))), 1));

    public ImageEmbeddedObject(RunSpan run, TextRunProperties properties)
    {
        Run = run;
        Properties = properties;
        IsFloating = run.Image?.IsFloating == true;
        Width = IsFloating ? 0 : Math.Max(1, run.Image?.Width.ToDips() ?? 1);
        Height = IsFloating ? 0 : Math.Max(1, run.Image?.Height.ToDips() ?? 1);
    }

    public RunSpan Run { get; }

    /// <summary>True for a floating picture's anchor, which takes no room on the line.</summary>
    public bool IsFloating { get; }

    public double Width { get; }

    public double Height { get; }

    public override LineBreakCondition BreakBefore => IsFloating ? LineBreakCondition.BreakRestrained : LineBreakCondition.BreakDesired;

    public override LineBreakCondition BreakAfter => IsFloating ? LineBreakCondition.BreakRestrained : LineBreakCondition.BreakDesired;

    public override bool HasFixedSize => true;

    public override CharacterBufferReference CharacterBufferReference => default;

    public override int Length => 1;

    public override TextRunProperties Properties { get; }

    public override TextEmbeddedObjectMetrics Format(double remainingParagraphWidth) => new(Width, Height, Height);

    public override Rect ComputeBoundingBox(bool rightToLeft, bool sideways) => new(0, -Height, Width, Height);

    /// <summary><paramref name="origin"/> is the pen position on the baseline.</summary>
    public override void Draw(DrawingContext drawingContext, Point origin, bool rightToLeft, bool sideways)
    {
        if (IsFloating)
        {
            return;
        }

        var bounds = new Rect(origin.X, origin.Y - Height, Width, Height);
        DrawInto(drawingContext, Run.ImageData, bounds);
        if (Run.Image?.Border is { } border)
        {
            PageRenderer.DrawImageBorder(new RectD(bounds.X, bounds.Y, bounds.Width, bounds.Height), border, new WpfRenderTarget(drawingContext));
        }
    }

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
