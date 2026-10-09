using System.ComponentModel;
using System.Windows;
using Quill.App.ViewModels;
using Quill.Core.Editing;
using Quill.Core.Model;

namespace Quill.App.Views;

public partial class MainWindow : Window
{
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

    private void OnClosing(object? sender, CancelEventArgs e) => e.Cancel = !ViewModel.ConfirmDiscard();

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
