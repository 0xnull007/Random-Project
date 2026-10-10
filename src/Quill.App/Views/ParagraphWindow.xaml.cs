using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using Quill.Core.Model;
using Quill.Core.Styles;
using Quill.Core.Units;

namespace Quill.App.Views;

/// <summary>Word's Paragraph dialog: indents, spacing, line spacing and pagination. Only values the user changed are applied.</summary>
public partial class ParagraphWindow : Window
{
    private readonly ResolvedParagraphProperties _initial;
    private readonly LengthUnit _unit;
    private readonly string _unitName;

    public ParagraphWindow(ResolvedParagraphProperties current)
    {
        ArgumentNullException.ThrowIfNull(current);
        InitializeComponent();
        _initial = current;
        _unit = RegionInfo.CurrentRegion.IsMetric ? LengthUnit.Centimeters : LengthUnit.Inches;
        _unitName = _unit == LengthUnit.Centimeters ? "cm" : "in";
        UnitHint.Text = "Indents in " + (_unit == LengthUnit.Centimeters ? "centimeters" : "inches");
        AlignmentBox.SelectedIndex = (int)current.Alignment;
        LeftBox.Text = current.LeftIndent.Format(_unit);
        RightBox.Text = current.RightIndent.Format(_unit);
        int first = current.FirstLineIndent.Value;
        SpecialBox.SelectedIndex = first > 0 ? 1 : first < 0 ? 2 : 0;
        ByBox.Text = first == 0 ? Twips.FromInches(0.5).Format(_unit) : new Twips(Math.Abs(first)).Format(_unit);
        BeforeBox.Text = current.SpaceBefore.ToPoints().ToString("0.#", CultureInfo.CurrentCulture);
        AfterBox.Text = current.SpaceAfter.ToPoints().ToString("0.#", CultureInfo.CurrentCulture);
        LineSpacing spacing = current.LineSpacing;
        LineBox.SelectedIndex = spacing.Rule switch
        {
            LineSpacingRule.AtLeast => 3,
            LineSpacingRule.Exact => 4,
            _ when spacing.Value == LineSpacing.Single.Value => 0,
            _ when spacing.Value == LineSpacing.OneAndHalf.Value => 1,
            _ when spacing.Value == LineSpacing.Double.Value => 2,
            _ => 5,
        };
        AtBox.Text = spacing.Rule == LineSpacingRule.Auto
            ? spacing.Factor.ToString("0.##", CultureInfo.CurrentCulture)
            : spacing.Height.ToPoints().ToString("0.#", CultureInfo.CurrentCulture);
        ContextualBox.IsChecked = current.ContextualSpacing;
        WidowBox.IsChecked = current.WidowControl;
        KeepNextBox.IsChecked = current.KeepWithNext;
        KeepLinesBox.IsChecked = current.KeepLinesTogether;
        PageBreakBox.IsChecked = current.PageBreakBefore;
        OnSpecialChanged(this, null);
        OnLineChanged(this, null);
        Loaded += (_, _) => AlignmentBox.Focus();
    }

    /// <summary>The changes to apply; empty when nothing changed.</summary>
    public ParagraphProperties Delta { get; private set; } = ParagraphProperties.Empty;

    private void OnSpecialChanged(object sender, SelectionChangedEventArgs? e) => ByBox.IsEnabled = SpecialBox.SelectedIndex > 0;

    private void OnLineChanged(object sender, SelectionChangedEventArgs? e)
    {
        int index = LineBox.SelectedIndex;
        AtBox.IsEnabled = index >= 3;
        AtHint.Text = index switch
        {
            3 or 4 => "At: line height in points",
            5 => "At: number of lines, e.g. 1.15",
            _ => string.Empty,
        };
    }

    private void OnOk(object sender, RoutedEventArgs e)
    {
        if (!Twips.TryParse(LeftBox.Text, _unit, out Twips left) || !Twips.TryParse(RightBox.Text, _unit, out Twips right) || !Twips.TryParse(ByBox.Text, _unit, out Twips by)
            || !double.TryParse(BeforeBox.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out double before) || before < 0
            || !double.TryParse(AfterBox.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out double after) || after < 0
            || !double.TryParse(AtBox.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out double at) || at <= 0)
        {
            Fail("Please check the numbers: indents in " + _unitName + ", spacing in points, line spacing as points or a number of lines.");
            return;
        }

        LineSpacing spacing = LineBox.SelectedIndex switch
        {
            0 => LineSpacing.Single,
            1 => LineSpacing.OneAndHalf,
            2 => LineSpacing.Double,
            3 => LineSpacing.AtLeast(Twips.FromPoints(at)),
            4 => LineSpacing.Exactly(Twips.FromPoints(at)),
            _ => LineSpacing.Multiple(at),
        };
        if (LineBox.SelectedIndex == 5 && at is < 0.5 or > 132)
        {
            Fail("Multiple line spacing must be between 0.5 and 132 lines.");
            return;
        }

        Twips firstLine = SpecialBox.SelectedIndex switch
        {
            1 => by,
            2 => new Twips(-by.Value),
            _ => Twips.Zero,
        };

        var delta = new ParagraphProperties();
        var alignment = (Alignment)Math.Max(0, AlignmentBox.SelectedIndex);
        if (alignment != _initial.Alignment)
        {
            delta = delta with { Alignment = alignment };
        }

        if (left != _initial.LeftIndent)
        {
            delta = delta with { LeftIndent = left };
        }

        if (right != _initial.RightIndent)
        {
            delta = delta with { RightIndent = right };
        }

        if (firstLine != _initial.FirstLineIndent)
        {
            delta = delta with { FirstLineIndent = firstLine };
        }

        Twips spaceBefore = Twips.FromPoints(before);
        Twips spaceAfter = Twips.FromPoints(after);
        if (spaceBefore != _initial.SpaceBefore)
        {
            delta = delta with { SpaceBefore = spaceBefore };
        }

        if (spaceAfter != _initial.SpaceAfter)
        {
            delta = delta with { SpaceAfter = spaceAfter };
        }

        if (spacing != _initial.LineSpacing)
        {
            delta = delta with { LineSpacing = spacing };
        }

        if ((ContextualBox.IsChecked == true) != _initial.ContextualSpacing)
        {
            delta = delta with { ContextualSpacing = ContextualBox.IsChecked == true };
        }

        if ((WidowBox.IsChecked == true) != _initial.WidowControl)
        {
            delta = delta with { WidowControl = WidowBox.IsChecked == true };
        }

        if ((KeepNextBox.IsChecked == true) != _initial.KeepWithNext)
        {
            delta = delta with { KeepWithNext = KeepNextBox.IsChecked == true };
        }

        if ((KeepLinesBox.IsChecked == true) != _initial.KeepLinesTogether)
        {
            delta = delta with { KeepLinesTogether = KeepLinesBox.IsChecked == true };
        }

        if ((PageBreakBox.IsChecked == true) != _initial.PageBreakBefore)
        {
            delta = delta with { PageBreakBefore = PageBreakBox.IsChecked == true };
        }

        Delta = delta;
        DialogResult = true;
    }

    private void Fail(string message)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
    }
}
