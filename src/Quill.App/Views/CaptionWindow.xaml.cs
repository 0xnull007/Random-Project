using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace Quill.App.Views;

/// <summary>Label, position and text of a new caption, like Word's Insert Caption dialog.</summary>
public partial class CaptionWindow : Window
{
    private readonly Func<string, int> _nextNumber;

    public CaptionWindow(string defaultLabel, Func<string, int> nextNumber)
    {
        ArgumentNullException.ThrowIfNull(nextNumber);
        InitializeComponent();
        _nextNumber = nextNumber;
        foreach (string label in new[] { "Figure", "Table", "Equation" })
        {
            LabelBox.Items.Add(label);
        }

        LabelBox.Text = defaultLabel;
        if (LabelBox.Items.Contains(defaultLabel))
        {
            LabelBox.SelectedItem = defaultLabel;
        }

        PositionBox.SelectedIndex = string.Equals(defaultLabel, "Table", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        LabelBox.AddHandler(TextBoxBase.TextChangedEvent, new TextChangedEventHandler((_, _) => UpdatePreview()));
        UpdatePreview();
        Loaded += (_, _) =>
        {
            TextBox.Focus();
        };
    }

    public string Label { get; private set; } = "Figure";

    public string? CaptionText { get; private set; }

    public bool Above { get; private set; }

    private void OnLabelChanged(object sender, SelectionChangedEventArgs e)
    {
        if (LabelBox.SelectedItem is string selected)
        {
            PositionBox.SelectedIndex = string.Equals(selected, "Table", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        }

        UpdatePreview();
    }

    private void UpdatePreview()
    {
        string label = (LabelBox.SelectedItem as string ?? LabelBox.Text).Trim();
        PreviewText.Text = label.Length == 0 ? string.Empty : $"Preview: {label} {_nextNumber(label)}";
    }

    private void OnOk(object sender, RoutedEventArgs e)
    {
        string label = (LabelBox.SelectedItem as string ?? LabelBox.Text).Trim();
        if (label.Length == 0)
        {
            LabelBox.Focus();
            return;
        }

        Label = label;
        CaptionText = string.IsNullOrWhiteSpace(TextBox.Text) ? null : TextBox.Text.Trim();
        Above = PositionBox.SelectedIndex == 1;
        DialogResult = true;
    }
}
