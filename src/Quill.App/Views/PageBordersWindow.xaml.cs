using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using Quill.Core.Model;
using Quill.Core.Units;

namespace Quill.App.Views;

/// <summary>Word's Page Borders dialog, reduced to a box border: style, width, colour, distance and scope.</summary>
public partial class PageBordersWindow : Window
{
    private static readonly (string Name, int Eighths)[] Widths =
    [
        ("¼ pt", 2), ("½ pt", 4), ("¾ pt", 6), ("1 pt", 8), ("1½ pt", 12), ("2¼ pt", 18), ("3 pt", 24), ("4½ pt", 36), ("6 pt", 48),
    ];

    private static readonly (string Name, string Hex)[] Colors =
    [
        ("Black", "000000"), ("Dark Gray", "595959"), ("Gray", "A6A6A6"), ("Dark Red", "C00000"), ("Red", "FF0000"),
        ("Orange", "ED7D31"), ("Gold", "FFC000"), ("Green", "70AD47"), ("Dark Green", "375623"), ("Blue", "0070C0"),
        ("Dark Blue", "1F3864"), ("Purple", "7030A0"),
    ];

    public PageBordersWindow(PageBorders? current, bool hasSeveralSections)
    {
        InitializeComponent();
        foreach ((string name, _) in Widths)
        {
            WidthBox.Items.Add(name);
        }

        foreach ((string name, _) in Colors)
        {
            ColorBox.Items.Add(name);
        }

        PageBorders shown = current ?? new PageBorders();
        SettingBox.SelectedIndex = current is null ? 0 : 1;
        StyleBox.SelectedIndex = (int)shown.Style;
        WidthBox.SelectedIndex = Math.Max(0, Array.FindIndex(Widths, w => w.Eighths >= shown.Width));
        int colorIndex = Array.FindIndex(Colors, c => c.Hex.Equals(shown.Color.ToHex(), StringComparison.OrdinalIgnoreCase));
        ColorBox.SelectedIndex = colorIndex < 0 ? 0 : colorIndex;
        SpaceBox.Text = shown.Space.ToString(CultureInfo.CurrentCulture);
        FromBox.SelectedIndex = shown.FromText ? 1 : 0;
        ApplyBox.SelectedIndex = 0;
        ApplyBox.IsEnabled = hasSeveralSections;
        UpdateEnabled();
    }

    /// <summary>The chosen border, or null for none.</summary>
    public PageBorders? Result { get; private set; }

    public bool WholeDocument { get; private set; } = true;

    private void OnSettingChanged(object sender, SelectionChangedEventArgs e) => UpdateEnabled();

    private void UpdateEnabled()
    {
        bool on = SettingBox.SelectedIndex == 1;
        foreach (Control control in new Control[] { StyleBox, WidthBox, ColorBox, SpaceBox, FromBox })
        {
            control.IsEnabled = on;
        }
    }

    private void OnOk(object sender, RoutedEventArgs e)
    {
        WholeDocument = ApplyBox.SelectedIndex == 0;
        if (SettingBox.SelectedIndex == 0)
        {
            Result = null;
            DialogResult = true;
            return;
        }

        if (!int.TryParse(SpaceBox.Text.Trim(), NumberStyles.Integer, CultureInfo.CurrentCulture, out int space) || space is < 0 or > 31)
        {
            ErrorText.Text = "Please enter a distance between 0 and 31 points.";
            ErrorText.Visibility = Visibility.Visible;
            return;
        }

        Result = new PageBorders
        {
            Style = (PageBorderStyle)Math.Max(0, StyleBox.SelectedIndex),
            Width = Widths[Math.Max(0, WidthBox.SelectedIndex)].Eighths,
            Color = DocColor.Parse(Colors[Math.Max(0, ColorBox.SelectedIndex)].Hex),
            Space = space,
            FromText = FromBox.SelectedIndex == 1,
        };
        DialogResult = true;
    }
}
