using System.Globalization;
using System.Windows;
using System.Windows.Media;
using Quill.Core.Model;
using Quill.Core.Styles;
using Quill.Core.Units;

namespace Quill.Layout.Wpf;

/// <summary>Draws layout primitives onto a WPF <see cref="DrawingContext"/>.</summary>
public sealed class WpfRenderTarget : IRenderTarget
{
    private readonly FontCatalog _fonts;

    public WpfRenderTarget(DrawingContext context, FontCatalog? fonts = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        Context = context;
        _fonts = fonts ?? FontCatalog.Shared;
    }

    public DrawingContext Context { get; }

    public void FillRectangle(RectD rect, DocColor color, double opacity = 1)
    {
        Brush brush = _fonts.GetBrush(color);
        if (opacity < 1)
        {
            var translucent = brush.Clone();
            translucent.Opacity = opacity;
            translucent.Freeze();
            brush = translucent;
        }

        Context.DrawRectangle(brush, null, ToRect(rect));
    }

    public void DrawRectangle(RectD rect, DocColor color, double thickness)
    {
        var pen = new Pen(_fonts.GetBrush(color), thickness);
        pen.Freeze();
        Context.DrawRectangle(null, pen, ToRect(rect));
    }

    public void DrawLine(PointD from, PointD to, DocColor color, double thickness)
    {
        var pen = new Pen(_fonts.GetBrush(color), thickness);
        pen.Freeze();
        Context.DrawLine(pen, new Point(from.X, from.Y), new Point(to.X, to.Y));
    }

    public void PushClip(RectD rect) => Context.PushClip(new RectangleGeometry(ToRect(rect)));

    public void Pop() => Context.Pop();

    /// <summary>Fallback for lines that were not formatted by WPF; the normal path draws the TextLine directly.</summary>
    public void DrawTextSegments(IEnumerable<TextSegment> segments, PointD origin)
    {
        ArgumentNullException.ThrowIfNull(segments);
        foreach (TextSegment segment in segments)
        {
            ResolvedRunProperties p = segment.Properties;
            var formatted = new FormattedText(
                segment.Text,
                CultureInfo.CurrentUICulture,
                System.Windows.FlowDirection.LeftToRight,
                _fonts.GetTypeface(p.FontFamily, p.Bold, p.Italic),
                p.FontSize.ToDips(),
                _fonts.GetBrush(p.Color),
                1.0);
            Context.DrawText(formatted, new Point(origin.X + segment.X, origin.Y + segment.Baseline - formatted.Baseline));
        }
    }

    private static Rect ToRect(RectD rect) => new(rect.X, rect.Y, Math.Max(0, rect.Width), Math.Max(0, rect.Height));
}
