using System.Windows;

namespace Quill.App.Views;

/// <summary>Picture Format > Alt Text: the description stored with a picture.</summary>
public partial class AltTextWindow : Window
{
    public AltTextWindow(string? current)
    {
        InitializeComponent();
        TextBox.Text = current ?? string.Empty;
        Loaded += (_, _) =>
        {
            TextBox.Focus();
            TextBox.SelectAll();
        };
    }

    /// <summary>The entered description; empty means none.</summary>
    public string Result => TextBox.Text.Trim();

    private void OnOk(object sender, RoutedEventArgs e) => DialogResult = true;
}
