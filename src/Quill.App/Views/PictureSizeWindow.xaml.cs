using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using Quill.Core.Model;
using Quill.Core.Units;
using Quill.Layout.Wpf;

namespace Quill.App.Views;

/// <summary>Width and height of the selected picture, with the aspect ratio locked by default.</summary>
public partial class PictureSizeWindow : Window
{
    private const int MinTwips = 15;
    private const int MaxTwips = 22 * 1440;

    private readonly LengthUnit _unit;
    private readonly string _unitName;
    private readonly double _aspect;
    private readonly (Twips Width, Twips Height)? _original;
    private bool _syncing;

    public PictureSizeWindow(InlineImage image, ImageData? data)
    {
        ArgumentNullException.ThrowIfNull(image);
        InitializeComponent();
        _unit = RegionInfo.CurrentRegion.IsMetric ? LengthUnit.Centimeters : LengthUnit.Inches;
        _unitName = _unit == LengthUnit.Centimeters ? "cm" : "in";
        _aspect = image.Height.Value > 0 ? image.Width.Value / (double)image.Height.Value : 1;
        _original = data is null ? null : OriginalSize(data);
        WidthUnit.Text = _unitName;
        HeightUnit.Text = _unitName;
        SetBoxes(image.Width, image.Height);
        OriginalText.Text = _original is { } original
            ? "Original size: " + original.Width.Format(_unit) + " x " + original.Height.Format(_unit) + " " + _unitName
            : "Original size unknown.";
        OriginalButton.IsEnabled = _original is not null;
        Loaded += (_, _) =>
        {
            WidthBox.Focus();
            WidthBox.SelectAll();
        };
    }

    public Twips ResultWidth { get; private set; }

    public Twips ResultHeight { get; private set; }

    private static (Twips Width, Twips Height)? OriginalSize(ImageData data)
    {
        if (ImageCache.Get(data) is { } bitmap)
        {
            double dpiX = bitmap.DpiX > 1 ? bitmap.DpiX : 96;
            double dpiY = bitmap.DpiY > 1 ? bitmap.DpiY : 96;
            return (Twips.FromInches(bitmap.PixelWidth / dpiX), Twips.FromInches(bitmap.PixelHeight / dpiY));
        }

        if (data.PixelWidth > 0 && data.PixelHeight > 0)
        {
            return (Twips.FromInches(data.PixelWidth / 96.0), Twips.FromInches(data.PixelHeight / 96.0));
        }

        return null;
    }

    private void SetBoxes(Twips width, Twips height)
    {
        _syncing = true;
        WidthBox.Text = width.Format(_unit);
        HeightBox.Text = height.Format(_unit);
        _syncing = false;
    }

    private void OnWidthChanged(object sender, TextChangedEventArgs e)
    {
        if (_syncing || LockAspect.IsChecked != true || !Twips.TryParse(WidthBox.Text, _unit, out Twips width))
        {
            return;
        }

        _syncing = true;
        HeightBox.Text = new Twips((int)Math.Round(width.Value / _aspect)).Format(_unit);
        _syncing = false;
    }

    private void OnHeightChanged(object sender, TextChangedEventArgs e)
    {
        if (_syncing || LockAspect.IsChecked != true || !Twips.TryParse(HeightBox.Text, _unit, out Twips height))
        {
            return;
        }

        _syncing = true;
        WidthBox.Text = new Twips((int)Math.Round(height.Value * _aspect)).Format(_unit);
        _syncing = false;
    }

    private void OnOriginal(object sender, RoutedEventArgs e)
    {
        if (_original is { } original)
        {
            SetBoxes(original.Width, original.Height);
        }
    }

    private void OnOk(object sender, RoutedEventArgs e)
    {
        if (Twips.TryParse(WidthBox.Text, _unit, out Twips width) && Twips.TryParse(HeightBox.Text, _unit, out Twips height)
            && width.Value is >= MinTwips and <= MaxTwips && height.Value is >= MinTwips and <= MaxTwips)
        {
            ResultWidth = width;
            ResultHeight = height;
            DialogResult = true;
            return;
        }

        ErrorText.Text = "Please enter a width and a height between " + new Twips(MinTwips).Format(_unit) + " and " + new Twips(MaxTwips).Format(_unit) + " " + _unitName + ".";
        ErrorText.Visibility = Visibility.Visible;
    }
}
