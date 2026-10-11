using System.Windows;

namespace Quill.App.Views;

/// <summary>Name for a style created from the paragraph at the caret.</summary>
public partial class NewStyleWindow : Window
{
    private readonly Func<string, bool> _nameTaken;

    public NewStyleWindow(string basedOnName, Func<string, bool> nameTaken)
    {
        ArgumentNullException.ThrowIfNull(nameTaken);
        InitializeComponent();
        _nameTaken = nameTaken;
        HintText.Text = "Based on " + basedOnName + ", with the paragraph and character formatting at the caret; the paragraph switches to the new style.";
        Loaded += (_, _) => NameBox.Focus();
    }

    public string StyleName { get; private set; } = string.Empty;

    private void OnOk(object sender, RoutedEventArgs e)
    {
        string name = NameBox.Text.Trim();
        if (name.Length == 0)
        {
            ErrorText.Text = "Please enter a name.";
            ErrorText.Visibility = Visibility.Visible;
            return;
        }

        if (_nameTaken(name))
        {
            ErrorText.Text = "A style with this name already exists.";
            ErrorText.Visibility = Visibility.Visible;
            return;
        }

        StyleName = name;
        DialogResult = true;
    }
}
