using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Quill.Core.Editing;
using Quill.Core.Text;
using TextSearch = Quill.Core.Editing.TextSearch;

namespace Quill.App.Views;

/// <summary>Non-modal Find and Replace. One instance lives for the main window's lifetime and hides instead of closing.</summary>
public partial class FindReplaceWindow : Window
{
    private readonly EditingSession _session;
    private bool _allowClose;

    public FindReplaceWindow(EditingSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        InitializeComponent();
        _session = session;
        Loaded += (_, _) => PopupThemeFix.AttachAll(this);
        Closing += (_, e) =>
        {
            if (!_allowClose)
            {
                e.Cancel = true;
                Hide();
            }
        };
    }

    private SearchOptions Options => new(MatchCaseBox.IsChecked == true, WholeWordBox.IsChecked == true);

    public void ShowFind(string? prefill)
    {
        Prefill(prefill);
        ShowAndFocus(FindBox);
    }

    public void ShowReplace(string? prefill)
    {
        Prefill(prefill);
        ShowAndFocus(string.IsNullOrEmpty(FindBox.Text) ? FindBox : ReplaceBox);
    }

    /// <summary>Closes for real (used when the owner window closes).</summary>
    public void ForceClose()
    {
        _allowClose = true;
        Close();
    }

    /// <summary>Selects the next (or previous) match, wrapping around; works even while the window is hidden.</summary>
    public void FindNext(bool backwards)
    {
        string query = FindBox.Text;
        if (string.IsNullOrEmpty(query))
        {
            ShowFind(null);
            SetStatus("Type the text to find.");
            return;
        }

        Selection selection = _session.Selection;
        TextPosition from = backwards ? selection.Start : selection.End;
        TextRange? match = TextSearch.FindNext(_session.Document, from, query, Options, backwards, out bool wrapped);
        if (match is null)
        {
            SetStatus(string.Format(CultureInfo.CurrentCulture, "Cannot find \"{0}\".", query));
            return;
        }

        _session.SetSelection(new Selection(match.Value.Start, match.Value.End));
        IReadOnlyList<TextRange> all = TextSearch.FindAll(_session.Document, query, Options);
        int index = TextSearch.IndexOf(all, match.Value);
        string position = string.Format(CultureInfo.CurrentCulture, "{0} of {1}", index + 1, all.Count);
        SetStatus(wrapped ? position + " (wrapped around)" : position);
    }

    private void Prefill(string? text)
    {
        if (!string.IsNullOrEmpty(text))
        {
            FindBox.Text = text;
        }
    }

    private void ShowAndFocus(TextBox box)
    {
        if (!IsVisible)
        {
            PositionNearOwner();
            Show();
        }

        Activate();
        box.Focus();
        box.SelectAll();
    }

    private void PositionNearOwner()
    {
        if (Owner is null)
        {
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            return;
        }

        double left = Owner.Left + Owner.ActualWidth - Width - 48;
        double top = Owner.Top + 140;
        Left = Math.Max(SystemParameters.VirtualScreenLeft, Math.Min(left, SystemParameters.VirtualScreenWidth - Width));
        Top = Math.Max(SystemParameters.VirtualScreenTop, top);
    }

    private void SetStatus(string text) => StatusText.Text = text;

    private static string SingleLine(string text) =>
        text.Replace("\r\n", " ", StringComparison.Ordinal).Replace('\r', ' ').Replace('\n', ' ');

    /// <summary>IsCancel only closes modal windows, so the non-modal Find window hides itself explicitly (Esc triggers this too).</summary>
    private void OnClose(object sender, RoutedEventArgs e)
    {
        Hide();
        Owner?.Activate();
    }

    private void OnFindNext(object sender, RoutedEventArgs e) => FindNext(backwards: false);

    private void OnFindPrevious(object sender, RoutedEventArgs e) => FindNext(backwards: true);

    private void OnReplace(object sender, RoutedEventArgs e)
    {
        string query = FindBox.Text;
        if (string.IsNullOrEmpty(query))
        {
            SetStatus("Type the text to find.");
            return;
        }

        Selection selection = _session.Selection;
        if (!selection.IsCollapsed && selection.Range.IsWithinOneParagraph)
        {
            string selected = DocumentEditor.ExtractFragment(_session.Document, selection.Range).ToPlainText();
            if (string.Equals(selected, query, Options.Comparison))
            {
                _session.ReplaceRange(selection.Range, SingleLine(ReplaceBox.Text));
            }
        }

        FindNext(backwards: false);
    }

    private void OnReplaceAll(object sender, RoutedEventArgs e)
    {
        string query = FindBox.Text;
        if (string.IsNullOrEmpty(query))
        {
            SetStatus("Type the text to find.");
            return;
        }

        int count = _session.ReplaceAll(query, SingleLine(ReplaceBox.Text), Options);
        SetStatus(count == 0
            ? string.Format(CultureInfo.CurrentCulture, "Cannot find \"{0}\".", query)
            : string.Format(CultureInfo.CurrentCulture, count == 1 ? "Replaced {0} occurrence." : "Replaced {0} occurrences.", count));
    }

    private void OnFindBoxKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            FindNext(backwards: Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));
            e.Handled = true;
        }
    }

    private void OnReplaceBoxKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            OnReplace(sender, e);
            e.Handled = true;
        }
    }
}
