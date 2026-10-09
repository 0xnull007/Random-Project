using System.Globalization;
using System.Windows;

namespace Quill.App.Views;

/// <summary>Inserts the current date or time in one of several formats, as plain text.</summary>
public partial class DateTimeWindow : Window
{
    private static readonly string[] Patterns = ["d", "D", "MMMM d, yyyy", "d MMMM yyyy", "yyyy-MM-dd", "dd.MM.yyyy", "MM/dd/yyyy", "dddd", "MMMM yyyy", "t", "T", "HH:mm", "f", "F", "g", "G"];

    public DateTimeWindow()
    {
        InitializeComponent();
        DateTime now = DateTime.Now;
        CultureInfo culture = CultureInfo.CurrentCulture;
        foreach (string pattern in Patterns.Distinct())
        {
            Formats.Items.Add(now.ToString(pattern, culture));
        }

        Formats.SelectedIndex = 0;
        Loaded += (_, _) => Formats.Focus();
    }

    /// <summary>The formatted text chosen, set when the dialog returns true.</summary>
    public string Text { get; private set; } = string.Empty;

    private void OnOk(object sender, RoutedEventArgs e)
    {
        if (Formats.SelectedItem is string text)
        {
            Text = text;
            DialogResult = true;
        }
    }
}
