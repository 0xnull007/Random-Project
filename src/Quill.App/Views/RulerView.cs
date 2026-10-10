using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Quill.App.Settings;
using Quill.Core.Editing;
using Quill.Core.Model;
using Quill.Core.Units;

namespace Quill.App.Views;

/// <summary>
/// Word's horizontal ruler: margins, indent markers (first line, hanging/left, right) and tab stops of the caret
/// paragraph, aligned with the page on screen. Drag a marker to change indents, click to add a left tab stop,
/// drag a tab stop off the ruler to remove it.
/// </summary>
public sealed class RulerView : FrameworkElement
{
    private const double RulerHeight = 24;
    private const double MarkerHalf = 5;
    private const double TopBandBottom = 11; // first-line marker lives above, the rest below

    private static readonly Brush MarginBrush = Frozen(Color.FromArgb(0x38, 0x80, 0x80, 0x80));
    private static readonly Brush ColumnBrush = Frozen(Colors.White);
    private static readonly Brush ColumnBorder = Frozen(Color.FromArgb(0x60, 0x80, 0x80, 0x80));
    private static readonly Brush MarkerFill = Frozen(Color.FromRgb(0xF5, 0xF5, 0xF5));
    private static readonly Pen MarkerPen = FrozenPen(Color.FromRgb(0x50, 0x50, 0x50), 1);
    private static readonly Pen TabPen = FrozenPen(Color.FromRgb(0x30, 0x30, 0x30), 1.5);
    private static readonly Pen DefaultTabPen = FrozenPen(Color.FromArgb(0x70, 0x80, 0x80, 0x80), 1);
    private static readonly Pen TickPen = FrozenPen(Color.FromRgb(0x60, 0x60, 0x60), 1);
    private static readonly Typeface LabelTypeface = new("Segoe UI");

    private DocumentView? _editor;
    private DocumentView.RulerInfo? _info;
    private DragKind _drag = DragKind.None;
    private double _dragX;
    private double _dragStartX;
    private bool _dragMoved;
    private TabStop _draggedTab;
    private double _dragOffset;

    private enum DragKind
    {
        None,
        FirstLine,
        LeftIndent,
        BothIndents,
        RightIndent,
        Tab,
        NewTab,
    }

    public RulerView()
    {
        Height = RulerHeight;
        ClipToBounds = true;
        Cursor = Cursors.Arrow;
        IsVisibleChanged += (_, _) => InvalidateVisual();
    }

    /// <summary>The editor whose caret paragraph the ruler shows; set once from the window.</summary>
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

    /// <summary>Call when the selection or the document changed.</summary>
    public void Refresh() => InvalidateVisual();

    private void OnEditorChanged(object? sender, EventArgs e) => InvalidateVisual();

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

    private LengthUnit Unit => UnitPreference.Current;

    private double UnitDips => Unit == LengthUnit.Centimeters ? 96.0 / 2.54 : 96.0;

    private int Subdivisions => Unit == LengthUnit.Centimeters ? 4 : 8;

    // ------------------------------------------------------------------ geometry

    private double OriginX => _info is { } info ? info.PageLeftInView + info.MarginLeft * info.Zoom : 0;

    private double ToView(double dipsFromMargin) => OriginX + dipsFromMargin * (_info?.Zoom ?? 1);

    private double ToDips(double viewX) => (viewX - OriginX) / (_info?.Zoom ?? 1);

    private double ColumnWidth => _info is { } info ? info.PageWidth - info.MarginLeft - info.MarginRight : 0;

    private double FirstLineX => _info is { } info ? ToView(info.LeftIndent + info.FirstLineIndent) : 0;

    private double LeftIndentX => _info is { } info ? ToView(info.LeftIndent) : 0;

    private double RightIndentX => _info is { } info ? ToView(ColumnWidth - info.RightIndent) : 0;

    /// <summary>Snaps a position (DIPs from the left margin) to 1/16 in or 1 mm.</summary>
    private double Snap(double dips)
    {
        double step = Unit == LengthUnit.Centimeters ? 96.0 / 25.4 : 6;
        return Math.Round(dips / step) * step;
    }

    // ------------------------------------------------------------------ drawing

    protected override Size MeasureOverride(Size availableSize) => new(double.IsInfinity(availableSize.Width) ? 0 : availableSize.Width, RulerHeight);

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, ActualWidth, ActualHeight));
        _info = _editor?.GetRulerInfo();
        if (_info is not { } info)
        {
            return;
        }

        double zoom = info.Zoom;
        double pageLeft = info.PageLeftInView;
        double pageRight = pageLeft + info.PageWidth * zoom;
        double columnLeft = OriginX;
        double columnRight = ToView(ColumnWidth);
        double top = 4;
        double bottom = RulerHeight - 4;

        dc.DrawRectangle(MarginBrush, null, new Rect(pageLeft, top, Math.Max(0, columnLeft - pageLeft), bottom - top));
        dc.DrawRectangle(MarginBrush, null, new Rect(columnRight, top, Math.Max(0, pageRight - columnRight), bottom - top));
        dc.DrawRectangle(ColumnBrush, new Pen(ColumnBorder, 1), new Rect(columnLeft, top, Math.Max(0, columnRight - columnLeft), bottom - top));

        DrawTicks(dc, info, pageLeft, pageRight, top, bottom);
        DrawDefaultTabs(dc, info, columnLeft, columnRight, bottom);
        DrawTabStops(dc, info, bottom);
        DrawIndentMarkers(dc);
    }

    private void DrawTicks(DrawingContext dc, DocumentView.RulerInfo info, double pageLeft, double pageRight, double top, double bottom)
    {
        double unit = UnitDips * info.Zoom;
        if (unit < 12)
        {
            return;
        }

        int subdivisions = Subdivisions;
        double step = unit / subdivisions;
        double middle = (top + bottom) / 2;
        double pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        int firstIndex = (int)Math.Floor((pageLeft - OriginX) / step);
        int lastIndex = (int)Math.Ceiling((pageRight - OriginX) / step);
        for (int i = firstIndex; i <= lastIndex; i++)
        {
            if (i == 0)
            {
                continue;
            }

            double x = Math.Round(OriginX + i * step) + 0.5;
            if (x < pageLeft || x > pageRight)
            {
                continue;
            }

            if (i % subdivisions == 0)
            {
                string label = Math.Abs(i / subdivisions).ToString(CultureInfo.CurrentCulture);
                var text = new FormattedText(label, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, LabelTypeface, 9, TickPen.Brush, pixelsPerDip);
                dc.DrawText(text, new Point(x - text.Width / 2, middle - text.Height / 2));
            }
            else if (i % (subdivisions / 2) == 0)
            {
                dc.DrawLine(TickPen, new Point(x, middle - 3), new Point(x, middle + 3));
            }
            else if (step >= 4)
            {
                dc.DrawLine(TickPen, new Point(x, middle - 1.5), new Point(x, middle + 1.5));
            }
        }
    }

    private void DrawDefaultTabs(DrawingContext dc, DocumentView.RulerInfo info, double columnLeft, double columnRight, double bottom)
    {
        double step = info.DefaultTabStop * info.Zoom;
        if (step < 6)
        {
            return;
        }

        for (double x = columnLeft + step; x < columnRight - 1; x += step)
        {
            double sx = Math.Round(x) + 0.5;
            dc.DrawLine(DefaultTabPen, new Point(sx, bottom), new Point(sx, bottom + 3));
        }
    }

    private void DrawTabStops(DrawingContext dc, DocumentView.RulerInfo info, double bottom)
    {
        foreach (TabStop tab in info.Tabs)
        {
            if (_drag == DragKind.Tab && tab == _draggedTab)
            {
                continue;
            }

            DrawTabGlyph(dc, ToView(tab.Position.ToDips()), bottom, tab.Alignment);
        }

        if (_drag == DragKind.Tab && _dragX > -1000)
        {
            DrawTabGlyph(dc, _dragX, bottom, _draggedTab.Alignment);
        }
        else if (_drag == DragKind.NewTab && _dragMoved)
        {
            DrawTabGlyph(dc, _dragX, bottom, TabAlignment.Left);
        }
    }

    private static void DrawTabGlyph(DrawingContext dc, double x, double bottom, TabAlignment alignment)
    {
        double sx = Math.Round(x) + 0.5;
        double y = bottom - 1;
        switch (alignment)
        {
            case TabAlignment.Center:
                dc.DrawLine(TabPen, new Point(sx, y - 6), new Point(sx, y));
                dc.DrawLine(TabPen, new Point(sx - 4, y), new Point(sx + 4, y));
                break;
            case TabAlignment.Right:
                dc.DrawLine(TabPen, new Point(sx, y - 6), new Point(sx, y));
                dc.DrawLine(TabPen, new Point(sx - 5, y), new Point(sx, y));
                break;
            case TabAlignment.Decimal:
                dc.DrawLine(TabPen, new Point(sx, y - 6), new Point(sx, y));
                dc.DrawLine(TabPen, new Point(sx - 4, y), new Point(sx + 4, y));
                dc.DrawEllipse(TabPen.Brush, null, new Point(sx + 3, y - 4), 1.2, 1.2);
                break;
            default:
                dc.DrawLine(TabPen, new Point(sx, y - 6), new Point(sx, y));
                dc.DrawLine(TabPen, new Point(sx, y), new Point(sx + 5, y));
                break;
        }
    }

    private void DrawIndentMarkers(DrawingContext dc)
    {
        double first = _drag is DragKind.FirstLine or DragKind.BothIndents ? _dragX : FirstLineX;
        double left = _drag is DragKind.LeftIndent or DragKind.BothIndents ? (_drag == DragKind.BothIndents ? _dragX + (LeftIndentX - FirstLineX) : _dragX) : LeftIndentX;
        double right = _drag == DragKind.RightIndent ? _dragX : RightIndentX;
        if (_drag == DragKind.BothIndents)
        {
            double delta = _dragX - _dragStartX;
            first = FirstLineX + delta;
            left = LeftIndentX + delta;
        }

        // First line: triangle pointing down from the top band.
        DrawTriangle(dc, first, 3, pointsDown: true);
        // Left (hanging) indent: triangle pointing up, with the box below that drags both.
        DrawTriangle(dc, left, RulerHeight - 9, pointsDown: false);
        dc.DrawRectangle(MarkerFill, MarkerPen, new Rect(left - MarkerHalf, RulerHeight - 4.5, MarkerHalf * 2, 3.5));
        // Right indent.
        DrawTriangle(dc, right, RulerHeight - 9, pointsDown: false);
    }

    private static void DrawTriangle(DrawingContext dc, double x, double top, bool pointsDown)
    {
        var geometry = new StreamGeometry();
        using (StreamGeometryContext context = geometry.Open())
        {
            if (pointsDown)
            {
                context.BeginFigure(new Point(x - MarkerHalf, top), true, true);
                context.LineTo(new Point(x + MarkerHalf, top), true, false);
                context.LineTo(new Point(x, top + 6), true, false);
            }
            else
            {
                context.BeginFigure(new Point(x, top), true, true);
                context.LineTo(new Point(x + MarkerHalf, top + 6), true, false);
                context.LineTo(new Point(x - MarkerHalf, top + 6), true, false);
            }
        }

        geometry.Freeze();
        dc.DrawGeometry(MarkerFill, MarkerPen, geometry);
    }

    // ------------------------------------------------------------------ mouse

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (_info is not { CanEdit: true } info || _editor?.Session is null)
        {
            return;
        }

        Point p = e.GetPosition(this);
        _dragStartX = p.X;
        _dragX = p.X;
        _dragMoved = false;
        _drag = DragKind.None;
        if (p.Y < TopBandBottom && Math.Abs(p.X - FirstLineX) <= MarkerHalf + 1)
        {
            _drag = DragKind.FirstLine;
        }
        else if (p.Y >= RulerHeight - 5 && Math.Abs(p.X - LeftIndentX) <= MarkerHalf + 1)
        {
            _drag = DragKind.BothIndents;
        }
        else if (p.Y >= TopBandBottom && Math.Abs(p.X - LeftIndentX) <= MarkerHalf + 1)
        {
            _drag = DragKind.LeftIndent;
        }
        else if (p.Y >= TopBandBottom && Math.Abs(p.X - RightIndentX) <= MarkerHalf + 1)
        {
            _drag = DragKind.RightIndent;
        }
        else if (p.Y >= TopBandBottom && HitTab(info, p.X) is { } tab)
        {
            _drag = DragKind.Tab;
            _draggedTab = tab;
            _dragOffset = p.X - ToView(tab.Position.ToDips());
        }
        else if (p.X >= OriginX && p.X <= ToView(ColumnWidth))
        {
            _drag = DragKind.NewTab;
        }

        if (_drag != DragKind.None)
        {
            CaptureMouse();
            e.Handled = true;
            InvalidateVisual();
        }
    }

    private TabStop? HitTab(DocumentView.RulerInfo info, double x)
    {
        foreach (TabStop tab in info.Tabs)
        {
            if (Math.Abs(ToView(tab.Position.ToDips()) - x) <= 5)
            {
                return tab;
            }
        }

        return null;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_drag == DragKind.None || !IsMouseCaptured)
        {
            if (_info is { } info)
            {
                Point hover = e.GetPosition(this);
                bool onMarker = Math.Abs(hover.X - FirstLineX) <= MarkerHalf + 1 || Math.Abs(hover.X - LeftIndentX) <= MarkerHalf + 1
                    || Math.Abs(hover.X - RightIndentX) <= MarkerHalf + 1 || (hover.Y >= TopBandBottom && HitTab(info, hover.X) is not null);
                Cursor = onMarker ? Cursors.SizeWE : Cursors.Arrow;
            }

            return;
        }

        Point p = e.GetPosition(this);
        if (Math.Abs(p.X - _dragStartX) > 2 || Math.Abs(p.Y - RulerHeight / 2) > RulerHeight)
        {
            _dragMoved = true;
        }

        _dragX = _drag == DragKind.Tab ? p.X - _dragOffset : p.X;
        if (_drag == DragKind.Tab && (p.Y > RulerHeight + 12 || p.Y < -12))
        {
            _dragX = -5000; // off the ruler: will be removed on release
        }

        InvalidateVisual();
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (_drag == DragKind.None)
        {
            return;
        }

        DragKind drag = _drag;
        _drag = DragKind.None;
        ReleaseMouseCapture();
        e.Handled = true;
        if (_info is not { } info || _editor?.Session is not { } session)
        {
            InvalidateVisual();
            return;
        }

        Point p = e.GetPosition(this);
        double column = ColumnWidth;
        try
        {
            switch (drag)
            {
                case DragKind.FirstLine when _dragMoved:
                {
                    double first = Snap(ToDips(p.X)) - info.LeftIndent;
                    first = Math.Clamp(first, -(info.LeftIndent + info.MarginLeft), column - info.RightIndent - info.LeftIndent - 36);
                    session.ApplyParagraphFormat(new ParagraphProperties { FirstLineIndent = Twips.FromDips(first) });
                    break;
                }

                case DragKind.LeftIndent when _dragMoved:
                {
                    // The hanging marker moves the left indent and keeps the first line where it is.
                    double left = Math.Clamp(Snap(ToDips(p.X)), -info.MarginLeft, column - info.RightIndent - 36);
                    double firstLineAbsolute = info.LeftIndent + info.FirstLineIndent;
                    session.ApplyParagraphFormat(new ParagraphProperties { LeftIndent = Twips.FromDips(left), FirstLineIndent = Twips.FromDips(firstLineAbsolute - left) });
                    break;
                }

                case DragKind.BothIndents when _dragMoved:
                {
                    double delta = ToDips(p.X) - ToDips(_dragStartX);
                    double left = Math.Clamp(Snap(info.LeftIndent + delta), -info.MarginLeft, column - info.RightIndent - 36);
                    session.ApplyParagraphFormat(new ParagraphProperties { LeftIndent = Twips.FromDips(left) });
                    break;
                }

                case DragKind.RightIndent when _dragMoved:
                {
                    double right = Math.Clamp(column - Snap(ToDips(p.X)), -info.MarginRight, column - info.LeftIndent - Math.Max(0, info.FirstLineIndent) - 36);
                    session.ApplyParagraphFormat(new ParagraphProperties { RightIndent = Twips.FromDips(right) });
                    break;
                }

                case DragKind.Tab:
                {
                    bool remove = _dragX < -1000;
                    if (remove || _dragMoved)
                    {
                        MoveTab(session, info, _draggedTab, remove ? null : Snap(ToDips(_dragX)));
                    }

                    break;
                }

                case DragKind.NewTab when !_dragMoved && p.Y >= TopBandBottom:
                {
                    double position = Snap(ToDips(p.X));
                    if (position > 0 && position < column)
                    {
                        MoveTab(session, info, null, position);
                    }

                    break;
                }
            }
        }
        finally
        {
            InvalidateVisual();
        }
    }

    /// <summary>Adds, moves or (with a null position) removes a tab stop on the caret paragraphs; stops inherited from the style are cancelled with a cleared stop.</summary>
    private static void MoveTab(EditingSession session, DocumentView.RulerInfo info, TabStop? old, double? newPositionDips)
    {
        var stops = new List<TabStop>(info.DirectTabs);
        if (old is { } previous)
        {
            int index = stops.FindIndex(s => s.Position == previous.Position && !s.IsCleared);
            if (index >= 0)
            {
                stops.RemoveAt(index);
            }
            else
            {
                stops.Add(new TabStop(previous.Position, IsCleared: true));
            }
        }

        if (newPositionDips is { } position)
        {
            Twips at = Twips.FromDips(position);
            stops.RemoveAll(s => s.Position == at);
            stops.Add(new TabStop(at, old?.Alignment ?? TabAlignment.Left, old?.Leader ?? TabLeader.None));
        }

        session.ApplyParagraphFormat(new ParagraphProperties { Tabs = TabStops.Create(stops) });
    }
}
