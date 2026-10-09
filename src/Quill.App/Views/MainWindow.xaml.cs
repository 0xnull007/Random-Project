using System.ComponentModel;
using System.Windows;
using Quill.App.ViewModels;

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
}
