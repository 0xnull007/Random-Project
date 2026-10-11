using System.Globalization;
using System.Windows;
using Quill.Core.Model;

namespace Quill.App.Views;

/// <summary>Inserts the current date or time in one of several formats, as plain text or as a self-updating field.</summary>
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

    /// <summary>True to insert a field that updates itself instead of plain text.</summary>
    public bool AsField => UpdateBox.IsChecked == true;

    /// <summary>Word's date picture for the chosen format (for the DATE/TIME field instruction).</summary>
    public string WordPicture { get; private set; } = string.Empty;

    /// <summary>True when the chosen format shows a time of day only.</summary>
    public bool IsTime { get; private set; }

    private void OnOk(object sender, RoutedEventArgs e)
    {
        if (Formats.SelectedItem is string text && Formats.SelectedIndex >= 0 && Formats.SelectedIndex < Patterns.Length)
        {
            Text = text;
            string pattern = Patterns[Formats.SelectedIndex];
            WordPicture = FieldSupport.ToWordPicture(pattern, CultureInfo.CurrentCulture);
            IsTime = pattern is "t" or "T" or "HH:mm";
            DialogResult = true;
        }
    }
}
