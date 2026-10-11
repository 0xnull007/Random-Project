using System.Windows;
using System.Windows.Controls;
using Quill.Core.Editing;
using Quill.Core.Model;

namespace Quill.App.Views;

/// <summary>Word's Bookmark dialog: add a bookmark on the selection, delete one, or jump to one.</summary>
public partial class BookmarkWindow : Window
{
    private readonly EditingSession _session;

    public BookmarkWindow(EditingSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        InitializeComponent();
        _session = session;
        RefreshList();
        Loaded += (_, _) => NameBox.Focus();
    }

    /// <summary>True when Go To moved the selection, so the caller scrolls to it.</summary>
    public bool Navigated { get; private set; }

    private void RefreshList()
    {
        NameList.Items.Clear();
        foreach (string name in _session.BookmarkNames)
        {
            NameList.Items.Add(name);
        }

        UpdateButtons();
    }

    private void UpdateButtons()
    {
        string name = NameBox.Text.Trim();
        bool exists = NameList.Items.Cast<string>().Any(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase));
        AddButton.IsEnabled = BookmarkSupport.IsValidName(name);
        AddButton.Content = exists ? "Replace" : "Add";
        DeleteButton.IsEnabled = exists;
        GoToButton.IsEnabled = exists;
    }

    private void OnNameChanged(object sender, TextChangedEventArgs e)
    {
        ErrorText.Visibility = Visibility.Collapsed;
        UpdateButtons();
    }

    private void OnSelected(object sender, SelectionChangedEventArgs e)
    {
        if (NameList.SelectedItem is string name)
        {
            NameBox.Text = name;
        }
    }

    private void OnAdd(object sender, RoutedEventArgs e)
    {
        if (!_session.AddBookmark(NameBox.Text.Trim()))
        {
            ErrorText.Text = "Select some text first, and use a name that starts with a letter (letters, digits and underscores only).";
            ErrorText.Visibility = Visibility.Visible;
            return;
        }

        DialogResult = true;
    }

    private void OnDelete(object sender, RoutedEventArgs e)
    {
        _session.RemoveBookmark(NameBox.Text.Trim());
        RefreshList();
    }

    private void OnGoTo(object sender, RoutedEventArgs e)
    {
        if (_session.GoToBookmark(NameBox.Text.Trim()))
        {
            Navigated = true;
            DialogResult = true;
        }
    }
}
