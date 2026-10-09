using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using Quill.App.Imaging;
using Quill.App.ViewModels;
using Quill.Core.Editing;
using Quill.Core.Model;
using Quill.Core.Text;

namespace Quill.App.Views;

public partial class MainWindow : Window
{
    public static readonly RoutedCommand FindNextCommand = new("FindNext", typeof(MainWindow));
    public static readonly RoutedCommand FindPreviousCommand = new("FindPrevious", typeof(MainWindow));
    public static readonly RoutedCommand GoToPageCommand = new("GoToPage", typeof(MainWindow));
    public static readonly RoutedCommand WordCountCommand = new("WordCount", typeof(MainWindow));

    private FindReplaceWindow? _findWindow;

    public MainWindow()
    {
        InitializeComponent();
        ViewModel.RestoreWindowState(this);
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
        else
        {
            Dispatcher.BeginInvoke(ViewModel.OfferRecovery, System.Windows.Threading.DispatcherPriority.Background);
        }

        Editor.Focus();
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = DroppedFile(e) is not null ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    /// <summary>Dropping a .docx opens it; dropping a picture inserts it at the caret.</summary>
    private void OnDrop(object sender, DragEventArgs e)
    {
        if (DroppedFile(e) is { } path)
        {
            if (ImageFiles.IsPictureFile(path))
            {
                InsertPictureFile(path);
            }
            else if (ViewModel.ConfirmDiscard())
            {
                ViewModel.OpenFile(path);
            }

            Editor.Focus();
        }

        e.Handled = true;
    }

    private static string? DroppedFile(DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] files)
        {
            return files.FirstOrDefault(f => f.EndsWith(".docx", StringComparison.OrdinalIgnoreCase) || ImageFiles.IsPictureFile(f));
        }

        return null;
    }

    private void OnInsertPicture(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = ImageFiles.Filter, Title = "Insert Picture" };
        if (dialog.ShowDialog(this) == true)
        {
            InsertPictureFile(dialog.FileName);
        }

        Editor.Focus();
    }

    private void InsertPictureFile(string path)
    {
        try
        {
            Editor.InsertImage(ImageFiles.Load(path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or FileFormatException or ArgumentException or InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            MessageBox.Show(this, "Could not insert the picture." + Environment.NewLine + Environment.NewLine + ex.Message, "Quill", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnPictureSize(object sender, RoutedEventArgs e)
    {
        EditingSession session = ViewModel.Session;
        if (session.SelectedImage() is not (_, { } image))
        {
            MessageBox.Show(this, "Click a picture first, then choose Picture Size.", "Quill", MessageBoxButton.OK, MessageBoxImage.Information);
            Editor.Focus();
            return;
        }

        var dialog = new PictureSizeWindow(image, session.Document.Images.Get(image.ImageId)) { Owner = this };
        if (dialog.ShowDialog() == true)
        {
            session.ResizeImage(dialog.ResultWidth, dialog.ResultHeight);
        }

        Editor.Focus();
    }


    private void OnClosing(object? sender, CancelEventArgs e)
    {
        e.Cancel = !ViewModel.ConfirmDiscard();
        if (!e.Cancel)
        {
            ViewModel.SaveWindowState(this);
            _findWindow?.ForceClose();
        }
    }

    private void OnExit(object sender, RoutedEventArgs e) => Close();

    private void OnCut(object sender, RoutedEventArgs e) => Editor.CutSelection();

    private void OnCopy(object sender, RoutedEventArgs e) => Editor.CopySelection();

    private void OnPaste(object sender, RoutedEventArgs e) => Editor.PasteFromClipboard();

    private void OnSelectAll(object sender, RoutedEventArgs e) => ViewModel.Session.SelectAll();

    private void OnFontColorSelected(object? sender, string value)
    {
        ViewModel.SetFontColor(value);
        Editor.Focus();
    }

    private void OnHighlightSelected(object? sender, string value)
    {
        ViewModel.SetHighlight(value);
        Editor.Focus();
    }

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

    private void OnFitPage(object sender, RoutedEventArgs e)
    {
        Editor.ZoomToFitPage();
        Editor.Focus();
    }

    private void OnFitWidth(object sender, RoutedEventArgs e)
    {
        Editor.ZoomToFitWidth();
        Editor.Focus();
    }

    private void OnGoToPageCommand(object sender, ExecutedRoutedEventArgs e) => OnGoToPage(sender, e);

    private void OnGoToPage(object sender, RoutedEventArgs e)
    {
        var dialog = new GoToPageWindow(Editor.CurrentPage, Math.Max(1, Editor.PageCount)) { Owner = this };
        if (dialog.ShowDialog() == true)
        {
            Editor.GoToPage(dialog.Page);
        }

        Editor.Focus();
    }

    private void OnWordCountCommand(object sender, ExecutedRoutedEventArgs e) => OnWordCount(sender, e);

    private void OnWordCount(object sender, RoutedEventArgs e)
    {
        EditingSession session = ViewModel.Session;
        bool forSelection = !session.Selection.IsCollapsed;
        DocumentStatistics statistics = forSelection
            ? DocumentStatistics.Compute(DocumentEditor.ExtractFragment(session.Document, session.Selection.Range).Paragraphs)
            : DocumentStatistics.Compute(session.Document);
        int lines = forSelection ? Editor.SelectionLineCount() : Editor.BodyLineCount;
        int pages = forSelection ? Editor.SelectionPageCount() : Editor.PageCount;
        new WordCountWindow(statistics, pages, lines, forSelection) { Owner = this }.ShowDialog();
        Editor.Focus();
    }

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
