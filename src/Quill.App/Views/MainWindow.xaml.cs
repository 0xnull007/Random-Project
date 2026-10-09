using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using Quill.App.ViewModels;
using Quill.Core.Editing;
using Quill.Core.Model;
using Quill.Core.Text;

namespace Quill.App.Views;

public partial class MainWindow : Window
{
    public static readonly RoutedCommand FindNextCommand = new("FindNext", typeof(MainWindow));
    public static readonly RoutedCommand FindPreviousCommand = new("FindPrevious", typeof(MainWindow));

    private FindReplaceWindow? _findWindow;

    public MainWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Closing += OnClosing;
    }

    private MainViewModel ViewModel => (MainViewModel)DataContext;

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        Editor.Session = ViewModel.Session;
        Editor.ViewStateChanged += (_, _) =>
        {
            ViewModel.PageCount = Editor.PageCount;
            ViewModel.CurrentPage = Editor.CurrentPage;
        };
        ViewModel.PageCount = Editor.PageCount;
        PopupThemeFix.AttachAll(this);
        Dispatcher.BeginInvoke(() => PopupThemeFix.AttachAll(this), System.Windows.Threading.DispatcherPriority.Loaded);
        if (Application.Current is App { StartupFile: { } startupFile })
        {
            ViewModel.OpenFile(startupFile);
        }

        Editor.Focus();
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        e.Cancel = !ViewModel.ConfirmDiscard();
        if (!e.Cancel)
        {
            _findWindow?.ForceClose();
        }
    }

    private void OnExit(object sender, RoutedEventArgs e) => Close();

    private void OnCut(object sender, RoutedEventArgs e) => Editor.CutSelection();

    private void OnCopy(object sender, RoutedEventArgs e) => Editor.CopySelection();

    private void OnPaste(object sender, RoutedEventArgs e) => Editor.PasteFromClipboard();

    private void OnSelectAll(object sender, RoutedEventArgs e) => ViewModel.Session.SelectAll();

    private void OnEditHeader(object sender, RoutedEventArgs e)
    {
        Editor.EditHeader();
        Editor.Focus();
    }

    private void OnEditFooter(object sender, RoutedEventArgs e)
    {
        Editor.EditFooter();
        Editor.Focus();
    }

    private void OnCloseHeaderFooter(object sender, RoutedEventArgs e)
    {
        Editor.ExitHeaderFooter();
        Editor.Focus();
    }

    private void OnInsertPageNumber(object sender, RoutedEventArgs e)
    {
        Editor.InsertField(Field.Page());
        Editor.Focus();
    }

    private void OnInsertPageCount(object sender, RoutedEventArgs e)
    {
        Editor.InsertField(Field.NumPages());
        Editor.Focus();
    }

    private FindReplaceWindow FindWindow => _findWindow ??= new FindReplaceWindow(ViewModel.Session) { Owner = this };

    /// <summary>Short single-paragraph selections pre-fill the Find box, like Word.</summary>
    private string? SelectedTextForSearch()
    {
        Selection selection = ViewModel.Session.Selection;
        if (selection.IsCollapsed || !selection.Range.IsWithinOneParagraph)
        {
            return null;
        }

        string text = DocumentEditor.ExtractFragment(ViewModel.Session.Document, selection.Range).ToPlainText();
        return text.Length is > 0 and <= 100 && !text.Contains('\n', StringComparison.Ordinal) ? text : null;
    }

    private void OnFind(object sender, ExecutedRoutedEventArgs e) => FindWindow.ShowFind(SelectedTextForSearch());

    private void OnReplace(object sender, ExecutedRoutedEventArgs e) => FindWindow.ShowReplace(SelectedTextForSearch());

    private void OnFindNext(object sender, ExecutedRoutedEventArgs e) => FindWindow.FindNext(backwards: false);

    private void OnFindPrevious(object sender, ExecutedRoutedEventArgs e) => FindWindow.FindNext(backwards: true);

    private void OnPrintPreview(object sender, RoutedEventArgs e)
    {
        var preview = new PrintPreviewWindow(ViewModel.Session.Document, ViewModel.DocumentName) { Owner = this };
        preview.ShowDialog();
        Editor.Focus();
    }

    private void OnPageSetup(object sender, RoutedEventArgs e)
    {
        EditingSession session = ViewModel.Session;
        int sectionIndex = session.Selection.Story.SectionIndex;
        var dialog = new PageSetupWindow(
            session.Document.Sections[sectionIndex].Properties,
            session.Document.Settings.EvenAndOddHeaders,
            session.Document.Sections.Count > 1)
        {
            Owner = this,
        };
        if (dialog.ShowDialog() == true && dialog.Result is { } result)
        {
            ViewModel.ApplyPageSetup(result, dialog.ApplyToWholeDocument, dialog.EvenAndOddHeaders);
        }

        Editor.Focus();
    }
}
