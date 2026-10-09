using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Quill.Core.Editing;
using Quill.Core.Text;
using Quill.Layout;
using Quill.Layout.Wpf;

namespace Quill.App.Views;

/// <summary>
/// The paged document surface: lays the session's document out into pages, draws the visible ones, and turns
/// mouse and keyboard input into editing-session calls. Lives inside a ScrollViewer and implements its own
/// scrolling so page visuals can be virtualized and zoom never re-paginates.
/// </summary>
public sealed partial class DocumentView : FrameworkElement, IScrollInfo
{
    private const double PageGap = 24;
    private const double CanvasPadding = 24;
    private const double ScrollLineSize = 48;
    private const double SelectionMarkWidth = 6;

    public static readonly DependencyProperty ZoomProperty = DependencyProperty.Register(
        nameof(Zoom), typeof(double), typeof(DocumentView),
        new FrameworkPropertyMetadata(1.0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, (d, _) => ((DocumentView)d).OnZoomChanged()),
        value => value is double z && z >= 0.05 && z <= 10);

    public static readonly DependencyProperty SessionProperty = DependencyProperty.Register(
        nameof(Session), typeof(EditingSession), typeof(DocumentView),
        new PropertyMetadata(null, (d, e) => ((DocumentView)d).OnSessionChanged(e.OldValue as EditingSession, e.NewValue as EditingSession)));

    private readonly VisualCollection _children;
    private readonly ContainerVisual _host = new();
    private readonly ContainerVisual _pagesLayer = new();
    private readonly DrawingVisual _caretVisual = new();
    private readonly Dictionary<int, PageVisuals> _pageVisuals = [];
    private readonly LayoutCache _cache = new();
    private readonly WpfLineFormatter _formatter = new();
    private readonly DispatcherTimer _caretTimer;
    private readonly List<LineRef> _bodyLines = [];
    private readonly List<LineRef> _otherLines = [];

    private Paginator _paginator;
    private LayoutDocument _layout = LayoutDocument.Empty;
    private double[] _pageTops = [];
    private double[] _pageLefts = [];
    private double _docWidth = 1;
    private double _docHeight = 1;
    private Size _viewport;
    private Vector _offset;
    private bool _caretOn = true;
    private bool _dragging;
    private double? _desiredCaretX;
    private (string Text, DocumentFragment Fragment)? _lastCopied;
    private bool _headerFooterMode;
    private TextPosition? _lastBodyPosition;

    public DocumentView()
    {
        _children = new VisualCollection(this) { _host };
        _host.Children.Add(_pagesLayer);
        _host.Children.Add(_caretVisual);

        Focusable = true;
        FocusVisualStyle = null;
        Cursor = Cursors.IBeam;
        ClipToBounds = true;
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Ideal);
        TextOptions.SetTextRenderingMode(this, TextRenderingMode.ClearType);
        TextOptions.SetTextHintingMode(this, TextHintingMode.Fixed);

        _paginator = new Paginator(_formatter, _cache, new LayoutOptions(PixelsPerDip: 1.0));
        _caretTimer = new DispatcherTimer(DispatcherPriority.Normal) { Interval = CaretBlinkInterval() };
        _caretTimer.Tick += (_, _) =>
        {
            _caretOn = !_caretOn;
            DrawCaret();
        };

        Loaded += (_, _) =>
        {
            UpdatePixelsPerDip(VisualTreeHelper.GetDpi(this).PixelsPerDip);
        };
        GotKeyboardFocus += (_, _) => RestartCaretBlink();
        LostKeyboardFocus += (_, _) =>
        {
            _caretTimer.Stop();
            _caretOn = false;
            DrawCaret();
        };
    }

    /// <summary>Raised when page count or the caret's page changes.</summary>
    public event EventHandler? ViewStateChanged;

    public double Zoom
    {
        get => (double)GetValue(ZoomProperty);
        set => SetValue(ZoomProperty, value);
    }

    public EditingSession? Session
    {
        get => (EditingSession?)GetValue(SessionProperty);
        set => SetValue(SessionProperty, value);
    }

    public int PageCount => _layout.PageCount;

    public int CurrentPage { get; private set; } = 1;

    public LayoutDocument Layout => _layout;

    /// <summary>True while the caret is in a header or footer story.</summary>
    public bool IsEditingHeaderFooter => _headerFooterMode;

    // ------------------------------------------------------------------ header / footer editing

    /// <summary>Opens the header of the caret's page for editing, creating it if the section has none.</summary>
    public void EditHeader() => EnterHeaderFooter(CurrentPageIndex(), isHeader: true);

    /// <summary>Opens the footer of the caret's page for editing, creating it if the section has none.</summary>
    public void EditFooter() => EnterHeaderFooter(CurrentPageIndex(), isHeader: false);

    /// <summary>Returns the caret to the body, at the position it left.</summary>
    public void ExitHeaderFooter()
    {
        EditingSession? session = Session;
        if (session is null || !_headerFooterMode)
        {
            return;
        }

        TextPosition target = _lastBodyPosition is { } remembered && session.Document.IsValid(remembered)
            ? remembered
            : TextNavigation.StoryStart(session.Document, StoryId.Body(0));
        session.MoveCaret(target, extend: false);
    }

    /// <summary>Inserts a field at the caret; from the body this first opens the footer, like Word's Page Number button.</summary>
    public void InsertField(Core.Model.Field field)
    {
        ArgumentNullException.ThrowIfNull(field);
        EditingSession? session = Session;
        if (session is null)
        {
            return;
        }

        if (!_headerFooterMode)
        {
            EditFooter();
        }

        session.InsertInline(field);
    }

    private int CurrentPageIndex()
    {
        if (Session is { } session && _layout.Find(session.Selection.Active) is { } found)
        {
            return found.Page.Index;
        }

        return Math.Max(0, PageIndexAt(VisibleDocumentRect().Top + 1));
    }

    private void EnterHeaderFooter(int pageIndex, bool isHeader)
    {
        EditingSession? session = Session;
        if (session is null || pageIndex < 0 || pageIndex >= _layout.PageCount)
        {
            return;
        }

        PageLayout page = _layout.Pages[pageIndex];
        StoryId story = isHeader ? page.EditableHeaderStory() : page.EditableFooterStory();
        if (session.Selection.Story.IsBody)
        {
            _lastBodyPosition = session.Selection.Active;
        }

        session.EnsureStory(story);
        session.MoveCaret(TextNavigation.StoryStart(session.Document, story), extend: false);
    }

    private void UpdateHeaderFooterMode()
    {
        EditingSession? session = Session;
        bool editing = session is not null && !session.Selection.Story.IsBody;
        if (session is not null && session.Selection.Story.IsBody)
        {
            _lastBodyPosition = session.Selection.Active;
        }

        if (editing == _headerFooterMode)
        {
            return;
        }

        _headerFooterMode = editing;
        foreach (PageVisuals visuals in _pageVisuals.Values)
        {
            DrawOverlay(visuals.Overlay, visuals.PageIndex);
        }
    }

    /// <summary>Double-click in the header or footer area enters that story; double-click in the body leaves it.</summary>
    private bool TryToggleHeaderFooterAt(Point viewPoint)
    {
        EditingSession? session = Session;
        if (session is null || _layout.PageCount == 0)
        {
            return false;
        }

        Point doc = ToDocument(viewPoint);
        int pageIndex = PageIndexAt(doc.Y);
        if (pageIndex < 0)
        {
            return false;
        }

        PageLayout page = _layout.Pages[pageIndex];
        double y = doc.Y - PageRect(pageIndex).Y;
        bool inHeader = y < page.BodyArea.Top;
        bool inFooter = y >= page.BodyArea.Bottom;
        if (!_headerFooterMode && (inHeader || inFooter))
        {
            EnterHeaderFooter(pageIndex, inHeader);
            if (HitTest(viewPoint) is { } hit)
            {
                session.MoveCaret(hit.Position, extend: false, hit.Affinity);
            }

            return true;
        }

        if (_headerFooterMode && !inHeader && !inFooter)
        {
            ExitHeaderFooter();
            if (HitTest(viewPoint) is { } hit)
            {
                session.MoveCaret(hit.Position, extend: false, hit.Affinity);
            }

            return true;
        }

        return false;
    }

    /// <summary>Moves the caret, ignoring an extension that would cross into another story (which a selection cannot span).</summary>
    private static void SafeMove(EditingSession session, TextPosition position, bool extend, CaretAffinity affinity = CaretAffinity.Downstream)
    {
        if (extend && position.Story != session.Selection.Story)
        {
            return;
        }

        session.MoveCaret(position, extend, affinity);
    }

    protected override int VisualChildrenCount => _children.Count;

    protected override Visual GetVisualChild(int index) => _children[index];

    // ------------------------------------------------------------------ session & layout

    private void OnSessionChanged(EditingSession? old, EditingSession? current)
    {
        if (old is not null)
        {
            old.DocumentChanged -= OnDocumentChanged;
            old.SelectionChanged -= OnSelectionChanged;
        }

        if (current is not null)
        {
            current.DocumentChanged += OnDocumentChanged;
            current.SelectionChanged += OnSelectionChanged;
        }

        _cache.Clear();
        Relayout();
    }

    private void OnDocumentChanged(object? sender, DocumentChangedEventArgs e)
    {
        Relayout();
        EnsureCaretVisible();
    }

    private void OnSelectionChanged(object? sender, EventArgs e)
    {
        RestartCaretBlink();
        UpdateHeaderFooterMode();
        UpdateCaretAndSelection();
        EnsureCaretVisible();
    }

    private void Relayout()
    {
        EditingSession? session = Session;
        _layout = session is null ? LayoutDocument.Empty : _paginator.Layout(session.Document, session.Resolver);
        ComputeGeometry();
        BuildLineIndex();
        foreach (PageVisuals visuals in _pageVisuals.Values)
        {
            _pagesLayer.Children.Remove(visuals.Root);
        }

        _pageVisuals.Clear();
        UpdateScrollExtent();
        RealizePages();
        UpdateCaretAndSelection();
    }

    private void ComputeGeometry()
    {
        int count = _layout.PageCount;
        _pageTops = new double[count];
        _pageLefts = new double[count];
        double widest = 0;
        foreach (PageLayout page in _layout.Pages)
        {
            widest = Math.Max(widest, page.Size.Width);
        }

        _docWidth = Math.Max(1, widest + 2 * CanvasPadding);
        double y = CanvasPadding;
        for (int i = 0; i < count; i++)
        {
            PageLayout page = _layout.Pages[i];
            _pageTops[i] = y;
            _pageLefts[i] = Math.Max(CanvasPadding, (_docWidth - page.Size.Width) / 2);
            y += page.Size.Height + PageGap;
        }

        _docHeight = Math.Max(1, count == 0 ? 1 : y - PageGap + CanvasPadding);
    }

    private void BuildLineIndex()
    {
        _bodyLines.Clear();
        _otherLines.Clear();
        for (int p = 0; p < _layout.PageCount; p++)
        {
            foreach (BlockFragment fragment in _layout.Pages[p].AllFragments())
            {
                if (fragment is not ParagraphFragment paragraph)
                {
                    continue;
                }

                List<LineRef> target = paragraph.Story.IsBody ? _bodyLines : _otherLines;
                for (int line = paragraph.FirstLine; line <= paragraph.LastLine; line++)
                {
                    target.Add(new LineRef(p, paragraph, line));
                }
            }
        }
    }

    private void UpdatePixelsPerDip(double pixelsPerDip)
    {
        _paginator = new Paginator(_formatter, _cache, new LayoutOptions(PixelsPerDip: pixelsPerDip));
        _cache.Clear();
        Relayout();
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        UpdatePixelsPerDip(newDpi.PixelsPerDip);
    }

    // ------------------------------------------------------------------ measure / arrange / render

    protected override Size MeasureOverride(Size availableSize)
    {
        double w = double.IsInfinity(availableSize.Width) ? _docWidth * Zoom : availableSize.Width;
        double h = double.IsInfinity(availableSize.Height) ? _docHeight * Zoom : availableSize.Height;
        return new Size(w, h);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        _viewport = finalSize;
        UpdateScrollExtent();
        RealizePages();
        return finalSize;
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        Brush canvas = TryFindResource("Quill.CanvasBrush") as Brush ?? Brushes.LightGray;
        drawingContext.DrawRectangle(canvas, null, new Rect(RenderSize));
    }

    private void OnZoomChanged()
    {
        UpdateScrollExtent();
        UpdateHostTransform();
        foreach (PageVisuals visuals in _pageVisuals.Values)
        {
            DrawChrome(visuals.Chrome, visuals.PageIndex);
            DrawOverlay(visuals.Overlay, visuals.PageIndex);
        }

        RealizePages();
        UpdateCaretAndSelection();
        EnsureCaretVisible();
    }

    // ------------------------------------------------------------------ pages

    private Rect VisibleDocumentRect()
    {
        double zoom = Zoom;
        return new Rect(_offset.X / zoom, _offset.Y / zoom, Math.Max(1, _viewport.Width / zoom), Math.Max(1, _viewport.Height / zoom));
    }

    private Rect PageRect(int index)
    {
        PageLayout page = _layout.Pages[index];
        return new Rect(_pageLefts[index], _pageTops[index], page.Size.Width, page.Size.Height);
    }

    private void RealizePages()
    {
        if (_layout.PageCount == 0)
        {
            return;
        }

        Rect visible = VisibleDocumentRect();
        visible.Inflate(0, Math.Max(200, visible.Height / 2));

        var wanted = new HashSet<int>();
        for (int i = 0; i < _layout.PageCount; i++)
        {
            if (PageRect(i).IntersectsWith(visible))
            {
                wanted.Add(i);
            }
        }

        foreach (int index in _pageVisuals.Keys.Where(k => !wanted.Contains(k)).ToList())
        {
            _pagesLayer.Children.Remove(_pageVisuals[index].Root);
            _pageVisuals.Remove(index);
        }

        foreach (int index in wanted.OrderBy(i => i))
        {
            if (_pageVisuals.ContainsKey(index))
            {
                continue;
            }

            var visuals = new PageVisuals(index);
            DrawChrome(visuals.Chrome, index);
            DrawContent(visuals.Content, index);
            DrawOverlay(visuals.Overlay, index);
            DrawSelection(visuals.Selection, index);
            _pagesLayer.Children.Add(visuals.Root);
            _pageVisuals[index] = visuals;
        }

        UpdateHostTransform();
    }

    private void DrawChrome(DrawingVisual visual, int index)
    {
        Rect rect = PageRect(index);
        Brush page = TryFindResource("Quill.PageBrush") as Brush ?? Brushes.White;
        Brush border = TryFindResource("Quill.PageBorderBrush") as Brush ?? Brushes.Gray;
        Brush shadow = TryFindResource("Quill.PageShadowBrush") as Brush ?? Brushes.Transparent;
        double thickness = 1 / Zoom;
        using DrawingContext dc = visual.RenderOpen();
        dc.DrawRectangle(shadow, null, new Rect(rect.X + 3, rect.Y + 3, rect.Width, rect.Height));
        var pen = new Pen(border, thickness);
        pen.Freeze();
        dc.DrawRectangle(page, pen, rect);
    }

    private void DrawContent(DrawingVisual visual, int index)
    {
        Rect rect = PageRect(index);
        using DrawingContext dc = visual.RenderOpen();
        dc.PushTransform(new TranslateTransform(rect.X, rect.Y));
        dc.PushClip(new RectangleGeometry(new Rect(0, 0, rect.Width, rect.Height)));
        PageRenderer.DrawContent(_layout.Pages[index], new WpfRenderTarget(dc));
        dc.Pop();
        dc.Pop();
    }

    /// <summary>While a header or footer is being edited: dims the body and marks the header/footer boundaries, like Word.</summary>
    private void DrawOverlay(DrawingVisual visual, int index)
    {
        using DrawingContext dc = visual.RenderOpen();
        if (!_headerFooterMode)
        {
            return;
        }

        PageLayout page = _layout.Pages[index];
        Rect rect = PageRect(index);
        double zoom = Zoom;

        Brush pageBrush = TryFindResource("Quill.PageBrush") as Brush ?? Brushes.White;
        Brush dim = pageBrush.Clone();
        dim.Opacity = 0.6;
        dim.Freeze();
        dc.DrawRectangle(dim, null, new Rect(rect.X + page.BodyArea.Left, rect.Y + page.BodyArea.Top, page.BodyArea.Width, page.BodyArea.Height));

        Brush accent = TryFindResource("Quill.GuideBrush") as Brush ?? new SolidColorBrush(Color.FromRgb(0x00, 0x78, 0xD4));
        var pen = new Pen(accent, 1 / zoom) { DashStyle = DashStyles.Dash };
        pen.Freeze();
        double left = rect.X + page.BodyArea.Left;
        double right = rect.X + page.BodyArea.Right;
        string section = (page.SectionIndex + 1).ToString(System.Globalization.CultureInfo.CurrentCulture);

        double headerY = rect.Y + page.BodyArea.Top;
        dc.DrawLine(pen, new Point(left, headerY), new Point(right, headerY));
        DrawGuideLabel(dc, $"Header - Section {section}", left, headerY, below: true, accent, zoom);

        double footerY = rect.Y + page.BodyArea.Bottom;
        dc.DrawLine(pen, new Point(left, footerY), new Point(right, footerY));
        DrawGuideLabel(dc, $"Footer - Section {section}", left, footerY, below: false, accent, zoom);
    }

    private void DrawGuideLabel(DrawingContext dc, string text, double x, double y, bool below, Brush accent, double zoom)
    {
        double fontSize = 11 / zoom;
        var formatted = new FormattedText(
            text,
            System.Globalization.CultureInfo.CurrentUICulture,
            System.Windows.FlowDirection.LeftToRight,
            new Typeface("Segoe UI"),
            fontSize,
            Brushes.White,
            VisualTreeHelper.GetDpi(this).PixelsPerDip);
        double padding = 4 / zoom;
        double height = formatted.Height + 2 * padding;
        double top = below ? y : y - height;
        dc.DrawRectangle(accent, null, new Rect(x, top, formatted.Width + 2 * padding, height));
        dc.DrawText(formatted, new Point(x + padding, top + padding));
    }

    private void DrawSelection(DrawingVisual visual, int index)
    {
        using DrawingContext dc = visual.RenderOpen();
        EditingSession? session = Session;
        if (session is null || session.Selection.IsCollapsed)
        {
            return;
        }

        Brush brush = TryFindResource("Quill.SelectionBrush") as Brush ?? new SolidColorBrush(Color.FromArgb(0x55, 0, 0x78, 0xD4));
        Rect pageRect = PageRect(index);
        TextRange range = session.Selection.Range;
        foreach (BlockFragment fragment in _layout.Pages[index].AllFragments())
        {
            if (fragment is not ParagraphFragment paragraph || paragraph.Story != range.Story)
            {
                continue;
            }

            if (paragraph.Path < range.Start.Block || paragraph.Path > range.End.Block)
            {
                continue;
            }

            bool startsHere = paragraph.Path == range.Start.Block;
            bool endsHere = paragraph.Path == range.End.Block;
            for (int i = paragraph.FirstLine; i <= paragraph.LastLine; i++)
            {
                IFormattedLine line = paragraph.Layout.Lines[i];
                int from = startsHere ? Math.Max(line.Start, range.Start.Offset) : line.Start;
                int to = endsHere ? Math.Min(line.End, range.End.Offset) : line.End;
                if (to < from)
                {
                    continue;
                }

                PointD origin = paragraph.LineOrigin(i);
                double top = pageRect.Y + paragraph.LineTop(i);
                double height = paragraph.Layout.LineHeights[i];
                bool includesLineEnd = !endsHere || range.End.Offset > line.End || (range.End.Offset == line.End && range.End.Block > paragraph.Path);
                if (to > from)
                {
                    foreach (RectD bounds in line.GetTextBounds(from, to - from))
                    {
                        dc.DrawRectangle(brush, null, new Rect(pageRect.X + origin.X + bounds.X, top, Math.Max(1, bounds.Width), height));
                    }
                }

                bool reachesEnd = includesLineEnd && (!endsHere || range.End.Block > paragraph.Path || range.End.Offset >= line.End);
                if (reachesEnd && (i < paragraph.LastLine || !endsHere || range.End.Block > paragraph.Path))
                {
                    double x = pageRect.X + origin.X + line.GetCaretX(line.End - line.NewlineLength);
                    dc.DrawRectangle(brush, null, new Rect(x, top, SelectionMarkWidth, height));
                }
            }
        }
    }

    private void UpdateHostTransform()
    {
        double zoom = Zoom;
        double extentWidth = _docWidth * zoom;
        double centerX = Math.Max(0, (_viewport.Width - extentWidth) / 2);
        _host.Transform = new ScaleTransform(zoom, zoom);
        _host.Offset = new Vector(centerX - _offset.X, -_offset.Y);
    }

    // ------------------------------------------------------------------ caret & selection

    private void UpdateCaretAndSelection()
    {
        foreach (PageVisuals visuals in _pageVisuals.Values)
        {
            DrawSelection(visuals.Selection, visuals.PageIndex);
        }

        DrawCaret();
        UpdateCurrentPage();
    }

    private void RestartCaretBlink()
    {
        _caretOn = true;
        _caretTimer.Stop();
        if (IsKeyboardFocused && _caretTimer.Interval > TimeSpan.Zero)
        {
            _caretTimer.Start();
        }
    }

    private void DrawCaret()
    {
        using DrawingContext dc = _caretVisual.RenderOpen();
        EditingSession? session = Session;
        if (session is null || !_caretOn || !IsKeyboardFocused || !session.Selection.IsCollapsed)
        {
            return;
        }

        if (CaretRect(session.Selection.Active, session.Selection.Affinity) is not { } caret)
        {
            return;
        }

        Brush brush = TryFindResource("Quill.CaretBrush") as Brush ?? Brushes.Black;
        double width = Math.Max(SystemParameters.CaretWidth, 1) / Zoom;
        dc.DrawRectangle(brush, null, new Rect(caret.Rect.X, caret.Rect.Y, width, caret.Rect.Height));
    }

    /// <summary>Caret rectangle in document space (unscaled), plus the page it is on.</summary>
    private (int Page, Rect Rect)? CaretRect(TextPosition position, CaretAffinity affinity)
    {
        if (_layout.Find(position) is not { } found)
        {
            return null;
        }

        (PageLayout page, ParagraphFragment fragment) = found;
        int line = Math.Clamp(fragment.Layout.LineIndexOf(position.Offset, affinity == CaretAffinity.Upstream), fragment.FirstLine, fragment.LastLine);
        PointD origin = fragment.LineOrigin(line);
        double x = origin.X + fragment.Layout.Lines[line].GetCaretX(position.Offset);
        double top = fragment.LineTop(line);
        double height = fragment.Layout.LineHeights[line];
        Rect pageRect = PageRect(page.Index);
        return (page.Index, new Rect(pageRect.X + x, pageRect.Y + top, 1, height));
    }

    private void UpdateCurrentPage()
    {
        int current = 1;
        if (Session is { } session && _layout.Find(session.Selection.Active) is { } found)
        {
            current = found.Page.Index + 1;
        }

        CurrentPage = current;
        ViewStateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void EnsureCaretVisible()
    {
        EditingSession? session = Session;
        if (session is null || _viewport.Width <= 0 || CaretRect(session.Selection.Active, session.Selection.Affinity) is not { } caret)
        {
            return;
        }

        double zoom = Zoom;
        Rect scaled = new(caret.Rect.X * zoom, caret.Rect.Y * zoom, Math.Max(2, caret.Rect.Width * zoom), caret.Rect.Height * zoom);
        double centerX = Math.Max(0, (_viewport.Width - _docWidth * zoom) / 2);
        scaled.X += centerX;

        double newX = _offset.X;
        double newY = _offset.Y;
        const double margin = 16;
        if (scaled.Top < _offset.Y + margin)
        {
            newY = scaled.Top - margin;
        }
        else if (scaled.Bottom > _offset.Y + _viewport.Height - margin)
        {
            newY = scaled.Bottom - _viewport.Height + margin;
        }

        if (scaled.Left < _offset.X + margin)
        {
            newX = scaled.Left - margin;
        }
        else if (scaled.Right > _offset.X + _viewport.Width - margin)
        {
            newX = scaled.Right - _viewport.Width + margin;
        }

        if (newX != _offset.X || newY != _offset.Y)
        {
            SetOffsets(newX, newY);
        }
    }

    // ------------------------------------------------------------------ hit testing & navigation

    private Point ToDocument(Point viewPoint)
    {
        double zoom = Zoom;
        return new Point((viewPoint.X - _host.Offset.X) / zoom, (viewPoint.Y - _host.Offset.Y) / zoom);
    }

    private int PageIndexAt(double y)
    {
        int count = _layout.PageCount;
        if (count == 0)
        {
            return -1;
        }

        for (int i = 0; i < count; i++)
        {
            Rect rect = PageRect(i);
            if (y < rect.Bottom + PageGap / 2)
            {
                return i;
            }
        }

        return count - 1;
    }

    private (TextPosition Position, CaretAffinity Affinity)? HitTest(Point viewPoint)
    {
        EditingSession? session = Session;
        if (session is null || _layout.PageCount == 0)
        {
            return null;
        }

        Point doc = ToDocument(viewPoint);
        int pageIndex = PageIndexAt(doc.Y);
        if (pageIndex < 0)
        {
            return null;
        }

        Rect pageRect = PageRect(pageIndex);
        var pagePoint = new Point(doc.X - pageRect.X, doc.Y - pageRect.Y);
        bool editingBody = session.Selection.Story.IsBody;
        List<ParagraphFragment> candidates = _layout.Pages[pageIndex].AllFragments()
            .OfType<ParagraphFragment>()
            .Where(f => f.Story.IsBody == editingBody)
            .ToList();
        if (candidates.Count == 0)
        {
            return null;
        }

        ParagraphFragment fragment = candidates.FirstOrDefault(f => pagePoint.Y >= f.Bounds.Top && pagePoint.Y < f.Bounds.Bottom)
            ?? candidates.MinBy(f => Math.Min(Math.Abs(pagePoint.Y - f.Bounds.Top), Math.Abs(pagePoint.Y - f.Bounds.Bottom)))!;
        return HitTestFragment(fragment, pagePoint);
    }

    private static (TextPosition Position, CaretAffinity Affinity) HitTestFragment(ParagraphFragment fragment, Point pagePoint)
    {
        int lineIndex = fragment.LastLine;
        for (int i = fragment.FirstLine; i <= fragment.LastLine; i++)
        {
            if (pagePoint.Y < fragment.LineBottom(i))
            {
                lineIndex = i;
                break;
            }
        }

        return HitTestLine(fragment, lineIndex, pagePoint.X);
    }

    private static (TextPosition Position, CaretAffinity Affinity) HitTestLine(ParagraphFragment fragment, int lineIndex, double pageX)
    {
        IFormattedLine line = fragment.Layout.Lines[lineIndex];
        PointD origin = fragment.LineOrigin(lineIndex);
        int offset = line.HitTest(pageX - origin.X);
        int visibleEnd = line.End - line.NewlineLength;
        offset = Math.Clamp(offset, line.Start, visibleEnd);
        CaretAffinity affinity = offset == line.End && line.NewlineLength == 0 && lineIndex < fragment.Layout.LineCount - 1
            ? CaretAffinity.Upstream
            : CaretAffinity.Downstream;
        return (new TextPosition(fragment.Story, fragment.Path, offset), affinity);
    }

    private LineRef? CurrentLine(TextPosition position, CaretAffinity affinity)
    {
        if (_layout.Find(position) is not { } found)
        {
            return null;
        }

        int line = Math.Clamp(found.Fragment.Layout.LineIndexOf(position.Offset, affinity == CaretAffinity.Upstream), found.Fragment.FirstLine, found.Fragment.LastLine);
        return new LineRef(found.Page.Index, found.Fragment, line);
    }

    private void MoveVertical(int delta, bool extend)
    {
        EditingSession? session = Session;
        if (session is null || CurrentLine(session.Selection.Active, session.Selection.Affinity) is not { } current)
        {
            return;
        }

        List<LineRef> lines = current.Fragment.Story.IsBody ? _bodyLines : _otherLines;
        int index = lines.FindIndex(l => l.Page == current.Page && ReferenceEquals(l.Fragment, current.Fragment) && l.Line == current.Line);
        if (index < 0)
        {
            return;
        }

        int target = Math.Clamp(index + delta, 0, lines.Count - 1);
        if (target == index)
        {
            // Top/bottom of the story: go to its start/end, like Word.
            TextPosition edge = delta < 0 ? TextNavigation.StoryStart(session.Document, session.Selection.Story) : TextNavigation.StoryEnd(session.Document, session.Selection.Story);
            session.MoveCaret(edge, extend);
            return;
        }

        double caretX = _desiredCaretX ?? (CaretRect(session.Selection.Active, session.Selection.Affinity)?.Rect.X ?? 0);
        _desiredCaretX = caretX;
        LineRef targetLine = lines[target];
        double pageX = caretX - PageRect(targetLine.Page).X;
        (TextPosition position, CaretAffinity affinity) = HitTestLine(targetLine.Fragment, targetLine.Line, pageX);
        SafeMove(session, position, extend, affinity);
        _desiredCaretX = caretX;
    }

    private void MoveToLineEdge(bool start, bool extend)
    {
        EditingSession? session = Session;
        if (session is null || CurrentLine(session.Selection.Active, session.Selection.Affinity) is not { } current)
        {
            return;
        }

        IFormattedLine line = current.Fragment.Layout.Lines[current.Line];
        if (start)
        {
            session.MoveCaret(new TextPosition(current.Fragment.Story, current.Fragment.Path, line.Start), extend);
        }
        else
        {
            int end = line.End - line.NewlineLength;
            bool wrapped = line.NewlineLength == 0 && current.Line < current.Fragment.Layout.LineCount - 1;
            session.MoveCaret(new TextPosition(current.Fragment.Story, current.Fragment.Path, end), extend, wrapped ? CaretAffinity.Upstream : CaretAffinity.Downstream);
        }
    }

    private void MoveByPage(int direction, bool extend)
    {
        EditingSession? session = Session;
        if (session is null || CaretRect(session.Selection.Active, session.Selection.Affinity) is not { } caret)
        {
            return;
        }

        double zoom = Zoom;
        double step = _viewport.Height / zoom;
        double targetY = caret.Rect.Y + direction * step;
        double x = _desiredCaretX ?? caret.Rect.X;
        var viewPoint = new Point(x * zoom + _host.Offset.X, targetY * zoom + _host.Offset.Y);
        SetOffsets(_offset.X, _offset.Y + direction * _viewport.Height);
        if (HitTest(viewPoint) is { } hit)
        {
            SafeMove(session, hit.Position, extend, hit.Affinity);
            _desiredCaretX = x;
        }
    }

    // ------------------------------------------------------------------ mouse

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        Focus();
        EditingSession? session = Session;
        if (session is null)
        {
            return;
        }

        Point viewPoint = e.GetPosition(this);
        _desiredCaretX = null;
        if (e.ClickCount == 2 && TryToggleHeaderFooterAt(viewPoint))
        {
            e.Handled = true;
            return;
        }

        if (HitTest(viewPoint) is not { } hit)
        {
            return;
        }

        switch (e.ClickCount)
        {
            case 2:
                session.SelectWordAt(hit.Position);
                break;
            case 3:
                session.SelectParagraphAt(hit.Position);
                break;
            default:
                SafeMove(session, hit.Position, Keyboard.Modifiers.HasFlag(ModifierKeys.Shift), hit.Affinity);
                _dragging = true;
                CaptureMouse();
                break;
        }

        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!_dragging || e.LeftButton != MouseButtonState.Pressed || Session is not { } session)
        {
            return;
        }

        if (HitTest(e.GetPosition(this)) is { } hit)
        {
            SafeMove(session, hit.Position, extend: true, hit.Affinity);
        }
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (_dragging)
        {
            _dragging = false;
            ReleaseMouseCapture();
            e.Handled = true;
        }
    }

    protected override void OnPreviewMouseWheel(MouseWheelEventArgs e)
    {
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            double factor = e.Delta > 0 ? 1.1 : 1 / 1.1;
            Zoom = Math.Clamp(Math.Round(Zoom * factor, 3), 0.1, 5);
            e.Handled = true;
            return;
        }

        base.OnPreviewMouseWheel(e);
    }

    // ------------------------------------------------------------------ keyboard

    protected override void OnTextInput(TextCompositionEventArgs e)
    {
        base.OnTextInput(e);
        EditingSession? session = Session;
        string text = e.Text;
        if (session is null || string.IsNullOrEmpty(text) || text.All(c => c < ' '))
        {
            return;
        }

        _desiredCaretX = null;
        session.InsertText(text.Replace("\r", string.Empty, StringComparison.Ordinal).Replace("\n", string.Empty, StringComparison.Ordinal));
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        EditingSession? session = Session;
        if (session is null)
        {
            return;
        }

        bool shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        bool ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        bool handled = true;
        bool keepDesiredX = false;
        switch (e.Key)
        {
            case Key.Left:
                MoveHorizontal(-1, ctrl, shift);
                break;
            case Key.Right:
                MoveHorizontal(1, ctrl, shift);
                break;
            case Key.Up:
                if (ctrl)
                {
                    TextPosition? start = TextNavigation.PreviousCharacter(session.Document, TextNavigation.ParagraphStart(session.Selection.Active));
                    session.MoveCaret(session.Selection.Active.Offset > 0 ? TextNavigation.ParagraphStart(session.Selection.Active) : (start is null ? TextNavigation.ParagraphStart(session.Selection.Active) : TextNavigation.ParagraphStart(start.Value)), shift);
                }
                else
                {
                    MoveVertical(-1, shift);
                    keepDesiredX = true;
                }

                break;
            case Key.Down:
                if (ctrl)
                {
                    TextPosition next = TextNavigation.StartOfNextParagraph(session.Document, session.Selection.Active) ?? TextNavigation.ParagraphEnd(session.Document, session.Selection.Active);
                    session.MoveCaret(next, shift);
                }
                else
                {
                    MoveVertical(1, shift);
                    keepDesiredX = true;
                }

                break;
            case Key.Home:
                if (ctrl)
                {
                    session.MoveCaret(TextNavigation.StoryStart(session.Document, session.Selection.Story), shift);
                }
                else
                {
                    MoveToLineEdge(start: true, shift);
                }

                break;
            case Key.End:
                if (ctrl)
                {
                    session.MoveCaret(TextNavigation.StoryEnd(session.Document, session.Selection.Story), shift);
                }
                else
                {
                    MoveToLineEdge(start: false, shift);
                }

                break;
            case Key.PageUp:
                MoveByPage(-1, shift);
                keepDesiredX = true;
                break;
            case Key.PageDown:
                MoveByPage(1, shift);
                keepDesiredX = true;
                break;
            case Key.Back:
                if (ctrl)
                {
                    session.DeleteWordBackward();
                }
                else
                {
                    session.Backspace();
                }

                break;
            case Key.Delete:
                if (shift)
                {
                    CutSelection();
                }
                else if (ctrl)
                {
                    session.DeleteWordForward();
                }
                else
                {
                    session.Delete();
                }

                break;
            case Key.Return:
                if (shift)
                {
                    session.InsertBreak(Core.Model.BreakKind.Line);
                }
                else if (ctrl)
                {
                    session.InsertBreak(Core.Model.BreakKind.Page);
                }
                else
                {
                    session.InsertParagraphBreak();
                }

                break;
            case Key.Tab:
                session.InsertText("\t");
                break;
            case Key.Insert when shift:
                PasteFromClipboard();
                break;
            case Key.Insert when ctrl:
                CopySelection();
                break;
            case Key.Escape:
                if (_headerFooterMode && session.Selection.IsCollapsed)
                {
                    ExitHeaderFooter();
                }
                else
                {
                    session.SetSelection(Selection.Caret(session.Selection.Active, session.Selection.Affinity));
                }

                break;
            case Key.A when ctrl:
                session.SelectAll();
                break;
            case Key.C when ctrl:
                CopySelection();
                break;
            case Key.X when ctrl:
                CutSelection();
                break;
            case Key.V when ctrl:
                PasteFromClipboard();
                break;
            case Key.Z when ctrl:
                session.Undo();
                break;
            case Key.Y when ctrl:
                session.Redo();
                break;
            default:
                handled = false;
                break;
        }

        if (handled)
        {
            if (!keepDesiredX)
            {
                _desiredCaretX = null;
            }

            e.Handled = true;
        }
    }

    private void MoveHorizontal(int direction, bool byWord, bool extend)
    {
        EditingSession? session = Session;
        if (session is null)
        {
            return;
        }

        Selection selection = session.Selection;
        if (!extend && !selection.IsCollapsed && !byWord)
        {
            session.MoveCaret(direction < 0 ? selection.Start : selection.End, extend: false);
            return;
        }

        TextPosition from = selection.Active;
        TextPosition? target = (direction, byWord) switch
        {
            (< 0, true) => TextNavigation.PreviousWord(session.Document, from),
            (< 0, false) => TextNavigation.PreviousCharacter(session.Document, from),
            (_, true) => TextNavigation.NextWord(session.Document, from),
            _ => TextNavigation.NextCharacter(session.Document, from),
        };
        if (target is { } position)
        {
            session.MoveCaret(position, extend);
        }
    }

    // ------------------------------------------------------------------ clipboard

    public void CopySelection()
    {
        EditingSession? session = Session;
        if (session is null || session.Selection.IsCollapsed)
        {
            return;
        }

        DocumentFragment fragment = session.Copy();
        string text = fragment.ToPlainText();
        try
        {
            Clipboard.SetText(text);
            _lastCopied = (text, fragment);
        }
        catch (COMException)
        {
            // Another process holds the clipboard; ignore.
        }
    }

    public void CutSelection()
    {
        EditingSession? session = Session;
        if (session is null || session.Selection.IsCollapsed)
        {
            return;
        }

        CopySelection();
        session.DeleteSelection();
    }

    public void PasteFromClipboard()
    {
        EditingSession? session = Session;
        if (session is null)
        {
            return;
        }

        string text;
        try
        {
            if (!Clipboard.ContainsText())
            {
                return;
            }

            text = Clipboard.GetText();
        }
        catch (COMException)
        {
            return;
        }

        if (_lastCopied is { } last && string.Equals(last.Text, text, StringComparison.Ordinal))
        {
            session.Paste(last.Fragment);
        }
        else
        {
            session.InsertPlainText(text.Replace("\t", "\t", StringComparison.Ordinal));
        }
    }

    // ------------------------------------------------------------------ IScrollInfo

    public bool CanVerticallyScroll { get; set; } = true;

    public bool CanHorizontallyScroll { get; set; } = true;

    public double ExtentWidth => _docWidth * Zoom;

    public double ExtentHeight => _docHeight * Zoom;

    public double ViewportWidth => _viewport.Width;

    public double ViewportHeight => _viewport.Height;

    public double HorizontalOffset => _offset.X;

    public double VerticalOffset => _offset.Y;

    public ScrollViewer? ScrollOwner { get; set; }

    public void LineUp() => SetOffsets(_offset.X, _offset.Y - ScrollLineSize);

    public void LineDown() => SetOffsets(_offset.X, _offset.Y + ScrollLineSize);

    public void LineLeft() => SetOffsets(_offset.X - ScrollLineSize, _offset.Y);

    public void LineRight() => SetOffsets(_offset.X + ScrollLineSize, _offset.Y);

    public void PageUp() => SetOffsets(_offset.X, _offset.Y - _viewport.Height);

    public void PageDown() => SetOffsets(_offset.X, _offset.Y + _viewport.Height);

    public void PageLeft() => SetOffsets(_offset.X - _viewport.Width, _offset.Y);

    public void PageRight() => SetOffsets(_offset.X + _viewport.Width, _offset.Y);

    public void MouseWheelUp() => SetOffsets(_offset.X, _offset.Y - ScrollLineSize * SystemParameters.WheelScrollLines);

    public void MouseWheelDown() => SetOffsets(_offset.X, _offset.Y + ScrollLineSize * SystemParameters.WheelScrollLines);

    public void MouseWheelLeft() => SetOffsets(_offset.X - ScrollLineSize * 3, _offset.Y);

    public void MouseWheelRight() => SetOffsets(_offset.X + ScrollLineSize * 3, _offset.Y);

    public void SetHorizontalOffset(double offset) => SetOffsets(offset, _offset.Y);

    public void SetVerticalOffset(double offset) => SetOffsets(_offset.X, offset);

    public Rect MakeVisible(Visual visual, Rect rectangle) => rectangle;

    private void SetOffsets(double x, double y)
    {
        double maxX = Math.Max(0, ExtentWidth - _viewport.Width);
        double maxY = Math.Max(0, ExtentHeight - _viewport.Height);
        var clamped = new Vector(Math.Clamp(x, 0, maxX), Math.Clamp(y, 0, maxY));
        if (clamped == _offset)
        {
            return;
        }

        _offset = clamped;
        UpdateHostTransform();
        RealizePages();
        ScrollOwner?.InvalidateScrollInfo();
    }

    private void UpdateScrollExtent()
    {
        double maxX = Math.Max(0, ExtentWidth - _viewport.Width);
        double maxY = Math.Max(0, ExtentHeight - _viewport.Height);
        _offset = new Vector(Math.Clamp(_offset.X, 0, maxX), Math.Clamp(_offset.Y, 0, maxY));
        UpdateHostTransform();
        ScrollOwner?.InvalidateScrollInfo();
    }

    // ------------------------------------------------------------------ helpers

    private static TimeSpan CaretBlinkInterval()
    {
        uint ms;
        try
        {
            ms = GetCaretBlinkTime();
        }
        catch (EntryPointNotFoundException)
        {
            ms = 530;
        }

        return ms == uint.MaxValue || ms == 0 ? TimeSpan.Zero : TimeSpan.FromMilliseconds(ms);
    }

    [LibraryImport("user32.dll")]
    private static partial uint GetCaretBlinkTime();

    private sealed record LineRef(int Page, ParagraphFragment Fragment, int Line);

    private sealed class PageVisuals
    {
        public PageVisuals(int pageIndex)
        {
            PageIndex = pageIndex;
            Root.Children.Add(Chrome);
            Root.Children.Add(Content);
            Root.Children.Add(Overlay);
            Root.Children.Add(Selection);
        }

        public int PageIndex { get; }

        public ContainerVisual Root { get; } = new();

        public DrawingVisual Chrome { get; } = new();

        public DrawingVisual Content { get; } = new();

        /// <summary>Dimming and guides shown while editing a header or footer.</summary>
        public DrawingVisual Overlay { get; } = new();

        public DrawingVisual Selection { get; } = new();
    }
}
