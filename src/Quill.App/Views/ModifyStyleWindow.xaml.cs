using System.Globalization;
using System.Windows;
using System.Windows.Media;
using Quill.Core.Editing;
using Quill.Core.Model;
using Quill.Core.Styles;
using Quill.Layout.Wpf;
using Style = Quill.Core.Model.Style;

namespace Quill.App.Views;

/// <summary>Changes a paragraph style's formatting through the Font and Paragraph dialogs; every paragraph in the style follows.</summary>
public partial class ModifyStyleWindow : Window
{
    private readonly EditingSession _session;
    private readonly Style _original;
    private readonly IEnumerable<string> _families;
    private RunProperties _runDelta = RunProperties.Empty;
    private ParagraphProperties _paragraphDelta = ParagraphProperties.Empty;

    public ModifyStyleWindow(EditingSession session, Style style, IEnumerable<string> families)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(style);
        InitializeComponent();
        _session = session;
        _original = style;
        _families = families;
        Title = "Modify Style: " + style.Name;
        NameText.Text = style.Name + (style.BasedOn is { } basedOn && session.Document.Styles.Get(basedOn) is { } parent ? "  (based on " + parent.Name + ")" : string.Empty);
        List<Style> paragraphStyles = session.Document.Styles.ParagraphStyles.OrderBy(s => s.Priority).ThenBy(s => s.Name).ToList();
        NextBox.ItemsSource = paragraphStyles;
        NextBox.SelectedItem = paragraphStyles.FirstOrDefault(s => s.Id == (style.Next ?? style.Id)) ?? paragraphStyles.FirstOrDefault(s => s.Id == style.Id);
        QuickBox.IsChecked = style.QuickFormat;
        UpdateSummary();
    }

    /// <summary>The edited style, set when the dialog returns true (unless <see cref="UpdateFromSelection"/> is set).</summary>
    public Style Result { get; private set; } = null!;

    /// <summary>True when the user chose "Update to Match Selection".</summary>
    public bool UpdateFromSelection { get; private set; }

    private ResolvedRunProperties CurrentRun()
    {
        ResolvedRunProperties resolved = _session.ResolveStyleRun(_original.Id);
        RunProperties merged = new RunProperties
        {
            FontFamily = resolved.FontFamily,
            FontSize = resolved.FontSize,
            Bold = resolved.Bold,
            Italic = resolved.Italic,
            Underline = resolved.Underline,
            Strikethrough = resolved.Strikethrough,
            DoubleStrikethrough = resolved.DoubleStrikethrough,
            Color = resolved.Color,
            Highlight = resolved.Highlight,
            VerticalAlignment = resolved.VerticalAlignment,
            SmallCaps = resolved.SmallCaps,
            AllCaps = resolved.AllCaps,
            Hidden = resolved.Hidden,
            Language = resolved.Language,
        }.Merge(_runDelta);
        return resolved with
        {
            FontFamily = merged.FontFamily!,
            FontSize = merged.FontSize!.Value,
            Bold = merged.Bold!.Value,
            Italic = merged.Italic!.Value,
            Underline = merged.Underline!.Value,
            Strikethrough = merged.Strikethrough!.Value,
            DoubleStrikethrough = merged.DoubleStrikethrough!.Value,
            Color = merged.Color!.Value,
            Highlight = merged.Highlight!.Value,
            VerticalAlignment = merged.VerticalAlignment!.Value,
            SmallCaps = merged.SmallCaps!.Value,
            AllCaps = merged.AllCaps!.Value,
            Hidden = merged.Hidden!.Value,
        };
    }

    private ResolvedParagraphProperties CurrentParagraph()
    {
        ResolvedParagraphProperties resolved = _session.ResolveStyleParagraph(_original.Id);
        return resolved with
        {
            Alignment = _paragraphDelta.Alignment ?? resolved.Alignment,
            LeftIndent = _paragraphDelta.LeftIndent ?? resolved.LeftIndent,
            RightIndent = _paragraphDelta.RightIndent ?? resolved.RightIndent,
            FirstLineIndent = _paragraphDelta.FirstLineIndent ?? resolved.FirstLineIndent,
            SpaceBefore = _paragraphDelta.SpaceBefore ?? resolved.SpaceBefore,
            SpaceAfter = _paragraphDelta.SpaceAfter ?? resolved.SpaceAfter,
            LineSpacing = _paragraphDelta.LineSpacing ?? resolved.LineSpacing,
            KeepWithNext = _paragraphDelta.KeepWithNext ?? resolved.KeepWithNext,
            KeepLinesTogether = _paragraphDelta.KeepLinesTogether ?? resolved.KeepLinesTogether,
            PageBreakBefore = _paragraphDelta.PageBreakBefore ?? resolved.PageBreakBefore,
            WidowControl = _paragraphDelta.WidowControl ?? resolved.WidowControl,
            ContextualSpacing = _paragraphDelta.ContextualSpacing ?? resolved.ContextualSpacing,
        };
    }

    private void UpdateSummary()
    {
        ResolvedRunProperties run = CurrentRun();
        ResolvedParagraphProperties paragraph = CurrentParagraph();
        string spacing = paragraph.LineSpacing.Rule == Quill.Core.Units.LineSpacingRule.Auto
            ? "line spacing " + paragraph.LineSpacing.Factor.ToString("0.##", CultureInfo.CurrentCulture)
            : "line height " + paragraph.LineSpacing.Height.ToPoints().ToString("0.#", CultureInfo.CurrentCulture) + " pt";
        SummaryText.Text = string.Format(
            CultureInfo.CurrentCulture,
            "{0}, {1} pt{2}{3}{4}, {5}, space before {6} pt, after {7} pt, {8}",
            run.FontFamily,
            run.FontSize.ToPoints(),
            run.Bold ? ", bold" : string.Empty,
            run.Italic ? ", italic" : string.Empty,
            run.Color.IsAuto ? string.Empty : ", color #" + run.Color.ToHex(),
            paragraph.Alignment.ToString().ToLowerInvariant(),
            paragraph.SpaceBefore.ToPoints().ToString("0.#", CultureInfo.CurrentCulture),
            paragraph.SpaceAfter.ToPoints().ToString("0.#", CultureInfo.CurrentCulture),
            spacing);
        Preview.FontFamily = FontCatalog.Shared.GetFamily(run.FontFamily);
        Preview.FontSize = Math.Clamp(run.FontSize.ToDips(), 6, 40);
        Preview.FontWeight = run.Bold ? FontWeights.Bold : FontWeights.Normal;
        Preview.FontStyle = run.Italic ? FontStyles.Italic : FontStyles.Normal;
        Preview.Foreground = run.Color.IsAuto ? Brushes.Black : new SolidColorBrush(Color.FromRgb(run.Color.R, run.Color.G, run.Color.B));
        Preview.TextAlignment = paragraph.Alignment switch
        {
            Alignment.Center => TextAlignment.Center,
            Alignment.Right => TextAlignment.Right,
            _ => TextAlignment.Left,
        };
    }

    private void OnFont(object sender, RoutedEventArgs e)
    {
        var dialog = new FontWindow(CurrentRun(), _families) { Owner = this };
        if (dialog.ShowDialog() == true)
        {
            _runDelta = _runDelta.Merge(dialog.Delta);
            UpdateSummary();
        }
    }

    private void OnParagraph(object sender, RoutedEventArgs e)
    {
        var dialog = new ParagraphWindow(CurrentParagraph()) { Owner = this };
        if (dialog.ShowDialog() == true)
        {
            _paragraphDelta = _paragraphDelta.Merge(dialog.Delta);
            UpdateSummary();
        }
    }

    private void OnUpdateFromSelection(object sender, RoutedEventArgs e)
    {
        UpdateFromSelection = true;
        DialogResult = true;
    }

    private void OnOk(object sender, RoutedEventArgs e)
    {
        Result = _original with
        {
            RunProperties = _original.RunProperties.Merge(_runDelta),
            ParagraphProperties = _original.ParagraphProperties.Merge(_paragraphDelta),
            QuickFormat = QuickBox.IsChecked == true,
            Next = (NextBox.SelectedItem as Style)?.Id ?? _original.Next,
        };
        DialogResult = true;
    }
}
