using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using Quill.App.Imaging;
using Quill.App.Spelling;
using Quill.App.ViewModels;
using Quill.Core.Editing;
using Quill.Core.Model;
using Quill.Core.Text;
using Quill.Core.Units;

namespace Quill.App.Views;

public partial class MainWindow : Window
{
    public static readonly RoutedCommand FindNextCommand = new("FindNext", typeof(MainWindow));
    public static readonly RoutedCommand FindPreviousCommand = new("FindPrevious", typeof(MainWindow));
    public static readonly RoutedCommand GoToPageCommand = new("GoToPage", typeof(MainWindow));
    public static readonly RoutedCommand WordCountCommand = new("WordCount", typeof(MainWindow));
    public static readonly RoutedCommand ToggleRibbonCommand = new("ToggleRibbon", typeof(MainWindow));
    public static readonly RoutedCommand ShortcutsCommand = new("Shortcuts", typeof(MainWindow));
    public static readonly RoutedCommand PastePlainCommand = new("PastePlain", typeof(MainWindow));
    public static readonly RoutedCommand InsertLinkCommand = new("InsertLink", typeof(MainWindow));
    public static readonly RoutedCommand FontDialogCommand = new("FontDialog", typeof(MainWindow));
    public static readonly RoutedCommand InsertCommentCommand = new("InsertComment", typeof(MainWindow));

    private FindReplaceWindow? _findWindow;

    public MainWindow()
    {
        InitializeComponent();
        ViewModel.CurrentSectionProvider = () => Editor.CurrentSectionIndex();
        Loaded += (_, _) => ApplyRibbonCollapse();
        Editor.GotKeyboardFocus += (_, _) =>
        {
            if (ViewModel.IsRibbonCollapsed && Tabs.SelectedIndex >= 0 && !ReferenceEquals(Tabs.SelectedItem, PictureTab) && !ReferenceEquals(Tabs.SelectedItem, HeaderFooterTab))
            {
                Tabs.SelectedIndex = -1;
            }
        };
        ViewModel.RestoreWindowState(this);
        Loaded += OnLoaded;
        Closing += OnClosing;
    }

    private MainViewModel ViewModel => (MainViewModel)DataContext;

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        Editor.Session = ViewModel.Session;
        Ruler.Editor = Editor;
        VerticalRuler.Editor = Editor;
        Editor.SelectionGestureCompleted += OnSelectionGesture;
        Editor.PreviewKeyDown += (_, _) => HideMiniToolbar();
        Editor.PreviewMouseLeftButtonDown += (_, _) => HideMiniToolbar();
        ViewModel.Session.SelectionChanged += (_, _) =>
        {
            if (ViewModel.Session.Selection.IsCollapsed)
            {
                HideMiniToolbar();
            }
        };
        ViewModel.Session.SelectionChanged += (_, _) => Ruler.Refresh();
        ViewModel.Session.DocumentChanged += (_, _) => Ruler.Refresh();

        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
        ViewModel.PainterChanged += (_, _) => Editor.PendingFormatSample = ViewModel.PainterSample;
        Editor.FormatPainted += (_, _) =>
        {
            if (!ViewModel.FormatPainterSticky)
            {
                ViewModel.IsFormatPainterActive = false;
            }

            ViewModel.RefreshFormatState();
        };
        Editor.FormatPainterCancelled += (_, _) => ViewModel.IsFormatPainterActive = false;
        Editor.LayoutFailed += (_, ex) => ViewModel.StatusMessage = "Layout error (" + ex.GetType().Name + "): the previous layout is shown; details in crash.log";

        Editor.ViewStateChanged += (_, _) =>
        {
            ViewModel.PageCount = Editor.PageCount;
            ViewModel.CurrentPage = Editor.CurrentPage;
        };
        ViewModel.PageCount = Editor.PageCount;
        PopupThemeFix.AttachAll(this);
        Dispatcher.BeginInvoke(() => PopupThemeFix.AttachAll(this), System.Windows.Threading.DispatcherPriority.Loaded);
        if (!s_startupFileConsumed && Application.Current is App { StartupFile: { } startupFile })
        {
            s_startupFileConsumed = true;
            ViewModel.OpenFile(startupFile);
        }
        else if (!s_recoveryOffered)
        {
            s_recoveryOffered = true;
            Dispatcher.BeginInvoke(ViewModel.OfferRecovery, System.Windows.Threading.DispatcherPriority.Background);
        }


        Editor.Focus();
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        if (e.Handled)
        {
            return;
        }

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
            return files.FirstOrDefault(f => f.EndsWith(".docx", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".rtf", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".txt", StringComparison.OrdinalIgnoreCase) || ImageFiles.IsPictureFile(f));
        }

        return null;
    }

    private void OnCaseMenu(object sender, RoutedEventArgs e)
    {
        if (sender is Button { ContextMenu: { } menu } button)
        {
            menu.PlacementTarget = button;
            menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            menu.IsOpen = true;
        }
    }

    private void OnMenuClosed(object sender, RoutedEventArgs e) => Editor.Focus();

    private SymbolWindow? _symbolWindow;

    private void OnInsertSymbol(object sender, RoutedEventArgs e)
    {
        if (_symbolWindow is null || !_symbolWindow.IsLoaded)
        {
            _symbolWindow = new SymbolWindow(symbol =>
            {
                ViewModel.Session.InsertText(symbol);
                Editor.Focus();
            })
            {
                Owner = this,
            };
        }

        _symbolWindow.Show();
        _symbolWindow.Activate();
    }

    private void OnInsertDateTime(object sender, RoutedEventArgs e)
    {
        var dialog = new DateTimeWindow { Owner = this };
        if (dialog.ShowDialog() == true)
        {
            ViewModel.Session.InsertText(dialog.Text);
        }

        Editor.Focus();
    }

    /// <summary>Insert > Table of Contents: from the headings and their current page numbers; run again to refresh.</summary>
    private void OnInsertToc(object sender, RoutedEventArgs e)
    {
        EditingSession session = ViewModel.Session;
        List<TocEntry> entries = CollectTocEntries(session);
        session.InsertTableOfContents(entries, Editor.TextColumnWidth());
        // The table itself shifts the pages, so take the numbers again once the layout settled.
        for (int pass = 0; pass < 2; pass++)
        {
            List<TocEntry> refreshed = CollectTocEntries(session);
            if (refreshed.SequenceEqual(entries))
            {
                break;
            }

            entries = refreshed;
            session.InsertTableOfContents(entries, Editor.TextColumnWidth());
        }

        ViewModel.StatusMessage = entries.Count == 0
            ? "No headings found: apply Heading 1-3 styles, then insert the table of contents again"
            : "Table of contents with " + entries.Count.ToString(System.Globalization.CultureInfo.CurrentCulture) + " entries; insert again to refresh the page numbers";
        Editor.Focus();
    }

    private void OnInsertTableOfFigures(object sender, RoutedEventArgs e)
    {
        string label = (sender as FrameworkElement)?.Tag as string ?? "Figure";
        EditingSession session = ViewModel.Session;
        List<TocEntry> entries = CollectCaptionEntries(session, label);
        session.InsertTableOfFigures(entries, Editor.TextColumnWidth(), label);
        for (int pass = 0; pass < 2; pass++)
        {
            List<TocEntry> refreshed = CollectCaptionEntries(session, label);
            if (refreshed.SequenceEqual(entries))
            {
                break;
            }

            entries = refreshed;
            session.InsertTableOfFigures(entries, Editor.TextColumnWidth(), label);
        }

        ViewModel.StatusMessage = entries.Count == 0
            ? "No " + label.ToLowerInvariant() + " captions found: insert captions first, then the table again"
            : "Table of " + label.ToLowerInvariant() + "s with " + entries.Count.ToString(System.Globalization.CultureInfo.CurrentCulture) + " entries; insert again to refresh the page numbers";
        Editor.Focus();
    }

    private List<TocEntry> CollectCaptionEntries(EditingSession session, string label) =>
        TableOfFigures.Captions(session.Document, label)
            .Select(c => new TocEntry(c.Text, 0, Editor.PageNumberTextOf(new TextPosition(c.Story, c.Path, 0)) ?? "?"))
            .ToList();

    private List<TocEntry> CollectTocEntries(EditingSession session)

    {
        var entries = new List<TocEntry>();
        Document document = session.Document;
        for (int s = 0; s < document.Sections.Count; s++)
        {
            foreach ((BlockPath path, Paragraph paragraph) in BlockWalker.Paragraphs(document.Sections[s].Body))
            {
                if (!TableOfContents.IsTocStyle(paragraph.StyleId)
                    && session.Resolver.ResolveParagraph(paragraph).OutlineLevel is { } level && level < 3 && paragraph.FlatText.Trim().Length > 0)
                {
                    var position = new TextPosition(StoryId.Body(s), path, 0);
                    entries.Add(new TocEntry(paragraph.FlatText.Trim(), level, Editor.PageNumberTextOf(position) ?? "?"));
                }
            }
        }


        return entries;
    }

    private void OnInsertLinkCommand(object sender, ExecutedRoutedEventArgs e) => OnInsertLink(sender, e);


    private void OnInsertLink(object sender, RoutedEventArgs e)
    {
        EditingSession session = ViewModel.Session;
        (TextRange Range, string Url)? existing = session.Selection.IsCollapsed ? session.LinkAtCaret() : null;
        TextRange textRange = existing?.Range ?? session.Selection.Range;
        string text = textRange.IsEmpty || !textRange.IsWithinOneParagraph ? string.Empty : DocumentEditor.ExtractFragment(session.Document, textRange).ToPlainText();
        var dialog = new LinkWindow(text, existing?.Url) { Owner = this };
        if (dialog.ShowDialog() == true)
        {
            if (dialog.RemoveRequested)
            {
                session.RemoveLink();
            }
            else
            {
                session.InsertLink(dialog.Text, dialog.Url);
            }
        }

        Editor.Focus();
    }

    private void OnNewStyle(object sender, RoutedEventArgs e)
    {
        EditingSession session = ViewModel.Session;
        Paragraph current = session.Document.GetParagraph(session.Selection.Active);
        string basedOn = session.Document.Styles.Get(current.StyleId ?? session.Document.Styles.DefaultParagraphStyleId)?.Name ?? "Normal";
        var dialog = new NewStyleWindow(basedOn, name => session.Document.Styles.Styles.Values.Any(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase))) { Owner = this };
        if (dialog.ShowDialog() == true && session.CreateStyleFromSelection(dialog.StyleName) is { } id)
        {
            ViewModel.RefreshStyles();
            ViewModel.StatusMessage = "Style \"" + dialog.StyleName + "\" created and applied";
        }

        Editor.Focus();
    }

    private void OnModifyStyle(object sender, RoutedEventArgs e)

    {
        EditingSession session = ViewModel.Session;
        Paragraph current = session.Document.GetParagraph(session.Selection.Active);
        Quill.Core.Model.Style? style = session.Document.Styles.Get(current.StyleId ?? session.Document.Styles.DefaultParagraphStyleId);
        if (style is null)
        {
            Editor.Focus();
            return;
        }

        var dialog = new ModifyStyleWindow(session, style, ViewModel.FontFamilies) { Owner = this };
        if (dialog.ShowDialog() == true)
        {
            if (dialog.UpdateFromSelection)
            {
                session.UpdateStyleFromSelection();
            }
            else
            {
                session.ModifyStyle(dialog.Result);
            }

            ViewModel.RefreshStyles();
        }

        Editor.Focus();
    }

    private void OnFontDialogCommand(object sender, ExecutedRoutedEventArgs e) => OnFontDialog(sender, e);


    private void OnFontDialog(object sender, RoutedEventArgs e)
    {
        EditingSession session = ViewModel.Session;
        Quill.Core.Styles.ResolvedRunProperties current = session.SelectionFormats().FirstOrDefault() ?? session.CaretFormat();
        var dialog = new FontWindow(current, ViewModel.FontFamilies) { Owner = this };
        if (dialog.ShowDialog() == true && !dialog.Delta.IsEmpty)
        {
            session.ApplyRunFormat(dialog.Delta);
            ViewModel.RefreshFormatState();
        }

        Editor.Focus();
    }

    private void OnParagraphDialog(object sender, RoutedEventArgs e)
    {
        EditingSession session = ViewModel.Session;
        var dialog = new ParagraphWindow(session.CaretParagraphFormat()) { Owner = this };
        if (dialog.ShowDialog() == true && !dialog.Delta.IsEmpty)
        {
            session.ApplyParagraphFormat(dialog.Delta);
            ViewModel.RefreshFormatState();
        }

        Editor.Focus();
    }

    private void OnPastePlainClick(object sender, RoutedEventArgs e)
    {
        Editor.PasteFromClipboard(plainTextOnly: true);
        Editor.Focus();
    }

    private void OnOpenLink(object sender, RoutedEventArgs e)
    {
        if (ViewModel.Session.LinkAtCaret() is { } link)
        {
            DocumentView.OpenLink(link.Url);
        }

        Editor.Focus();
    }

    private void OnRemoveLink(object sender, RoutedEventArgs e)
    {
        ViewModel.Session.RemoveLink();
        Editor.Focus();
    }

    /// <summary>Shows the link and picture entries only when they apply, and spelling suggestions for a misspelled word.</summary>
    private void OnEditorMenuOpened(object sender, RoutedEventArgs e)
    {
        if (sender is not ContextMenu menu)
        {
            return;
        }

        foreach (FrameworkElement stale in menu.Items.OfType<FrameworkElement>().Where(i => i.Tag as string == "spell").ToList())
        {
            menu.Items.Remove(stale);
        }

        EditingSession session = ViewModel.Session;
        if (Editor.MisspellingAtCaret() is { } misspelling)
        {
            int at = 0;
            foreach (string suggestion in SpellService.Shared.Suggest(misspelling.Word, misspelling.LanguageTag))
            {
                var item = new MenuItem { Header = suggestion.Replace("_", "__", StringComparison.Ordinal), FontWeight = FontWeights.SemiBold, Tag = "spell" };
                item.Click += (_, _) =>
                {
                    session.ReplaceRange(misspelling.Range, suggestion);
                    session.MoveCaret(session.Selection.End, extend: false);
                    Editor.Focus();
                };
                menu.Items.Insert(at++, item);
            }

            if (at == 0)
            {
                menu.Items.Insert(at++, new MenuItem { Header = "(no spelling suggestions)", IsEnabled = false, Tag = "spell" });
            }

            var ignore = new MenuItem { Header = "_Ignore All", Tag = "spell" };
            ignore.Click += (_, _) =>
            {
                SpellService.Shared.Ignore(misspelling.Word, misspelling.LanguageTag);
                Editor.Focus();
            };
            var add = new MenuItem { Header = "_Add to Dictionary", Tag = "spell" };
            add.Click += (_, _) =>
            {
                SpellService.Shared.Add(misspelling.Word, misspelling.LanguageTag);
                Editor.Focus();
            };
            menu.Items.Insert(at++, ignore);
            menu.Items.Insert(at++, add);
            menu.Items.Insert(at, new Separator { Tag = "spell" });
        }

        bool onLink = session.LinkAtCaret() is not null;

        bool onPicture = session.SelectedImage() is not null;
        foreach (MenuItem item in menu.Items.OfType<MenuItem>())
        {
            switch (item.Tag as string)
            {
                case "link":
                    item.Header = onLink ? "Edit _Link..." : "_Link...";
                    break;
                case "open":
                case "unlink":
                    item.Visibility = onLink ? Visibility.Visible : Visibility.Collapsed;
                    break;
                case "picture":
                    item.Visibility = onPicture ? Visibility.Visible : Visibility.Collapsed;
                    break;
                case "table":
                    item.Visibility = session.IsInTable ? Visibility.Visible : Visibility.Collapsed;
                    break;

                case "list":
                    item.Visibility = session.ListKind(session.Document.GetParagraph(session.Selection.Active)) is not null ? Visibility.Visible : Visibility.Collapsed;
                    break;

            }
        }
    }

    private void OnRestartNumbering(object sender, RoutedEventArgs e)
    {
        ViewModel.Session.RestartNumbering();
        Editor.Focus();
    }

    private void OnContinueNumbering(object sender, RoutedEventArgs e)
    {
        ViewModel.Session.ContinueNumbering();
        Editor.Focus();
    }

    /// <summary>Menu Tag is "Format|template", the template using %1 for the item's own counter.</summary>
    private void OnListStyle(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { Tag: string tag } && tag.Split('|') is [string formatName, string template] && Enum.TryParse(formatName, out NumberFormat format))
        {
            EditingSession session = ViewModel.Session;
            int level = session.CaretParagraphFormat().List?.Level ?? 0;
            string text = format == NumberFormat.Bullet ? template : template.Replace("%1", "%" + (level + 1).ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);
            session.SetListStyle(format, text);
        }

        Editor.Focus();
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

    private void OnSelectionGesture(object? sender, SelectionGestureEventArgs e)
    {
        if (!ViewModel.Session.Selection.IsCollapsed && Editor.IsKeyboardFocusWithin)
        {
            MiniToolbar.HorizontalOffset = Math.Max(0, e.ViewPoint.X - 40);
            MiniToolbar.VerticalOffset = Math.Max(0, e.ViewPoint.Y - 56);
            MiniToolbar.IsOpen = true;
            Dispatcher.BeginInvoke(() => PopupThemeFix.AttachAll(this), System.Windows.Threading.DispatcherPriority.Loaded);
        }
    }

    private void HideMiniToolbar()
    {
        if (MiniToolbar.IsOpen && !MiniToolbar.IsMouseOver && !MiniToolbar.IsKeyboardFocusWithin)
        {
            MiniToolbar.IsOpen = false;
        }
    }

    private void OnScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (e.VerticalChange != 0 || e.HorizontalChange != 0)
        {
            MiniToolbar.IsOpen = false;
        }
    }

    private void OnInsertCaption(object sender, RoutedEventArgs e)

    {
        EditingSession session = ViewModel.Session;
        var dialog = new CaptionWindow(session.DefaultCaptionLabel, session.NextCaptionNumber) { Owner = this };
        if (dialog.ShowDialog() == true)
        {
            session.InsertCaption(dialog.Label, dialog.CaptionText, dialog.Above);
        }

        Editor.Focus();
    }

    private void OnAutoFitTable(object sender, RoutedEventArgs e)
    {
        ViewModel.Session.AutoFitTable(Editor.TextColumnWidth());
        Editor.Focus();
    }

    private void OnConvertTextToTable(object sender, RoutedEventArgs e)
    {
        ViewModel.Session.ConvertTextToTable(Editor.TextColumnWidth());
        Editor.Focus();
    }

    private void OnInsertTable(object sender, RoutedEventArgs e)


    {
        var dialog = new InsertTableWindow { Owner = this };
        if (dialog.ShowDialog() == true)
        {
            ViewModel.Session.InsertTable(dialog.Rows, dialog.Columns, Editor.TextColumnWidth());
        }

        Editor.Focus();
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
            _symbolWindow?.Close();
        }
    }

    private static bool s_startupFileConsumed;
    private static bool s_recoveryOffered;

    private void OnNewWindow(object sender, RoutedEventArgs e) => new MainWindow().Show();

    private void OnExit(object sender, RoutedEventArgs e) => Close();


    private void OnCut(object sender, RoutedEventArgs e) => Editor.CutSelection();

    private void OnCopy(object sender, RoutedEventArgs e) => Editor.CopySelection();

    private void OnPaste(object sender, RoutedEventArgs e) => Editor.PasteFromClipboard();

    private void OnPastePlain(object sender, ExecutedRoutedEventArgs e) => Editor.PasteFromClipboard(plainTextOnly: true);

    private void OnPainterDoubleClick(object sender, MouseButtonEventArgs e)
    {
        // The second click of a double-click toggles the button off again; re-arm it as sticky afterwards.
        Dispatcher.BeginInvoke(() =>
        {
            ViewModel.FormatPainterSticky = true;
            ViewModel.IsFormatPainterActive = false;
            ViewModel.FormatPainterSticky = true;
            ViewModel.IsFormatPainterActive = true;
        });
    }

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

    private void OnParagraphShadingSelected(object? sender, string value)
    {
        ViewModel.SetParagraphShading(value);
        Editor.Focus();
    }

    private void OnPageColorSelected(object? sender, string value)

    {
        ViewModel.SetPageColor(value);
        Editor.Focus();
    }

    private void OnCustomWatermark(object sender, RoutedEventArgs e)
    {
        EditingSession session = ViewModel.Session;
        var dialog = new WatermarkWindow(session.Document.Settings.Watermark) { Owner = this };
        if (dialog.ShowDialog() == true)
        {
            session.SetWatermark(dialog.Result);
        }

        Editor.Focus();
    }

    private void OnPageBorders(object sender, RoutedEventArgs e)

    {
        EditingSession session = ViewModel.Session;
        var dialog = new PageBordersWindow(session.PageBordersAtCaret, session.Document.Sections.Count > 1) { Owner = this };
        if (dialog.ShowDialog() == true)
        {
            session.SetPageBorders(dialog.Result, dialog.WholeDocument);
        }

        Editor.Focus();
    }

    private void OnCellShadingSelected(object? sender, string value)

    {
        ViewModel.SetCellShading(value);
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

    private FindReplaceWindow FindWindow => _findWindow ??= new FindReplaceWindow(ViewModel.Session) { Owner = this, Editor = Editor };

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
            ? DocumentStatistics.Compute(DocumentEditor.ExtractFragment(session.Document, session.Selection.Range).AllParagraphs())
            : DocumentStatistics.Compute(session.Document);
        int lines = forSelection ? Editor.SelectionLineCount() : Editor.BodyLineCount;
        int pages = forSelection ? Editor.SelectionPageCount() : Editor.PageCount;
        new WordCountWindow(statistics, pages, lines, forSelection) { Owner = this }.ShowDialog();
        Editor.Focus();
    }

    private void OnProperties(object sender, RoutedEventArgs e)
    {
        EditingSession session = ViewModel.Session;
        var dialog = new DocumentPropertiesWindow(
            session.Document.Metadata,
            ViewModel.DocumentPath,
            ViewModel.DocumentName,
            DocumentStatistics.Compute(session.Document),
            Editor.PageCount)
        {
            Owner = this,
        };
        if (dialog.ShowDialog() == true)
        {
            session.SetMetadata(dialog.Result);
        }

        Editor.Focus();
    }

    private void OnInsertCommentCommand(object sender, ExecutedRoutedEventArgs e) => OnInsertComment(sender, e);

    private void OnInsertComment(object sender, RoutedEventArgs e)
    {
        int id = ViewModel.AddComment();
        Dispatcher.BeginInvoke(() => FocusCommentText(id), System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private void FocusCommentText(int id)
    {
        if (CommentList.ItemContainerGenerator.ContainerFromItem(ViewModel.Comments.FirstOrDefault(c => c.Id == id)) is ListBoxItem item)
        {
            item.BringIntoView();
            if (FindDescendant<TextBox>(item) is { } box)
            {
                box.Focus();
                box.CaretIndex = box.Text.Length;
            }
        }
    }

    private static T? FindDescendant<T>(DependencyObject root)
        where T : DependencyObject
    {
        int count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            DependencyObject child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
            if (child is T match)
            {
                return match;
            }

            if (FindDescendant<T>(child) is { } nested)
            {
                return nested;
            }
        }

        return null;
    }

    private void OnCommentSelected(object sender, SelectionChangedEventArgs e)
    {
        if (ViewModel.IsSyncingComments || CommentList.SelectedItem is not CommentEntry entry || ViewModel.Session.CommentRange(entry.Id) is not { } range)
        {
            return;
        }

        EditingSession session = ViewModel.Session;
        if (session.Document.IsValid(range.Start) && session.Document.IsValid(range.End))
        {
            session.MoveCaret(range.Start, extend: false);
            session.MoveCaret(range.End, extend: true);
        }
    }

    private void OnCommentTextLostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox { DataContext: CommentEntry entry })
        {
            ViewModel.SaveCommentText(entry);
        }
    }

    private void OnDeleteComment(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: int id })
        {
            ViewModel.DeleteComment(id);
        }

        Editor.Focus();
    }

    private void OnHeadingSelected(object sender, SelectionChangedEventArgs e)

    {
        if (ViewModel.IsSyncingHeading || NavList.SelectedItem is not HeadingEntry heading || !ViewModel.Session.Document.IsValid(heading.Position))
        {
            return;
        }

        ViewModel.Session.MoveCaret(heading.Position, extend: false);
        Editor.Focus();
    }

    private void OnOptions(object sender, RoutedEventArgs e)

    {
        var dialog = new OptionsWindow(ViewModel.Settings) { Owner = this };
        if (dialog.ShowDialog() == true)
        {
            ViewModel.ApplyOptions(dialog.AutosaveMinutes, dialog.AutoCorrect, dialog.Units, dialog.Author);
            ViewModel.CheckSpelling = dialog.CheckSpelling;
        }

        Editor.Focus();
    }

    private void OnShortcutsCommand(object sender, ExecutedRoutedEventArgs e) => OnShortcuts(sender, e);


    private void OnShortcuts(object sender, RoutedEventArgs e)
    {
        new ShortcutsWindow { Owner = this }.ShowDialog();
        Editor.Focus();
    }

    private void OnAbout(object sender, RoutedEventArgs e)
    {
        Version version = typeof(App).Assembly.GetName().Version ?? new Version(0, 1);
        MessageBox.Show(
            this,
            "Quill " + version.ToString(3) + Environment.NewLine + Environment.NewLine
            + "A paged word processor for Windows 11." + Environment.NewLine
            + "Built with .NET " + Environment.Version.ToString(2) + " and WPF; .docx via the Open XML SDK; PDF via PDFsharp." + Environment.NewLine + Environment.NewLine
            + "Settings and logs: " + Quill.App.Settings.AppSettings.Directory,
            "About Quill",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
        Editor.Focus();
    }

    private int _tabBeforePicture = 1;

    private void OnToggleRibbon(object sender, ExecutedRoutedEventArgs e) => ViewModel.IsRibbonCollapsed = !ViewModel.IsRibbonCollapsed;

    /// <summary>Collapsed ribbon: no tab selected, so only the tab strip shows; clicking a tab opens it until the editor takes focus again.</summary>
    private void ApplyRibbonCollapse()
    {
        if (ViewModel.IsRibbonCollapsed)
        {
            Tabs.SelectedIndex = -1;
        }
        else if (Tabs.SelectedIndex < 0)
        {
            Tabs.SelectedIndex = Math.Max(0, Math.Min(_tabBeforePicture, Tabs.Items.Count - 1));
        }
    }

    private void RestoreTab() => Tabs.SelectedIndex = ViewModel.IsRibbonCollapsed ? -1 : Math.Max(0, Math.Min(_tabBeforePicture, Tabs.Items.Count - 1));


    /// <summary>
    /// Shows the Picture tab while a picture is selected and returns to the previous tab afterwards; the Table tab
    /// only appears (Word does not switch to it either) but gives way to the previous tab when the caret leaves the table.
    /// </summary>
    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.IsInTable))
        {
            if (!ViewModel.IsInTable && ReferenceEquals(Tabs.SelectedItem, TableTab))
            {
                RestoreTab();
            }

            return;
        }

        if (e.PropertyName == nameof(MainViewModel.IsInHeaderFooter))
        {
            if (ViewModel.IsInHeaderFooter)
            {
                if (!ReferenceEquals(Tabs.SelectedItem, HeaderFooterTab))
                {
                    if (!ReferenceEquals(Tabs.SelectedItem, PictureTab) && !ReferenceEquals(Tabs.SelectedItem, TableTab) && Tabs.SelectedIndex >= 0)
                    {
                        _tabBeforePicture = Tabs.SelectedIndex;
                    }

                    Tabs.SelectedItem = HeaderFooterTab;
                }
            }
            else if (ReferenceEquals(Tabs.SelectedItem, HeaderFooterTab))
            {
                RestoreTab();
            }

            return;
        }

        if (e.PropertyName == nameof(MainViewModel.IsRibbonCollapsed))

        {
            ApplyRibbonCollapse();
            return;
        }

        if (e.PropertyName != nameof(MainViewModel.IsPictureSelected))
        {
            return;
        }

        if (ViewModel.IsPictureSelected)

        {
            if (!ReferenceEquals(Tabs.SelectedItem, PictureTab))
            {
                _tabBeforePicture = Tabs.SelectedIndex;
                Tabs.SelectedItem = PictureTab;
            }
        }
        else if (ReferenceEquals(Tabs.SelectedItem, PictureTab))
        {
            RestoreTab();
        }
    }

    /// <summary>Tab contents are created on first use, so new drop-downs need the dark-mode fix attached then.</summary>
    private void OnTabChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!ReferenceEquals(e.OriginalSource, Tabs))
        {
            return;
        }

        if (ReferenceEquals(Tabs.SelectedItem, TableTab) && e.RemovedItems.Count > 0 && !ReferenceEquals(e.RemovedItems[0], PictureTab) && !ReferenceEquals(e.RemovedItems[0], TableTab))
        {
            _tabBeforePicture = Tabs.Items.IndexOf(e.RemovedItems[0]); // where to go back to when the caret leaves the table
        }

        Dispatcher.BeginInvoke(() => PopupThemeFix.AttachAll(this), System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private (TextPosition Start, InlineImage Image)? RequirePicture()
    {
        if (ViewModel.Session.SelectedImage() is { } picture)
        {
            return picture;
        }

        MessageBox.Show(this, "Click a picture first.", "Quill", MessageBoxButton.OK, MessageBoxImage.Information);
        return null;
    }

    private void SelectPicture(TextPosition start)
    {
        EditingSession session = ViewModel.Session;
        session.MoveCaret(start, extend: false);
        session.MoveCaret(start.WithOffset(start.Offset + 1), extend: true);
    }

    private void OnPictureOriginalSize(object sender, RoutedEventArgs e)
    {
        EditingSession session = ViewModel.Session;
        if (RequirePicture() is { } picture)
        {
            ImageData? data = session.Document.Images.Get(picture.Image.ImageId);
            (Twips Width, Twips Height)? natural = data is null ? null : ImageFiles.NaturalSize(data);
            if (natural is { } size)
            {
                session.ResizeImage(size.Width, size.Height);
            }
            else
            {
                MessageBox.Show(this, "The original size of this picture is not known.", "Quill", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        Editor.Focus();
    }

    private void OnPictureFitWidth(object sender, RoutedEventArgs e)
    {
        if (RequirePicture() is { } picture)
        {
            ScalePicture(picture.Image, Editor.TextColumnWidth().Value / (double)Math.Max(1, picture.Image.Width.Value));
        }

        Editor.Focus();
    }

    private void OnPictureScale(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string tag } && double.TryParse(tag, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double factor) && RequirePicture() is { } picture)
        {
            ScalePicture(picture.Image, factor);
        }

        Editor.Focus();
    }

    private void ScalePicture(InlineImage image, double factor)
    {
        var width = new Twips((int)Math.Clamp(Math.Round(image.Width.Value * factor), 15, 22 * 1440));
        var height = new Twips((int)Math.Clamp(Math.Round(image.Height.Value * factor), 15, 22 * 1440));
        ViewModel.Session.ResizeImage(width, height);
    }

    private void OnReplacePicture(object sender, RoutedEventArgs e)
    {
        if (RequirePicture() is { } picture)
        {
            var dialog = new OpenFileDialog { Filter = ImageFiles.Filter, Title = "Replace Picture" };
            if (dialog.ShowDialog(this) == true)
            {
                try
                {
                    PictureSource source = ImageFiles.Load(dialog.FileName);
                    double aspect = source.Width.Value > 0 ? source.Height.Value / (double)source.Width.Value : 1;
                    SelectPicture(picture.Start);
                    Editor.InsertImage(source, picture.Image.Width, new Twips(Math.Max(15, (int)Math.Round(picture.Image.Width.Value * aspect))));
                    SelectPicture(picture.Start);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or FileFormatException or ArgumentException or InvalidOperationException or System.Runtime.InteropServices.COMException)
                {
                    MessageBox.Show(this, "Could not read the picture." + Environment.NewLine + Environment.NewLine + ex.Message, "Quill", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        Editor.Focus();
    }

    private void OnDeletePicture(object sender, RoutedEventArgs e)
    {
        if (RequirePicture() is { } picture)
        {
            SelectPicture(picture.Start);
            ViewModel.Session.DeleteSelection();
        }

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
