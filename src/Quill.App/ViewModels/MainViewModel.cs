using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Windows.Shell;
using System.Windows.Threading;
using Microsoft.Win32;
using Quill.App.Printing;
using Quill.App.Settings;
using Quill.Core.Editing;
using Quill.Core.Model;
using Quill.Core.Styles;
using Quill.Core.Text;
using Quill.Core.Units;
using Quill.Docx;
using Style = Quill.Core.Model.Style;

namespace Quill.App.ViewModels;

/// <summary>State and commands for the main window: the editing session plus toolbar and status-bar mirrors.</summary>
public sealed partial class MainViewModel : ObservableObject
{
    private static readonly double[] StandardSizes = [8, 9, 10, 10.5, 11, 12, 14, 16, 18, 20, 22, 24, 26, 28, 36, 48, 72];

    private const string FileFilter = "Word Documents (*.docx)|*.docx|All files (*.*)|*.*";

    private readonly AppSettings _settings = AppSettings.Load();
    private readonly DispatcherTimer _autosaveTimer;
    private Document? _lastAutosaved;
    private string _recoveryId = RecoveryStore.NewId();
    private bool _openingRecent;
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
        Zoom = Math.Clamp(_settings.Zoom, 0.1, 5.0);
        ShowFormattingMarks = _settings.ShowFormattingMarks;
        Theme = ThemeChoices.Contains(_settings.Theme) ? _settings.Theme : "System";
        RecentFiles = new ObservableCollection<RecentFile>(_settings.RecentFiles.Select(p => new RecentFile(p)));
        _autosaveTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMinutes(Math.Max(1, _settings.AutosaveMinutes)) };
        _autosaveTimer.Tick += (_, _) => Autosave();
        if (_settings.AutosaveMinutes > 0)
        {
            _autosaveTimer.Start();
        }
        FontSizes = new ObservableCollection<double>(StandardSizes);
        ParagraphStyles = new ObservableCollection<Style>(Session.Document.Styles.ParagraphStyles.Where(s => s.QuickFormat).OrderBy(s => s.Priority));
        RefreshFormatState();
        UpdateTitle();
    }

    public EditingSession Session { get; }

    /// <summary>File name without extension, used for window titles and print jobs.</summary>
    public string DocumentName => _documentName;

    public ObservableCollection<double> FontSizes { get; }

    public ObservableCollection<RecentFile> RecentFiles { get; }

    public AppSettings Settings => _settings;

    public IReadOnlyList<string> ThemeChoices { get; } = ["System", "Light", "Dark"];

    /// <summary>Light, dark or follow Windows; applied immediately and remembered.</summary>
    [ObservableProperty]
    public partial string Theme { get; set; } = "System";

    partial void OnThemeChanged(string value)
    {
        ApplyTheme(value);
        _settings.Theme = value;
        _settings.Save();
    }

    private static void ApplyTheme(string theme)
    {
        if (Application.Current is not { } app)
        {
            return;
        }

        app.ThemeMode = theme switch
        {
            "Light" => ThemeMode.Light,
            "Dark" => ThemeMode.Dark,
            _ => ThemeMode.System,
        };
    }

    [ObservableProperty]
    public partial RecentFile? SelectedRecent { get; set; }


    partial void OnSelectedRecentChanged(RecentFile? value)
    {
        if (value is null || _openingRecent)
        {
            return;
        }

        _openingRecent = true;
        try
        {
            if (!File.Exists(value.Path))
            {
                MessageBox.Show("The file no longer exists:" + Environment.NewLine + value.Path, "Quill", MessageBoxButton.OK, MessageBoxImage.Information);
                _settings.RemoveRecent(value.Path);
                RefreshRecent();
            }
            else if (ConfirmDiscard())
            {
                OpenFile(value.Path);
            }
        }
        finally
        {
            SelectedRecent = null;
            _openingRecent = false;
        }
    }

    private void RefreshRecent()
    {
        RecentFiles.Clear();
        foreach (string path in _settings.RecentFiles)
        {
            RecentFiles.Add(new RecentFile(path));
        }
    }

    private void RememberFile(string path)
    {
        _settings.AddRecent(path);
        RefreshRecent();
        _settings.Save();
        try
        {
            JumpList.AddToRecentCategory(path);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            // Jump lists need a registered file type; not fatal.
        }
    }

    // ----- Autosave and recovery -----

    private void Autosave()
    {
        if (!Session.IsDirty || ReferenceEquals(Session.Document, _lastAutosaved))
        {
            return;
        }

        try
        {
            RecoveryStore.Write(_recoveryId, Session.Document, _documentPath, _documentName);
            _lastAutosaved = Session.Document;
            StatusMessage = "Autosaved at " + DateTime.Now.ToShortTimeString();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            StatusMessage = "Autosave failed: " + ex.Message;
        }
    }

    private void StartNewRecoverySession()
    {
        RecoveryStore.Delete(_recoveryId);
        _recoveryId = RecoveryStore.NewId();
        _lastAutosaved = null;
    }

    /// <summary>Offers to restore autosaved documents left behind by a crash. Call once after the main window is up.</summary>
    public void OfferRecovery()
    {
        foreach (RecoveryStore.Entry entry in RecoveryStore.Pending())
        {
            if (entry.Id == _recoveryId)
            {
                continue;
            }

            string when = entry.SavedAt.LocalDateTime.ToString("g", System.Globalization.CultureInfo.CurrentCulture);
            MessageBoxResult answer = MessageBox.Show(
                "Quill found unsaved changes to \"" + entry.Name + "\" from " + when + "." + Environment.NewLine + Environment.NewLine + "Recover them?",
                "Quill",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question,
                MessageBoxResult.Yes);
            if (answer == MessageBoxResult.Yes)
            {
                try
                {
                    LoadResult result = DocxReader.ReadFile(entry.File);
                    Session.LoadDocument(result.Document, isDirty: true);
                    _documentPath = entry.OriginalPath;
                    _documentName = entry.Name;
                    _loadWasLossy = false;
                    _recoveryId = entry.Id;
                    _lastAutosaved = Session.Document;
                    StatusMessage = "Recovered unsaved changes; save to keep them";
                    UpdateTitle();
                    return;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or FileFormatException or DocumentFormat.OpenXml.Packaging.OpenXmlPackageException)
                {
                    MessageBox.Show("Could not recover the document." + Environment.NewLine + Environment.NewLine + ex.Message, "Quill", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }

            RecoveryStore.Delete(entry.Id);
        }
    }

    /// <summary>Remembers window placement and zoom. Call when the main window closes.</summary>
    public void SaveWindowState(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        Rect bounds = window.WindowState == WindowState.Normal ? new Rect(window.Left, window.Top, window.Width, window.Height) : window.RestoreBounds;
        _settings.WindowLeft = bounds.Left;
        _settings.WindowTop = bounds.Top;
        _settings.WindowWidth = bounds.Width;
        _settings.WindowHeight = bounds.Height;
        _settings.WindowMaximized = window.WindowState == WindowState.Maximized;
        _settings.Zoom = Zoom;
        _settings.ShowFormattingMarks = ShowFormattingMarks;
        _settings.Save();
        if (!Session.IsDirty)
        {
            RecoveryStore.Delete(_recoveryId);
        }
    }

    /// <summary>Applies the remembered window placement if it is still on screen. Call before the window is shown.</summary>
    public void RestoreWindowState(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        if (_settings.WindowWidth is { } width && _settings.WindowHeight is { } height && _settings.WindowLeft is { } left && _settings.WindowTop is { } top
            && width >= 400 && height >= 300
            && left + width > SystemParameters.VirtualScreenLeft + 50 && top + 50 < SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight
            && left < SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - 50 && top >= SystemParameters.VirtualScreenTop)
        {
            window.WindowStartupLocation = WindowStartupLocation.Manual;
            window.Left = left;
            window.Top = top;
            window.Width = width;
            window.Height = height;
        }

        if (_settings.WindowMaximized)
        {
            window.WindowState = WindowState.Maximized;
        }
    }

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
    public partial bool IsStrikethrough { get; set; }

    [ObservableProperty]
    public partial bool IsSuperscript { get; set; }

    [ObservableProperty]
    public partial bool IsSubscript { get; set; }

    [ObservableProperty]
    public partial Color FontColor { get; set; }

    [ObservableProperty]
    public partial Color HighlightSwatch { get; set; }

    [ObservableProperty]
    public partial double LineSpacingValue { get; set; }

    [ObservableProperty]
    public partial double SpaceBeforePoints { get; set; }

    [ObservableProperty]
    public partial double SpaceAfterPoints { get; set; }

    public IReadOnlyList<double> LineSpacingChoices { get; } = [1.0, 1.08, 1.15, 1.5, 2.0, 2.5, 3.0];

    public IReadOnlyList<double> ParagraphSpacingChoices { get; } = [0, 3, 6, 8, 10, 12, 18, 24];

    [ObservableProperty]
    public partial Alignment Alignment { get; set; }

    [ObservableProperty]
    public partial bool IsBulleted { get; set; }

    [ObservableProperty]
    public partial bool IsNumbered { get; set; }

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
    public partial bool ShowFormattingMarks { get; set; }

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
    private void IncreaseIndent() => Session.ChangeIndent(1);

    [RelayCommand]
    private void DecreaseIndent() => Session.ChangeIndent(-1);

    partial void OnIsBulletedChanged(bool value)
    {
        if (!_syncingFormat && value != (Session.ListKind(Session.Document.GetParagraph(Session.Selection.Active)) == true))
        {
            Session.ToggleList(bulleted: true);
        }
    }

    partial void OnIsNumberedChanged(bool value)
    {
        if (!_syncingFormat && value != (Session.ListKind(Session.Document.GetParagraph(Session.Selection.Active)) == false))
        {
            Session.ToggleList(bulleted: false);
        }
    }

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
        StartNewRecoverySession();
        UpdateTitle();
    }

    [RelayCommand]
    private void ChangeCase(string kind)
    {
        if (Enum.TryParse(kind, ignoreCase: true, out CaseChange change))
        {
            Session.ChangeCase(change);
        }
    }

    [RelayCommand]
    private void CycleCase() => Session.CycleCase();

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
            StartNewRecoverySession();
            RememberFile(path);
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
            RecoveryStore.Delete(_recoveryId);
            _lastAutosaved = Session.Document;
            RememberFile(path);
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

        RecoveryStore.Delete(_recoveryId);
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

    partial void OnIsStrikethroughChanged(bool value)
    {
        if (!_syncingFormat)
        {
            Session.ApplyRunFormat(new RunProperties { Strikethrough = value });
        }
    }

    partial void OnIsSuperscriptChanged(bool value)
    {
        if (!_syncingFormat)
        {
            Session.ApplyRunFormat(new RunProperties { VerticalAlignment = value ? VerticalTextAlignment.Superscript : VerticalTextAlignment.Baseline });
        }
    }

    partial void OnIsSubscriptChanged(bool value)
    {
        if (!_syncingFormat)
        {
            Session.ApplyRunFormat(new RunProperties { VerticalAlignment = value ? VerticalTextAlignment.Subscript : VerticalTextAlignment.Baseline });
        }
    }

    partial void OnLineSpacingValueChanged(double value)
    {
        if (!_syncingFormat && value > 0)
        {
            Session.ApplyParagraphFormat(new ParagraphProperties { LineSpacing = LineSpacing.Multiple(value) });
        }
    }

    partial void OnSpaceBeforePointsChanged(double value)
    {
        if (!_syncingFormat && value >= 0)
        {
            Session.ApplyParagraphFormat(new ParagraphProperties { SpaceBefore = Twips.FromPoints(value) });
        }
    }

    partial void OnSpaceAfterPointsChanged(double value)
    {
        if (!_syncingFormat && value >= 0)
        {
            Session.ApplyParagraphFormat(new ParagraphProperties { SpaceAfter = Twips.FromPoints(value) });
        }
    }

    /// <summary>Applies a font color from the picker: "auto" or an RRGGBB hex string.</summary>
    public void SetFontColor(string value)
    {
        DocColor color = value == "auto" ? DocColor.Auto : DocColor.Parse(value);
        Session.ApplyRunFormat(new RunProperties { Color = color });
        RefreshFormatState();
    }

    /// <summary>Applies a highlight from the picker by <see cref="HighlightColor"/> name.</summary>
    public void SetHighlight(string name)
    {
        if (Enum.TryParse(name, out HighlightColor highlight))
        {
            Session.ApplyRunFormat(new RunProperties { Highlight = highlight });
            RefreshFormatState();
        }
    }

    private static readonly double[] FontSizeSteps = [8, 9, 10, 11, 12, 14, 16, 18, 20, 22, 24, 26, 28, 36, 48, 72];

    [RelayCommand]
    private void ToggleStrikethrough() => IsStrikethrough = !IsStrikethrough;

    [RelayCommand]
    private void ToggleSuperscript() => IsSuperscript = !IsSuperscript;

    [RelayCommand]
    private void ToggleSubscript() => IsSubscript = !IsSubscript;

    [RelayCommand]
    private void GrowFont() => StepFont(1);

    [RelayCommand]
    private void ShrinkFont() => StepFont(-1);

    [RelayCommand]
    private void NudgeFont(string delta) => ApplyFontSize(Math.Clamp(FontSize + double.Parse(delta, System.Globalization.CultureInfo.InvariantCulture), 1, 400));

    /// <summary>Word's Ctrl+Shift+> and Ctrl+Shift+< steps through the standard sizes.</summary>
    private void StepFont(int direction)
    {
        double current = FontSize;
        double next = direction > 0
            ? FontSizeSteps.FirstOrDefault(s => s > current + 0.01, Math.Min(400, current + 2))
            : FontSizeSteps.LastOrDefault(s => s < current - 0.01, Math.Max(1, current - 2));
        ApplyFontSize(next);
    }

    private void ApplyFontSize(double points)
    {
        Session.ApplyRunFormat(new RunProperties { FontSize = HalfPoints.FromPoints(points) });
        RefreshFormatState();
    }

    [RelayCommand]
    private void ApplyHeading(string level)
    {
        string styleId = level switch
        {
            "1" => DefaultStyleSheet.Heading1Id,
            "2" => DefaultStyleSheet.Heading2Id,
            "3" => DefaultStyleSheet.Heading3Id,
            _ => StyleSheet.NormalStyleId,
        };
        if (Session.Document.Styles.Contains(styleId))
        {
            Session.SetParagraphStyle(styleId);
            RefreshFormatState();
        }
    }

    [RelayCommand]
    private void SetLineSpacing(string factor) => LineSpacingValue = double.Parse(factor, System.Globalization.CultureInfo.InvariantCulture);

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
            IsStrikethrough = formats.Count > 0 && formats.All(f => f.Strikethrough);
            IsSuperscript = formats.Count > 0 && formats.All(f => f.VerticalAlignment == VerticalTextAlignment.Superscript);
            IsSubscript = formats.Count > 0 && formats.All(f => f.VerticalAlignment == VerticalTextAlignment.Subscript);
            ResolvedRunProperties caret = formats.Count > 0 ? formats[0] : Session.CaretFormat();
            FontFamily = caret.FontFamily;
            FontSize = caret.FontSize.ToPoints();
            ResolvedParagraphProperties paragraph = Session.CaretParagraphFormat();
            Alignment = paragraph.Alignment;
            LineSpacingValue = paragraph.LineSpacing.Rule == LineSpacingRule.Auto ? Math.Round(paragraph.LineSpacing.Factor, 2) : 0;
            SpaceBeforePoints = Math.Round(paragraph.SpaceBefore.ToPoints(), 1);
            SpaceAfterPoints = Math.Round(paragraph.SpaceAfter.ToPoints(), 1);
            DocColor color = caret.Color;
            FontColor = color.IsAuto ? Colors.Black : Color.FromRgb(color.R, color.G, color.B);
            HighlightSwatch = Quill.Layout.Wpf.FontCatalog.Shared.GetHighlightBrush(caret.Highlight)?.Color ?? Colors.Yellow;
            Paragraph current = Session.Document.GetParagraph(Session.Selection.Active);
            CurrentStyle = ParagraphStyles.FirstOrDefault(s => s.Id == (current.StyleId ?? Session.Document.Styles.DefaultParagraphStyleId));
            bool? listKind = Session.ListKind(current);
            IsBulleted = listKind == true;
            IsNumbered = listKind == false;
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

/// <summary>A recently opened file for the File tab list.</summary>
public sealed record RecentFile(string Path)
{
    public string Name => System.IO.Path.GetFileName(Path);

    public override string ToString() => Name;
}
