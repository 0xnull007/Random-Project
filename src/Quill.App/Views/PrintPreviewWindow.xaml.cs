using System.Globalization;
using System.Windows;
using System.Windows.Input;
using Quill.App.Printing;
using Quill.Core.Editing;
using Quill.Core.Model;
using Quill.Core.Styles;

namespace Quill.App.Views;

/// <summary>Shows the document exactly as it prints, using the same layout and renderer as the editor.</summary>
public partial class PrintPreviewWindow : Window
{
    private readonly Document _document;
    private readonly StyleResolver _resolver;
    private readonly string _title;

    public PrintPreviewWindow(Document document, string title)
    {
        ArgumentNullException.ThrowIfNull(document);
        InitializeComponent();
        _document = document;
        _title = title;
        var session = new EditingSession(document);
        _resolver = session.Resolver;
        Loaded += (_, _) =>
        {
            Preview.Session = session;
            Preview.ViewStateChanged += (_, _) => UpdatePageInfo();
            Preview.ZoomToFitPage();
            UpdatePageInfo();
            PopupThemeFix.AttachAll(this);
            Preview.Focus();
        };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.P && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            {
                OnPrint(this, e);
                e.Handled = true;
            }
        };
    }

    private void UpdatePageInfo()
    {
        PageInfo.Text = string.Format(
            CultureInfo.CurrentCulture,
            "Page {0} of {1}   {2}%",
            Preview.CurrentPage,
            Preview.PageCount,
            Math.Round(Preview.Zoom * 100));
    }

    private void OnPrint(object sender, RoutedEventArgs e)
    {
        try
        {
            PrintService.Print(_document, _resolver, _title);
        }
        catch (Exception ex) when (ex is System.Printing.PrintSystemException or InvalidOperationException)
        {
            MessageBox.Show(this, "Printing failed." + Environment.NewLine + Environment.NewLine + ex.Message, "Quill", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnExportPdf(object sender, RoutedEventArgs e) => PdfExportCommand.Run(_document, _resolver, _title, this);

    private void OnFitPage(object sender, RoutedEventArgs e)
    {
        Preview.ZoomToFitPage();
        UpdatePageInfo();
    }

    private void OnFitWidth(object sender, RoutedEventArgs e)
    {
        Preview.ZoomToFitWidth();
        UpdatePageInfo();
    }

    private void OnZoom100(object sender, RoutedEventArgs e)
    {
        Preview.Zoom = 1.0;
        UpdatePageInfo();
    }

    private void OnZoomOut(object sender, RoutedEventArgs e)
    {
        Preview.Zoom = Math.Max(0.1, Math.Round(Preview.Zoom / 1.25, 3));
        UpdatePageInfo();
    }

    private void OnZoomIn(object sender, RoutedEventArgs e)
    {
        Preview.Zoom = Math.Min(5.0, Math.Round(Preview.Zoom * 1.25, 3));
        UpdatePageInfo();
    }
}
