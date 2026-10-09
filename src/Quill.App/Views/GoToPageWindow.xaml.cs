using System.Globalization;
using System.Windows;

namespace Quill.App.Views;

/// <summary>Ctrl+G: jump to a page number.</summary>
public partial class GoToPageWindow : Window
{
    private readonly int _pageCount;

    public GoToPageWindow(int currentPage, int pageCount)
    {
        InitializeComponent();
        _pageCount = pageCount;
        Prompt.Text = string.Format(CultureInfo.CurrentCulture, "Enter a page number (1 to {0}):", pageCount);
        PageBox.Text = currentPage.ToString(CultureInfo.CurrentCulture);
        Loaded += (_, _) =>
        {
            PageBox.Focus();
            PageBox.SelectAll();
        };
    }

    /// <summary>The 1-based page chosen, set when the dialog returns true.</summary>
    public int Page { get; private set; }

    private void OnGo(object sender, RoutedEventArgs e)
    {
        if (int.TryParse(PageBox.Text, NumberStyles.Integer, CultureInfo.CurrentCulture, out int page) && page >= 1 && page <= _pageCount)
        {
            Page = page;
            DialogResult = true;
            return;
        }

        ErrorText.Text = string.Format(CultureInfo.CurrentCulture, "Please enter a number between 1 and {0}.", _pageCount);
        ErrorText.Visibility = Visibility.Visible;
    }
}
