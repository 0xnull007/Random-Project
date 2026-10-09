using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using Quill.App.Printing;
using Quill.Core.Editing;
using Quill.Core.Model;
using Quill.Core.Styles;
using Quill.Core.Units;
using Quill.Docx;
using Style = Quill.Core.Model.Style;

namespace Quill.App.ViewModels;

/// <summary>State and commands for the main window: the editing session plus toolbar and status-bar mirrors.</summary>
public sealed partial class MainViewModel : ObservableObject
{
    private static readonly double[] StandardSizes = [8, 9, 10, 10.5, 11, 12, 14, 16, 18, 20, 22, 24, 26, 28, 36, 48, 72];

    private const string FileFilter = "Word Documents (*.docx)|*.docx|All files (*.*)|*.*";

    private string _documentName = "Document1";
    private string? _documentPath;
    private bool _loadWasLossy;
    private bool _syncingFormat = true; // stays on until the constructor finishes so initial values do not edit the document

    public MainViewModel()
    {
        Title = "Quill";
        Zoom = 1.0;
        CurrentPage = 1;
        PageCount = 1;
        FontFamily = "Calibri";
        FontSize = 11;
        StatusMessage = string.Empty;
        Session = new EditingSession(Document.CreateNew(metricPaper: IsMetricRegion()));
        Session.DocumentChanged += (_, _) => OnDocumentChanged();
        Session.SelectionChanged += (_, _) => RefreshFormatState();
        FontSizes = new ObservableCollection<double>(StandardSizes);
        ParagraphStyles = new ObservableCollection<Style>(Session.Document.Styles.ParagraphStyles.Where(s => s.QuickFormat).OrderBy(s => s.Priority));
        RefreshFormatState();
        UpdateTitle();
    }

    public EditingSession Session { get; }

    /// <summary>File name without extension, used for window titles and print jobs.</summary>
    public string DocumentName => _documentName;

    public ObservableCollection<double> FontSizes { get; }

    public ObservableCollection<Style> ParagraphStyles { get; }

    public IReadOnlyList<string> FontFamilies { get; } = Fonts.SystemFontFamilies
        .Select(f => f.Source)
        .OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase)
        .ToList();

    [ObservableProperty]
    public partial string Title { get; set; }

    [ObservableProperty]
    public partial double Zoom { get; set; }

    [ObservableProperty]
    public partial int CurrentPage { get; set; }

    [ObservableProperty]
    public partial int PageCount { get; set; }

    [ObservableProperty]
    public partial int WordCount { get; set; }

    [ObservableProperty]
    public partial bool IsBold { get; set; }

    [ObservableProperty]
    public partial bool IsItalic { get; set; }

    [ObservableProperty]
    public partial bool IsUnderline { get; set; }

    [ObservableProperty]
    public partial Alignment Alignment { get; set; }

    [ObservableProperty]
    public partial string FontFamily { get; set; }

    [ObservableProperty]
    public partial double FontSize { get; set; }

    [ObservableProperty]
    public partial Style? CurrentStyle { get; set; }

    [ObservableProperty]
    public partial bool CanUndo { get; set; }

    [ObservableProperty]
    public partial bool CanRedo { get; set; }

    [ObservableProperty]
    public partial string StatusMessage { get; set; }

    public bool IsAlignLeft
    {
        get => Alignment == Alignment.Left;
        set
        {
            if (value)
            {
                Align("Left");
            }
        }
    }

    public bool IsAlignCenter
    {
        get => Alignment == Alignment.Center;
        set
        {
            if (value)
            {
                Align("Center");
            }
        }
    }

    public bool IsAlignRight
    {
        get => Alignment == Alignment.Right;
        set
        {
            if (value)
            {
                Align("Right");
            }
        }
    }

    public bool IsAlignJustify
    {
        get => Alignment == Alignment.Justify;
        set
        {
            if (value)
            {
                Align("Justify");
            }
        }
    }

    public int ZoomPercent => (int)Math.Round(Zoom * 100);

    [RelayCommand]
    private void ToggleBold() => Session.ToggleBold();

    [RelayCommand]
    private void ToggleItalic() => Session.ToggleItalic();

    [RelayCommand]
    private void ToggleUnderline() => Session.ToggleUnderline();

    [RelayCommand]
    private void Align(string which)
    {
        Alignment alignment = which switch
        {
            "Center" => Alignment.Center,
            "Right" => Alignment.Right,
            "Justify" => Alignment.Justify,
            _ => Alignment.Left,
        };
        Session.ApplyParagraphFormat(new ParagraphProperties { Alignment = alignment });
        RefreshFormatState();
    }

    [RelayCommand]
    private void Undo() => Session.Undo();

    [RelayCommand]
    private void Redo() => Session.Redo();

    [RelayCommand]
    private void InsertPageBreak() => Session.InsertBreak(BreakKind.Page);

    [RelayCommand]
    private void InsertSectionBreak() => Session.InsertSectionBreak(SectionStart.NextPage);

    [RelayCommand]
    private void ToggleOrientation()
    {
        int section = Session.Selection.Story.SectionIndex;
        SectionProperties props = Session.Document.Sections[section].Properties;
        Orientation next = props.Orientation == Orientation.Portrait ? Orientation.Landscape : Orientation.Portrait;
        Session.SetSectionProperties(section, props.WithOrientation(next));
    }

    [RelayCommand]
    private void SetMargins(string preset)
    {
        int section = Session.Selection.Story.SectionIndex;
        SectionProperties props = Session.Document.Sections[section].Properties;
        Twips margin = preset switch
        {
            "Narrow" => Twips.FromInches(0.5),
            "Moderate" => Twips.FromInches(0.75),
            _ => Twips.FromInches(1),
        };
        Session.SetSectionProperties(section, props with { MarginTop = margin, MarginBottom = margin, MarginLeft = margin, MarginRight = margin });
    }

    /// <summary>
    /// Applies page setup to the current section or to every section as one undo step. Each section keeps its
    /// own break type and page numbering; the odd/even setting is document-wide.
    /// </summary>
    public void ApplyPageSetup(SectionProperties setup, bool wholeDocument, bool evenAndOddHeaders)
    {
        ArgumentNullException.ThrowIfNull(setup);
        Document document = Session.Document;
        int current = Session.Selection.Story.SectionIndex;
        System.Collections.Immutable.ImmutableList<Section> sections = document.Sections;
        for (int i = 0; i < sections.Count; i++)
        {
            if (!wholeDocument && i != current)
            {
                continue;
            }

            SectionProperties existing = sections[i].Properties;
            SectionProperties updated = setup with
            {
                Start = existing.Start,
                PageNumberStart = existing.PageNumberStart,
                PageNumberFormat = existing.PageNumberFormat,
                ColumnCount = existing.ColumnCount,
            };
            if (updated != existing)
            {
                sections = sections.SetItem(i, sections[i].WithProperties(updated));
            }
        }

        DocumentSettings settings = document.Settings with { EvenAndOddHeaders = evenAndOddHeaders };
        if (ReferenceEquals(sections, document.Sections) && settings == document.Settings)
        {
            return;
        }

        Session.ReplaceDocument(document.WithSections(sections).WithSettings(settings));
    }

    [RelayCommand]
    private void SetZoom(string percent) => Zoom = double.Parse(percent, System.Globalization.CultureInfo.InvariantCulture) / 100.0;

    [RelayCommand]
    private void ZoomIn() => Zoom = Math.Min(5.0, Math.Round(Zoom + 0.1, 2));

    [RelayCommand]
    private void ZoomOut() => Zoom = Math.Max(0.1, Math.Round(Zoom - 0.1, 2));

    [RelayCommand]
    private void NewDocument()
    {
        if (!ConfirmDiscard())
        {
            return;
        }

        Session.LoadDocument(Document.CreateNew(metricPaper: IsMetricRegion()));
        _documentName = "Document1";
        _documentPath = null;
        _loadWasLossy = false;
        StatusMessage = string.Empty;
        UpdateTitle();
    }

    [RelayCommand]
    private void Open()
    {
        if (!ConfirmDiscard())
        {
            return;
        }

        var dialog = new OpenFileDialog { Filter = FileFilter, DefaultExt = ".docx" };
        if (dialog.ShowDialog() == true)
        {
            OpenFile(dialog.FileName);
        }
    }

    public void OpenFile(string path)
    {
        try
        {
            LoadResult result = DocxReader.ReadFile(path);
            Session.LoadDocument(result.Document);
            _documentPath = path;
            _documentName = Path.GetFileNameWithoutExtension(path);
            _loadWasLossy = result.HasLossyContent;
            StatusMessage = result.HasLossyContent
                ? "Opened with limitations: " + string.Join(" ", result.Warnings.Select(w => w.Message))
                : $"Opened {Path.GetFileName(path)}";
            UpdateTitle();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or FileFormatException or DocumentFormat.OpenXml.Packaging.OpenXmlPackageException)
        {
            MessageBox.Show($"Could not open {path}.\n\n{ex.Message}", "Quill", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private void Save()
    {
        if (_documentPath is null)
        {
            SaveAs();
            return;
        }

        if (_loadWasLossy)
        {
            MessageBoxResult choice = MessageBox.Show(
                "This file contains content Quill cannot keep (see the status bar). Saving over it will drop that content.\n\nSave a copy instead?",
                "Quill",
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Warning,
                MessageBoxResult.Yes);
            if (choice == MessageBoxResult.Cancel)
            {
                return;
            }

            if (choice == MessageBoxResult.Yes)
            {
                SaveAs();
                return;
            }
        }

        SaveTo(_documentPath);
    }

    [RelayCommand]
    private void SaveAs()
    {
        var dialog = new SaveFileDialog
        {
            Filter = FileFilter,
            DefaultExt = ".docx",
            FileName = _documentName + ".docx",
            AddExtension = true,
        };
        if (dialog.ShowDialog() == true)
        {
            SaveTo(dialog.FileName);
        }
    }

    private void SaveTo(string path)
    {
        try
        {
            DocxWriter.WriteFile(Session.Document, path);
            Session.MarkSaved();
            _documentPath = path;
            _documentName = Path.GetFileNameWithoutExtension(path);
            _loadWasLossy = false;
            StatusMessage = $"Saved {Path.GetFileName(path)}";
            UpdateTitle();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show($"Could not save {path}.\n\n{ex.Message}", "Quill", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private void Print()
    {
        try
        {
            if (PrintService.Print(Session.Document, Session.Resolver, _documentName))
            {
                StatusMessage = "Sent to printer";
            }
        }
        catch (Exception ex) when (ex is System.Printing.PrintSystemException or InvalidOperationException)
        {
            MessageBox.Show($"Printing failed.\n\n{ex.Message}", "Quill", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private void ExportPdf()
    {
        string? path = PdfExportCommand.Run(Session.Document, Session.Resolver, _documentName, Application.Current.MainWindow);
        if (path is not null)
        {
            StatusMessage = "Exported " + Path.GetFileName(path);
        }
    }

    /// <summary>Asks before discarding unsaved changes. Returns true when it is safe to proceed.</summary>
    public bool ConfirmDiscard()
    {
        if (!Session.IsDirty)
        {
            return true;
        }

        MessageBoxResult result = MessageBox.Show($"Save changes to {_documentName}?", "Quill", MessageBoxButton.YesNoCancel, MessageBoxImage.Question, MessageBoxResult.Yes);
        if (result == MessageBoxResult.Cancel)
        {
            return false;
        }

        if (result == MessageBoxResult.Yes)
        {
            Save();
            return !Session.IsDirty;
        }

        return true;
    }

    partial void OnZoomChanged(double value) => OnPropertyChanged(nameof(ZoomPercent));

    partial void OnAlignmentChanged(Alignment value)
    {
        OnPropertyChanged(nameof(IsAlignLeft));
        OnPropertyChanged(nameof(IsAlignCenter));
        OnPropertyChanged(nameof(IsAlignRight));
        OnPropertyChanged(nameof(IsAlignJustify));
    }

    partial void OnIsBoldChanged(bool value)
    {
        if (!_syncingFormat)
        {
            Session.ApplyRunFormat(new RunProperties { Bold = value });
        }
    }

    partial void OnIsItalicChanged(bool value)
    {
        if (!_syncingFormat)
        {
            Session.ApplyRunFormat(new RunProperties { Italic = value });
        }
    }

    partial void OnIsUnderlineChanged(bool value)
    {
        if (!_syncingFormat)
        {
            Session.ApplyRunFormat(new RunProperties { Underline = value ? UnderlineStyle.Single : UnderlineStyle.None });
        }
    }

    partial void OnFontFamilyChanged(string value)
    {
        if (!_syncingFormat && !string.IsNullOrWhiteSpace(value))
        {
            Session.ApplyRunFormat(new RunProperties { FontFamily = value });
        }
    }

    partial void OnFontSizeChanged(double value)
    {
        if (!_syncingFormat && value > 0)
        {
            Session.ApplyRunFormat(new RunProperties { FontSize = HalfPoints.FromPoints(value) });
        }
    }

    partial void OnCurrentStyleChanged(Style? value)
    {
        if (!_syncingFormat && value is not null)
        {
            Session.SetParagraphStyle(value.Id);
        }
    }

    private void OnDocumentChanged()
    {
        CanUndo = Session.CanUndo;
        CanRedo = Session.CanRedo;
        WordCount = CountWords(Session.Document);
        RefreshFormatState();
        UpdateTitle();
    }

    /// <summary>Mirrors the format at the caret (or across the selection) into the toolbar state.</summary>
    public void RefreshFormatState()
    {
        _syncingFormat = true;
        try
        {
            List<ResolvedRunProperties> formats = Session.SelectionFormats().ToList();
            IsBold = formats.Count > 0 && formats.All(f => f.Bold);
            IsItalic = formats.Count > 0 && formats.All(f => f.Italic);
            IsUnderline = formats.Count > 0 && formats.All(f => f.Underline != UnderlineStyle.None);
            ResolvedRunProperties caret = formats.Count > 0 ? formats[0] : Session.CaretFormat();
            FontFamily = caret.FontFamily;
            FontSize = caret.FontSize.ToPoints();
            ResolvedParagraphProperties paragraph = Session.CaretParagraphFormat();
            Alignment = paragraph.Alignment;
            Paragraph current = Session.Document.GetParagraph(Session.Selection.Active);
            CurrentStyle = ParagraphStyles.FirstOrDefault(s => s.Id == (current.StyleId ?? Session.Document.Styles.DefaultParagraphStyleId));
            CanUndo = Session.CanUndo;
            CanRedo = Session.CanRedo;
        }
        finally
        {
            _syncingFormat = false;
        }
    }

    private void UpdateTitle() => Title = $"{_documentName}{(Session.IsDirty ? "*" : string.Empty)} - Quill";

    private static int CountWords(Document document)
    {
        int count = 0;
        foreach (Section section in document.Sections)
        {
            foreach (Block block in section.Body)
            {
                if (block is Paragraph paragraph)
                {
                    count += paragraph.FlatText.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Count(w => w.Any(char.IsLetterOrDigit));
                }
            }
        }

        return count;
    }

    private static bool IsMetricRegion() => System.Globalization.RegionInfo.CurrentRegion.IsMetric;
}
