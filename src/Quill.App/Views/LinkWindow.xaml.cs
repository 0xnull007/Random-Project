using System.Windows;

namespace Quill.App.Views;

/// <summary>Ctrl+K: insert a hyperlink, or edit / remove the one under the caret.</summary>
public partial class LinkWindow : Window
{
    public LinkWindow(string text, string? url)
    {
        InitializeComponent();
        TextBox.Text = text;
        AddressBox.Text = url ?? string.Empty;
        if (url is not null)
        {
            Title = "Edit Link";
            RemoveButton.Visibility = Visibility.Visible;
        }

        Loaded += (_, _) =>
        {
            System.Windows.Controls.TextBox focus = text.Length > 0 ? AddressBox : TextBox;
            focus.Focus();
            focus.SelectAll();
        };
    }

    public string Text { get; private set; } = string.Empty;

    public string Url { get; private set; } = string.Empty;

    /// <summary>True when the user chose Remove Link.</summary>
    public bool RemoveRequested { get; private set; }

    private void OnOk(object sender, RoutedEventArgs e)
    {
        string address = AddressBox.Text.Trim();
        if (address.Length == 0 || address.Contains(' ', StringComparison.Ordinal))
        {
            ErrorText.Text = "Please enter an address such as https://example.com or mailto:someone@example.com.";
            ErrorText.Visibility = Visibility.Visible;
            return;
        }

        if (!address.Contains("://", StringComparison.Ordinal) && !address.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase) && !address.StartsWith('#'))
        {
            address = address.Contains('@', StringComparison.Ordinal) ? "mailto:" + address : "https://" + address;
        }

        Text = TextBox.Text.Trim();
        Url = address;
        DialogResult = true;
    }

    private void OnRemove(object sender, RoutedEventArgs e)
    {
        RemoveRequested = true;
        DialogResult = true;
    }
}
