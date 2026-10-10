using System.Globalization;
using System.Windows;
using System.Windows.Media;
using Quill.Core.Model;
using Quill.Core.Styles;
using Quill.Core.Units;
using Quill.Layout.Wpf;

namespace Quill.App.Views;

/// <summary>Ctrl+D: the full character formatting of the selection. Only values the user changed are applied.</summary>
public partial class FontWindow : Window
{
    private static readonly double[] Sizes = [8, 9, 10, 10.5, 11, 12, 14, 16, 18, 20, 22, 24, 26, 28, 36, 48, 72];
    private static readonly (UnderlineStyle Style, string Name)[] Underlines =
    [
        (UnderlineStyle.None, "(none)"),
        (UnderlineStyle.Single, "Single"),
        (UnderlineStyle.Double, "Double"),
        (UnderlineStyle.Dotted, "Dotted"),
        (UnderlineStyle.Dashed, "Dashed"),
        (UnderlineStyle.Wavy, "Wavy"),
        (UnderlineStyle.Words, "Words only"),
    ];

    private readonly ResolvedRunProperties _initial;
    private DocColor? _pickedColor;
    private bool _loading = true;

    public FontWindow(ResolvedRunProperties current, IEnumerable<string> families)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(families);
        InitializeComponent();
        _initial = current;
        FamilyBox.ItemsSource = families.ToList();
        FamilyBox.Text = current.FontFamily;
        StyleBox.SelectedIndex = (current.Bold ? 2 : 0) + (current.Italic ? 1 : 0);
        SizeBox.ItemsSource = Sizes.Select(s => s.ToString(CultureInfo.CurrentCulture)).ToList();
        SizeBox.Text = current.FontSize.ToPoints().ToString(CultureInfo.CurrentCulture);
        UnderlineBox.ItemsSource = Underlines.Select(u => u.Name).ToList();
        UnderlineBox.SelectedIndex = Math.Max(0, Array.FindIndex(Underlines, u => u.Style == current.Underline));
        ColorPicker.CurrentColor = current.Color.IsAuto ? Colors.Black : System.Windows.Media.Color.FromRgb(current.Color.R, current.Color.G, current.Color.B);
        ColorText.Text = current.Color.IsAuto ? "Automatic" : "#" + Hex(current.Color);
        StrikeBox.IsChecked = current.Strikethrough;
        DoubleStrikeBox.IsChecked = current.DoubleStrikethrough;
        SuperBox.IsChecked = current.VerticalAlignment == VerticalTextAlignment.Superscript;
        SubBox.IsChecked = current.VerticalAlignment == VerticalTextAlignment.Subscript;
        SmallCapsBox.IsChecked = current.SmallCaps;
        AllCapsBox.IsChecked = current.AllCaps;
        HiddenBox.IsChecked = current.Hidden;
        _loading = false;
        UpdatePreview();
        Loaded += (_, _) => FamilyBox.Focus();
    }

    /// <summary>The changes to apply; empty when nothing changed.</summary>
    public RunProperties Delta { get; private set; } = RunProperties.Empty;

    private static string Hex(DocColor color) => color.R.ToString("X2", CultureInfo.InvariantCulture) + color.G.ToString("X2", CultureInfo.InvariantCulture) + color.B.ToString("X2", CultureInfo.InvariantCulture);

    private bool Bold => StyleBox.SelectedIndex >= 2;

    private bool Italic => StyleBox.SelectedIndex is 1 or 3;

    private UnderlineStyle Underline => Underlines[Math.Max(0, UnderlineBox.SelectedIndex)].Style;

    private double? Size => double.TryParse(SizeBox.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out double size) && size is >= 1 and <= 400 ? size : null;

    private DocColor ChosenColor => _pickedColor ?? _initial.Color;

    private void OnColorSelected(object? sender, string value)
    {
        _pickedColor = value == "auto" ? DocColor.Auto : DocColor.Parse(value);
        ColorText.Text = _pickedColor.Value.IsAuto ? "Automatic" : "#" + Hex(_pickedColor.Value);
        UpdatePreview();
    }

    private void OnSuperClick(object sender, RoutedEventArgs e)
    {
        if (SuperBox.IsChecked == true)
        {
            SubBox.IsChecked = false;
        }

        UpdatePreview();
    }

    private void OnSubClick(object sender, RoutedEventArgs e)
    {
        if (SubBox.IsChecked == true)
        {
            SuperBox.IsChecked = false;
        }

        UpdatePreview();
    }

    private void OnChanged(object sender, RoutedEventArgs e) => UpdatePreview();

    private void UpdatePreview()
    {
        if (_loading)
        {
            return;
        }

        string family = string.IsNullOrWhiteSpace(FamilyBox.Text) ? _initial.FontFamily : FamilyBox.Text;
        Preview.FontFamily = FontCatalog.Shared.GetFamily(family);
        Preview.FontSize = Math.Clamp((Size ?? _initial.FontSize.ToPoints()) * 96.0 / 72.0, 6, 40);
        Preview.FontWeight = Bold ? FontWeights.Bold : FontWeights.Normal;
        Preview.FontStyle = Italic ? FontStyles.Italic : FontStyles.Normal;
        var decorations = new TextDecorationCollection();
        if (Underline != UnderlineStyle.None)
        {
            decorations.Add(TextDecorations.Underline);
        }

        if (StrikeBox.IsChecked == true || DoubleStrikeBox.IsChecked == true)
        {
            decorations.Add(TextDecorations.Strikethrough);
        }

        Preview.TextDecorations = decorations;
        DocColor color = ChosenColor;
        Preview.Foreground = color.IsAuto ? Brushes.Black : new SolidColorBrush(System.Windows.Media.Color.FromRgb(color.R, color.G, color.B));
        string text = "The quick brown fox jumps over the lazy dog";
        Preview.Text = AllCapsBox.IsChecked == true || SmallCapsBox.IsChecked == true ? text.ToUpperInvariant() : text;
        Preview.Opacity = HiddenBox.IsChecked == true ? 0.4 : 1;
    }

    private void OnOk(object sender, RoutedEventArgs e)
    {
        if (Size is not { } size)
        {
            ErrorText.Text = "Please enter a size between 1 and 400 points.";
            ErrorText.Visibility = Visibility.Visible;
            return;
        }

        var delta = new RunProperties();
        string family = FamilyBox.Text.Trim();
        if (family.Length > 0 && !string.Equals(family, _initial.FontFamily, StringComparison.OrdinalIgnoreCase))
        {
            delta = delta with { FontFamily = family };
        }

        HalfPoints halfPoints = HalfPoints.FromPoints(size);
        if (halfPoints != _initial.FontSize)
        {
            delta = delta with { FontSize = halfPoints };
        }

        if (Bold != _initial.Bold)
        {
            delta = delta with { Bold = Bold };
        }

        if (Italic != _initial.Italic)
        {
            delta = delta with { Italic = Italic };
        }

        if (Underline != _initial.Underline)
        {
            delta = delta with { Underline = Underline };
        }

        if (_pickedColor is { } picked && picked != _initial.Color)
        {
            delta = delta with { Color = picked };
        }

        bool strike = StrikeBox.IsChecked == true;
        bool doubleStrike = DoubleStrikeBox.IsChecked == true;
        bool smallCaps = SmallCapsBox.IsChecked == true;
        bool allCaps = AllCapsBox.IsChecked == true;
        bool hidden = HiddenBox.IsChecked == true;
        VerticalTextAlignment vertical = SuperBox.IsChecked == true ? VerticalTextAlignment.Superscript : SubBox.IsChecked == true ? VerticalTextAlignment.Subscript : VerticalTextAlignment.Baseline;
        if (strike != _initial.Strikethrough)
        {
            delta = delta with { Strikethrough = strike };
        }

        if (doubleStrike != _initial.DoubleStrikethrough)
        {
            delta = delta with { DoubleStrikethrough = doubleStrike };
        }

        if (smallCaps != _initial.SmallCaps)
        {
            delta = delta with { SmallCaps = smallCaps };
        }

        if (allCaps != _initial.AllCaps)
        {
            delta = delta with { AllCaps = allCaps };
        }

        if (hidden != _initial.Hidden)
        {
            delta = delta with { Hidden = hidden };
        }

        if (vertical != _initial.VerticalAlignment)
        {
            delta = delta with { VerticalAlignment = vertical };
        }

        Delta = delta;
        DialogResult = true;
    }
}
