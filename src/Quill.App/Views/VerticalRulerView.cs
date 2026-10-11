using System.Globalization;
using System.Windows;
using System.Windows.Media;
using Quill.App.Settings;
using Quill.Core.Units;

namespace Quill.App.Views;

/// <summary>Word's vertical ruler beside the page: the top and bottom margins shaded, the text area white, ticks from the top margin.</summary>
public sealed class VerticalRulerView : FrameworkElement
{
    private const double RulerWidth = 24;

    private static readonly Brush MarginBrush = Frozen(Color.FromArgb(0x38, 0x80, 0x80, 0x80));
    private static readonly Brush ColumnBrush = Frozen(Colors.White);
    private static readonly Pen ColumnBorder = FrozenPen(Color.FromArgb(0x60, 0x80, 0x80, 0x80), 1);
    private static readonly Pen TickPen = FrozenPen(Color.FromRgb(0x60, 0x60, 0x60), 1);
    private static readonly Typeface LabelTypeface = new("Segoe UI");

    private DocumentView? _editor;

    public VerticalRulerView()
    {
        Width = RulerWidth;
        ClipToBounds = true;
        IsVisibleChanged += (_, _) => InvalidateVisual();
    }

    public DocumentView? Editor
    {
        get => _editor;
        set
        {
            if (_editor is not null)
            {
                _editor.ViewportChanged -= OnEditorChanged;
                _editor.ViewStateChanged -= OnEditorChanged;
            }

            _editor = value;
            if (_editor is not null)
            {
                _editor.ViewportChanged += OnEditorChanged;
                _editor.ViewStateChanged += OnEditorChanged;
            }

            InvalidateVisual();
        }
    }

    public void Refresh() => InvalidateVisual();

    private void OnEditorChanged(object? sender, EventArgs e) => InvalidateVisual();

    protected override Size MeasureOverride(Size availableSize) => new(RulerWidth, double.IsInfinity(availableSize.Height) ? 0 : availableSize.Height);

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, ActualWidth, ActualHeight));
        if (_editor?.GetVerticalRulerInfo() is not { } info)
        {
            return;
        }

        double zoom = info.Zoom;
        double pageTop = info.PageTopInView;
        double pageBottom = pageTop + info.PageHeight * zoom;
        double bodyTop = pageTop + info.MarginTop * zoom;
        double bodyBottom = pageBottom - info.MarginBottom * zoom;
        double left = 4;
        double right = RulerWidth - 4;

        dc.DrawRectangle(MarginBrush, null, new Rect(left, pageTop, right - left, Math.Max(0, bodyTop - pageTop)));
        dc.DrawRectangle(MarginBrush, null, new Rect(left, bodyBottom, right - left, Math.Max(0, pageBottom - bodyBottom)));
        dc.DrawRectangle(ColumnBrush, ColumnBorder, new Rect(left, bodyTop, right - left, Math.Max(0, bodyBottom - bodyTop)));

        bool metric = UnitPreference.Current == LengthUnit.Centimeters;
        double unit = (metric ? 96.0 / 2.54 : 96.0) * zoom;
        if (unit < 12)
        {
            return;
        }

        int subdivisions = metric ? 4 : 8;
        double step = unit / subdivisions;
        double middle = (left + right) / 2;
        double pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        int firstIndex = (int)Math.Floor((pageTop - bodyTop) / step);
        int lastIndex = (int)Math.Ceiling((pageBottom - bodyTop) / step);
        for (int i = firstIndex; i <= lastIndex; i++)
        {
            if (i == 0)
            {
                continue;
            }

            double y = Math.Round(bodyTop + i * step) + 0.5;
            if (y < pageTop || y > pageBottom)
            {
                continue;
            }

            if (i % subdivisions == 0)
            {
                string label = Math.Abs(i / subdivisions).ToString(CultureInfo.CurrentCulture);
                var text = new FormattedText(label, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, LabelTypeface, 9, TickPen.Brush, pixelsPerDip);
                dc.DrawText(text, new Point(middle - text.Width / 2, y - text.Height / 2));
            }
            else if (i % (subdivisions / 2) == 0)
            {
                dc.DrawLine(TickPen, new Point(middle - 3, y), new Point(middle + 3, y));
            }
            else
            {
                dc.DrawLine(TickPen, new Point(middle - 1.5, y), new Point(middle + 1.5, y));
            }
        }
    }

    private static SolidColorBrush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private static Pen FrozenPen(Color color, double thickness)
    {
        var pen = new Pen(Frozen(color), thickness);
        pen.Freeze();
        return pen;
    }
}
