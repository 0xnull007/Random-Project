using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using Quill.Core.Model;
using Quill.Core.Units;
using Orientation = Quill.Core.Model.Orientation;

namespace Quill.App.Views;

/// <summary>Paper size, orientation, margins and header/footer options for a section (or the whole document).</summary>
public partial class PageSetupWindow : Window
{
    private static readonly (string Name, Twips Width, Twips Height)[] PaperSizes =
    [
        ("Letter", Twips.FromInches(8.5), Twips.FromInches(11)),
        ("Legal", Twips.FromInches(8.5), Twips.FromInches(14)),
        ("Tabloid", Twips.FromInches(11), Twips.FromInches(17)),
        ("Executive", Twips.FromInches(7.25), Twips.FromInches(10.5)),
        ("A3", Twips.FromCentimeters(29.7), Twips.FromCentimeters(42)),
        ("A4", Twips.FromCentimeters(21), Twips.FromCentimeters(29.7)),
        ("A5", Twips.FromCentimeters(14.8), Twips.FromCentimeters(21)),
        ("B5", Twips.FromCentimeters(17.6), Twips.FromCentimeters(25)),
        ("Custom", Twips.Zero, Twips.Zero),
    ];

    private static readonly Twips MinPageSide = Twips.FromInches(1);
    private static readonly Twips MaxPageSide = Twips.FromInches(22);
    private static readonly Twips MinBody = Twips.FromInches(0.5);

    private readonly LengthUnit _unit;
    private bool _updating;

    public PageSetupWindow(SectionProperties current, bool evenAndOddHeaders, bool hasMultipleSections)
    {
        ArgumentNullException.ThrowIfNull(current);
        InitializeComponent();
        _unit = Quill.App.Settings.UnitPreference.Current;
        Loaded += (_, _) => PopupThemeFix.AttachAll(this);

        string unitText = _unit == LengthUnit.Centimeters ? "cm" : "in";
        foreach (TextBlock label in new[] { WidthUnit, HeightUnit, TopUnit, BottomUnit, LeftUnit, RightUnit, GutterUnit, HeaderUnit, FooterUnit })
        {
            label.Text = unitText;
        }

        _updating = true;
        PaperSize.ItemsSource = PaperSizes.Select(p => p.Name).ToList();
        WidthBox.Text = current.PageWidth.Format(_unit);
        HeightBox.Text = current.PageHeight.Format(_unit);
        PortraitRadio.IsChecked = current.Orientation == Orientation.Portrait;
        LandscapeRadio.IsChecked = current.Orientation == Orientation.Landscape;
        TopBox.Text = current.MarginTop.Format(_unit);
        BottomBox.Text = current.MarginBottom.Format(_unit);
        LeftBox.Text = current.MarginLeft.Format(_unit);
        RightBox.Text = current.MarginRight.Format(_unit);
        GutterBox.Text = current.Gutter.Format(_unit);
        HeaderBox.Text = current.HeaderDistance.Format(_unit);
        FooterBox.Text = current.FooterDistance.Format(_unit);
        DifferentFirstPage.IsChecked = current.TitlePage;
        DifferentOddEven.IsChecked = evenAndOddHeaders;
        ApplyTo.SelectedIndex = 0;
        ApplyTo.IsEnabled = hasMultipleSections;
        PaperSize.SelectedIndex = MatchPaperSize(current.PageWidth, current.PageHeight);
        _updating = false;
    }

    /// <summary>The section properties the user confirmed; null until OK.</summary>
    public SectionProperties? Result { get; private set; }

    public bool ApplyToWholeDocument => ApplyTo.SelectedIndex == 1;

    public bool EvenAndOddHeaders => DifferentOddEven.IsChecked == true;

    private static int MatchPaperSize(Twips width, Twips height)
    {
        Twips shortSide = Twips.Min(width, height);
        Twips longSide = Twips.Max(width, height);
        for (int i = 0; i < PaperSizes.Length - 1; i++)
        {
            if (Math.Abs(PaperSizes[i].Width.Value - shortSide.Value) <= 20 && Math.Abs(PaperSizes[i].Height.Value - longSide.Value) <= 20)
            {
                return i;
            }
        }

        return PaperSizes.Length - 1;
    }

    private void OnPaperSizeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updating || PaperSize.SelectedIndex < 0 || PaperSize.SelectedIndex >= PaperSizes.Length - 1)
        {
            return;
        }

        (_, Twips width, Twips height) = PaperSizes[PaperSize.SelectedIndex];
        bool landscape = LandscapeRadio.IsChecked == true;
        _updating = true;
        WidthBox.Text = (landscape ? height : width).Format(_unit);
        HeightBox.Text = (landscape ? width : height).Format(_unit);
        _updating = false;
    }

    private void OnSizeTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_updating)
        {
            return;
        }

        _updating = true;
        PaperSize.SelectedIndex = PaperSizes.Length - 1; // Custom
        _updating = false;
    }

    private void OnOrientationChanged(object sender, RoutedEventArgs e)
    {
        if (_updating || !Twips.TryParse(WidthBox.Text, _unit, out Twips width) || !Twips.TryParse(HeightBox.Text, _unit, out Twips height))
        {
            return;
        }

        bool landscape = LandscapeRadio.IsChecked == true;
        if ((landscape && width < height) || (!landscape && width > height))
        {
            _updating = true;
            WidthBox.Text = height.Format(_unit);
            HeightBox.Text = width.Format(_unit);
            _updating = false;
        }
    }

    private void OnOk(object sender, RoutedEventArgs e)
    {
        var errors = new List<string>();
        Twips width = Read(WidthBox, "Width", errors);
        Twips height = Read(HeightBox, "Height", errors);
        Twips top = Read(TopBox, "Top margin", errors);
        Twips bottom = Read(BottomBox, "Bottom margin", errors);
        Twips left = Read(LeftBox, "Left margin", errors);
        Twips right = Read(RightBox, "Right margin", errors);
        Twips gutter = Read(GutterBox, "Gutter", errors);
        Twips header = Read(HeaderBox, "Header distance", errors);
        Twips footer = Read(FooterBox, "Footer distance", errors);

        if (errors.Count == 0)
        {
            if (width < MinPageSide || width > MaxPageSide || height < MinPageSide || height > MaxPageSide)
            {
                errors.Add($"Paper size must be between {MinPageSide.Format(_unit)} and {MaxPageSide.Format(_unit)} {UnitText}.");
            }

            if (top.IsNegative || bottom.IsNegative || left.IsNegative || right.IsNegative || gutter.IsNegative || header.IsNegative || footer.IsNegative)
            {
                errors.Add("Margins and distances cannot be negative.");
            }

            if (width - left - right - gutter < MinBody)
            {
                errors.Add("The left and right margins leave too little room for text.");
            }

            if (height - top - bottom < MinBody)
            {
                errors.Add("The top and bottom margins leave too little room for text.");
            }
        }

        if (errors.Count > 0)
        {
            ErrorText.Text = string.Join(Environment.NewLine, errors);
            ErrorText.Visibility = Visibility.Visible;
            return;
        }

        bool landscape = LandscapeRadio.IsChecked == true;
        if ((landscape && width < height) || (!landscape && width > height))
        {
            (width, height) = (height, width);
        }

        Result = new SectionProperties
        {
            PageWidth = width,
            PageHeight = height,
            Orientation = landscape ? Orientation.Landscape : Orientation.Portrait,
            MarginTop = top,
            MarginBottom = bottom,
            MarginLeft = left,
            MarginRight = right,
            Gutter = gutter,
            HeaderDistance = header,
            FooterDistance = footer,
            TitlePage = DifferentFirstPage.IsChecked == true,
        };
        DialogResult = true;
    }

    private string UnitText => _unit == LengthUnit.Centimeters ? "cm" : "in";

    private Twips Read(TextBox box, string name, List<string> errors)
    {
        if (Twips.TryParse(box.Text, _unit, out Twips value))
        {
            return value;
        }

        errors.Add($"{name}: '{box.Text}' is not a length (try 1.25 or 3 cm).");
        return Twips.Zero;
    }
}
