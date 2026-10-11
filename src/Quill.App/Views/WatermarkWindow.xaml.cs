using System.Windows;
using Quill.Core.Model;
using Quill.Core.Units;

namespace Quill.App.Views;

/// <summary>Text and colour of a custom watermark.</summary>
public partial class WatermarkWindow : Window
{
    private static readonly (string Name, string Hex)[] Colors =
    [
        ("Silver", "C0C0C0"), ("Gray", "808080"), ("Light Blue", "9DC3E6"), ("Light Red", "F4B183"), ("Red", "FF0000"), ("Blue", "0070C0"), ("Green", "70AD47"), ("Black", "000000"),
    ];

    public WatermarkWindow(Watermark? current)
    {
        InitializeComponent();
        foreach ((string name, _) in Colors)
        {
            ColorBox.Items.Add(name);
        }

        TextBox.Text = current?.Text ?? "DRAFT";
        int index = current is null ? 0 : Array.FindIndex(Colors, c => c.Hex.Equals(current.EffectiveColor.ToHex(), StringComparison.OrdinalIgnoreCase));
        ColorBox.SelectedIndex = index < 0 ? 0 : index;
        Loaded += (_, _) =>
        {
            TextBox.Focus();
            TextBox.SelectAll();
        };
    }

    /// <summary>The watermark to apply, or null to remove it (empty text).</summary>
    public Watermark? Result { get; private set; }

    private void OnOk(object sender, RoutedEventArgs e)
    {
        string text = TextBox.Text.Trim();
        Result = text.Length == 0 ? null : new Watermark(text, DocColor.Parse(Colors[Math.Max(0, ColorBox.SelectedIndex)].Hex));
        DialogResult = true;
    }
}
