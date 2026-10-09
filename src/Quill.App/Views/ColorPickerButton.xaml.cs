using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Quill.Core.Model;
using Quill.Layout.Wpf;

namespace Quill.App.Views;

/// <summary>A command-bar button with a color swatch flyout: Word-style theme and standard colors, or the 16 highlight colors.</summary>
public partial class ColorPickerButton : UserControl
{
    public static readonly DependencyProperty IsHighlightProperty =
        DependencyProperty.Register(nameof(IsHighlight), typeof(bool), typeof(ColorPickerButton), new PropertyMetadata(false, (d, _) => ((ColorPickerButton)d).BuildPalette()));

    public static readonly DependencyProperty CurrentColorProperty =
        DependencyProperty.Register(nameof(CurrentColor), typeof(Color), typeof(ColorPickerButton), new PropertyMetadata(Colors.Black, (d, e) => ((ColorPickerButton)d).Indicator.Background = new SolidColorBrush((Color)e.NewValue)));

    private static readonly string[] ThemeBases = ["FFFFFF", "000000", "E7E6E6", "44546A", "4472C4", "ED7D31", "A5A5A5", "FFC000", "5B9BD5", "70AD47"];
    private static readonly string[] StandardColors = ["C00000", "FF0000", "FFC000", "FFFF00", "92D050", "00B050", "00B0F0", "0070C0", "002060", "7030A0"];

    public ColorPickerButton()
    {
        InitializeComponent();
        BuildPalette();
    }

    /// <summary>Raised with a value for the view model: "auto", an RRGGBB hex, or a <see cref="HighlightColor"/> name.</summary>
    public event EventHandler<string>? ColorSelected;

    public bool IsHighlight
    {
        get => (bool)GetValue(IsHighlightProperty);
        set => SetValue(IsHighlightProperty, value);
    }

    /// <summary>The color shown in the indicator bar under the glyph.</summary>
    public Color CurrentColor
    {
        get => (Color)GetValue(CurrentColorProperty);
        set => SetValue(CurrentColorProperty, value);
    }

    public string GlyphText
    {
        get => Glyph.Text;
        set => Glyph.Text = value;
    }

    public string? ToggleToolTip
    {
        get => Toggle.ToolTip as string;
        set => Toggle.ToolTip = value;
    }

    private void BuildPalette()
    {
        if (Palette is null)
        {
            return;
        }

        Palette.Children.Clear();
        if (IsHighlight)
        {
            var grid = new UniformGrid { Columns = 5 };
            foreach (HighlightColor highlight in Enum.GetValues<HighlightColor>())
            {
                if (highlight == HighlightColor.None)
                {
                    continue;
                }

                Color color = FontCatalog.Shared.GetHighlightBrush(highlight)!.Color;
                grid.Children.Add(Swatch(color, highlight.ToString(), highlight.ToString()));
            }

            Palette.Children.Add(grid);
            Palette.Children.Add(TextButton("No Color", HighlightColor.None.ToString()));
            return;
        }

        Palette.Children.Add(TextButton("Automatic", "auto"));
        Palette.Children.Add(Label("Theme Colors"));
        var theme = new UniformGrid { Columns = ThemeBases.Length };
        foreach (string hex in ThemeBases)
        {
            theme.Children.Add(Swatch(Parse(hex), hex, hex));
        }

        foreach (double factor in new[] { 0.8, 0.6, 0.4, -0.25, -0.5 })
        {
            foreach (string hex in ThemeBases)
            {
                Color shade = Shade(Parse(hex), factor);
                theme.Children.Add(Swatch(shade, Hex(shade), Hex(shade)));
            }
        }

        Palette.Children.Add(theme);
        Palette.Children.Add(Label("Standard Colors"));
        var standard = new UniformGrid { Columns = StandardColors.Length };
        foreach (string hex in StandardColors)
        {
            standard.Children.Add(Swatch(Parse(hex), hex, hex));
        }

        Palette.Children.Add(standard);
    }

    private Button Swatch(Color color, string value, string tooltip)
    {
        var button = new Button
        {
            Width = 22,
            Height = 22,
            Margin = new Thickness(1),
            Padding = new Thickness(0),
            Background = new SolidColorBrush(color),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x60, 0x80, 0x80, 0x80)),
            BorderThickness = new Thickness(1),
            ToolTip = tooltip,
        };
        button.Click += (_, _) => Select(value);
        return button;
    }

    private Button TextButton(string text, string value)
    {
        var button = new Button { Content = text, Margin = new Thickness(0, 2, 0, 2), HorizontalAlignment = HorizontalAlignment.Stretch, Padding = new Thickness(8, 4, 8, 4) };
        button.Click += (_, _) => Select(value);
        return button;
    }

    private static TextBlock Label(string text) => new() { Text = text, FontSize = 11, Opacity = 0.7, Margin = new Thickness(0, 6, 0, 2) };

    private void Select(string value)
    {
        Toggle.IsChecked = false;
        ColorSelected?.Invoke(this, value);
    }

    private static Color Parse(string hex) => Color.FromRgb(Convert.ToByte(hex[..2], 16), Convert.ToByte(hex[2..4], 16), Convert.ToByte(hex[4..6], 16));

    private static string Hex(Color c) => $"{c.R:X2}{c.G:X2}{c.B:X2}";

    /// <summary>Positive factors blend toward white (tints), negative toward black (shades), like Word's palette columns.</summary>
    private static Color Shade(Color c, double factor)
    {
        byte Blend(byte channel) => factor >= 0
            ? (byte)Math.Round(channel + (255 - channel) * factor)
            : (byte)Math.Round(channel * (1 + factor));
        return Color.FromRgb(Blend(c.R), Blend(c.G), Blend(c.B));
    }
}
