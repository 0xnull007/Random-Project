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

    private const string FileFilter = "All supported (*.docx;*.dotx;*.rtf;*.txt)|*.docx;*.dotx;*.rtf;*.txt|Word Documents (*.docx)|*.docx|Word Templates (*.dotx)|*.dotx|Rich Text Format (*.rtf)|*.rtf|Text files (*.txt)|*.txt|All files (*.*)|*.*";
    private const string SaveFilter = "Word Document (*.docx)|*.docx|Word Template (*.dotx)|*.dotx|Rich Text Format (*.rtf)|*.rtf|Plain Text (*.txt)|*.txt";

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
        Session.AutoCorrect = _settings.AutoCorrect.ToOptions();
        Session.Author = AuthorName();
        _headingsTimer.Tick += (_, _) => RefreshHeadings();
        ShowNavigation = _settings.ShowNavigation;
        CheckSpelling = _settings.CheckSpelling;
        ShowRuler = _settings.ShowRuler;
        IsRibbonCollapsed = _settings.RibbonCollapsed;
        ShowComments = _settings.ShowComments;
        UnitPreference.Apply(_settings.Units);
        RecentFiles = new ObservableCollection<RecentFile>(_settings.RecentFiles.Select(p => new RecentFile(p)));
        _autosaveTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMinutes(Math.Max(1, _settings.AutosaveMinutes)) };
        _autosaveTimer.Tick += (_, _) => Autosave();
        if (_settings.AutosaveMinutes > 0)
        {
            _autosaveTimer.Start();
        }
        FontSizes = new ObservableCollection<double>(StandardSizes);
        ParagraphStyles = new ObservableCollection<StyleEntry>(Session.Document.Styles.ParagraphStyles.Where(s => s.QuickFormat).OrderBy(s => s.Priority).Select(ToStyleEntry));
        _knownStyles = Session.Document.Styles;
        if (ShowComments)
        {
            RefreshComments();
        }

        RefreshFormatState();
        UpdateTitle();
    }

    public EditingSession Session { get; }

    /// <summary>File name without extension, used for window titles and print jobs.</summary>
    public string DocumentName => _documentName;

    /// <summary>Full path of the open file, or null for a new document.</summary>
    public string? DocumentPath => _documentPath;

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

    /// <summary>True while the selection is exactly one picture; shows the contextual Picture tab.</summary>
    [ObservableProperty]
    public partial bool IsPictureSelected { get; set; }

    /// <summary>Word's Wrap Text menu: "Inline" puts the picture back in the line; any other value floats it (keeping its place if it already floats).</summary>
    [RelayCommand]
    private void WrapPicture(string mode)
    {
        if (mode == "Inline")
        {
            Session.SetImageAnchor(null);
            return;
        }

        var wrap = Enum.Parse<ImageWrap>(mode);
        ImageAnchor current = Session.SelectedImage() is (_, { Anchor: { } anchor }) ? anchor : ImageAnchor.Default;
        Session.SetImageAnchor(current with { Wrap = wrap });
    }

    /// <summary>Word's Position gallery, simplified: square wrap at the left, centre or right of the column, level with the paragraph's top.</summary>
    [RelayCommand]
    private void PositionPicture(string where)
    {
        var alignment = Enum.Parse<ImageHorizontalAlignment>(where);
        ImageAnchor current = Session.SelectedImage() is (_, { Anchor: { } anchor }) ? anchor : ImageAnchor.Default;
        Session.SetImageAnchor(current with
        {
            Wrap = current.AffectsText ? current.Wrap : ImageWrap.Square,
            HorizontalRelativeTo = HorizontalAnchor.Column,
            HorizontalAlignment = alignment,
            VerticalRelativeTo = VerticalAnchor.Paragraph,
            OffsetY = Twips.Zero,
        });
    }

    /// <summary>True while the caret is inside a table; shows the contextual Table tab.</summary>
    [ObservableProperty]
    public partial bool IsInTable { get; set; }

    /// <summary>Gives the section of the page under the caret (set by the window from the layout); the caret's story section otherwise.</summary>
    public Func<int>? CurrentSectionProvider { get; set; }

    private int HeaderFooterSection => CurrentSectionProvider?.Invoke() ?? Session.Selection.Story.SectionIndex;

    /// <summary>True while the caret is in a header or footer; shows the contextual Header &amp; Footer tab.</summary>
    [ObservableProperty]
    public partial bool IsInHeaderFooter { get; set; }

    /// <summary>Overtype mode (Insert key): shown in the status bar.</summary>
    [ObservableProperty]
    public partial bool IsOvertype { get; set; }

    /// <summary>Matches of the navigation pane's search box, in document order.</summary>
    public ObservableCollection<SearchResultEntry> SearchResults { get; } = [];

    /// <summary>Page thumbnails for the navigation pane's Pages tab (filled by the window from the layout).</summary>
    public ObservableCollection<PageThumbnail> PageThumbnails { get; } = [];


    [ObservableProperty]
    public partial string ResultCountText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool HasNoResults { get; set; }

    /// <summary>Runs the navigation pane search and returns the matched ranges for highlighting (empty for an empty query).</summary>
    public IReadOnlyList<TextRange> UpdateSearchResults(string query)
    {
        SearchResults.Clear();
        if (string.IsNullOrWhiteSpace(query))
        {
            ResultCountText = string.Empty;
            HasNoResults = false;
            return [];
        }

        IReadOnlyList<TextRange> matches = TextSearch.FindAll(Session.Document, query.Trim());
        foreach (TextRange match in matches.Take(500))
        {
            if (Session.Document.TryGetParagraph(match.Start) is not { } paragraph)
            {
                continue;
            }

            string text = paragraph.FlatText;
            int from = Math.Max(0, match.Start.Offset - 30);
            int to = Math.Min(text.Length, match.End.Offset + 40);
            string context = (from > 0 ? "…" : string.Empty) + text[from..to].Replace('￼', ' ').Replace('\t', ' ') + (to < text.Length ? "…" : string.Empty);
            SearchResults.Add(new SearchResultEntry(match, context));
        }

        ResultCountText = matches.Count == 0 ? "No results" : matches.Count == 1 ? "1 result" : matches.Count.ToString("N0", System.Globalization.CultureInfo.CurrentCulture) + " results";
        HasNoResults = matches.Count == 0;
        return matches;
    }



    [ObservableProperty]
    public partial bool DifferentFirstPage { get; set; }

    partial void OnDifferentFirstPageChanged(bool value)
    {
        if (!_syncingFormat)
        {
            Session.SetDifferentFirstPage(HeaderFooterSection, value);
        }
    }

    [ObservableProperty]
    public partial bool DifferentOddEven { get; set; }

    partial void OnDifferentOddEvenChanged(bool value)
    {
        if (!_syncingFormat)
        {
            Session.SetDifferentOddEven(value);
        }
    }

    /// <summary>Word's "Link to Previous": the page shows the previous section's header or footer.</summary>
    [ObservableProperty]
    public partial bool IsLinkedToPrevious { get; set; }

    partial void OnIsLinkedToPreviousChanged(bool value)
    {
        if (_syncingFormat)
        {
            return;
        }

        if (value)
        {
            Session.LinkHeaderFooterToPrevious(HeaderFooterSection);
        }
        else
        {
            Session.UnlinkHeaderFooterFromPrevious(HeaderFooterSection);
        }

        RefreshFormatState();
    }

    [ObservableProperty]
    public partial bool CanLinkToPrevious { get; set; }


    /// <summary>Whether the caret's table row repeats at the top of following pages (Table tab toggle).</summary>
    [ObservableProperty]
    public partial bool IsHeaderRow { get; set; }

    partial void OnIsHeaderRowChanged(bool value)
    {
        if (Session.IsInTable && Session.IsHeaderRowAtCaret != value)
        {
            Session.ToggleHeaderRow();
        }
    }

    /// <summary>Background of the caret's table cell for the shading picker (white when none).</summary>
    [ObservableProperty]
    public partial Color CellShadingSwatch { get; set; } = Colors.White;

    /// <summary>Applies a cell shading from the picker: "auto" clears it, otherwise an RRGGBB hex.</summary>
    public void SetCellShading(string value)
    {
        Session.SetCellShading(value == "auto" ? null : DocColor.Parse(value));
        RefreshFormatState();
    }

    /// <summary>Page colour for the Design tab picker (white when none).</summary>
    [ObservableProperty]
    public partial Color PageColorSwatch { get; set; } = Colors.White;

    /// <summary>Shading of the caret paragraph for the Home tab picker (white when none).</summary>
    [ObservableProperty]
    public partial Color ParagraphShadingSwatch { get; set; } = Colors.White;

    /// <summary>Applies paragraph shading from the picker: "auto" clears it, otherwise an RRGGBB hex.</summary>
    public void SetParagraphShading(string value)
    {
        Session.SetParagraphShading(value == "auto" ? null : DocColor.Parse(value));
        RefreshFormatState();
    }

    [RelayCommand]
    private void SetParagraphBorder(string side)
    {
        Session.ToggleParagraphBorder(side);
        RefreshFormatState();
    }

    [RelayCommand]
    private void SortParagraphs(string order) => Session.SortParagraphs(descending: string.Equals(order, "Desc", StringComparison.OrdinalIgnoreCase));

    [RelayCommand]
    private void ApplyMultilevelList(string preset)
    {
        Session.ApplyMultilevelList(preset);
        RefreshFormatState();
    }


    [RelayCommand]
    private void InsertBlankPage() => Session.InsertBlankPage();

    [RelayCommand]
    private void InsertCoverPage() => Session.InsertCoverPage(AuthorName());

    /// <summary>Insert > Text > Quick Parts: a document property field (AUTHOR, TITLE, SUBJECT, COMMENTS).</summary>
    [RelayCommand]
    private void InsertDocumentProperty(string property) => Session.InsertDocumentPropertyField(property);


    /// <summary>Design > Watermark presets; an empty text removes the watermark.</summary>
    [RelayCommand]
    private void SetWatermark(string text) => Session.SetWatermark(string.IsNullOrWhiteSpace(text) ? null : new Watermark(text));

    /// <summary>Insert > Drop Cap: "None", or "Dropped:3" / "InMargin:3" (position and lines).</summary>
    [RelayCommand]
    private void SetDropCap(string choice)
    {
        string[] parts = choice.Split(':');
        int lines = parts.Length > 1 && int.TryParse(parts[1], out int parsed) ? parsed : 3;
        Session.SetDropCap(parts[0] switch
        {
            "Dropped" => DropCap.Dropped(lines),
            "InMargin" => DropCap.InMargin(lines),
            _ => null,
        });
    }

    /// <summary>Layout > Hyphenation menu: document-wide automatic hyphenation, or the current paragraph's opt-out.</summary>
    [RelayCommand]
    private void SetHyphenation(string mode)
    {
        switch (mode)
        {
            case "Automatic":
                Session.SetAutoHyphenation(true);
                StatusMessage = "Automatic hyphenation on: words break at syllables when a line is full";
                break;
            case "Suppress":
                Session.SuppressHyphenation(true);
                break;
            case "Allow":
                Session.SuppressHyphenation(false);
                break;
            default:
                Session.SetAutoHyphenation(false);
                StatusMessage = "Hyphenation off";
                break;
        }
    }

    /// <summary>Layout > Line Numbers: None, Continuous, EachPage, EachSection, or Suppress / Resume for the selected paragraphs.</summary>
    [RelayCommand]
    private void SetLineNumbers(string mode)
    {
        switch (mode)
        {
            case "None":
                Session.SetLineNumbers(null);
                break;
            case "Suppress":
                Session.SuppressLineNumbers(true);
                break;
            case "Resume":
                Session.SuppressLineNumbers(false);
                break;
            default:
                Session.SetLineNumbers(new LineNumbering { Restart = Enum.TryParse(mode, out LineNumberRestart restart) ? restart : LineNumberRestart.Continuous });
                break;
        }
    }





    /// <summary>Applies a page colour from the picker: "auto" clears it, otherwise an RRGGBB hex.</summary>
    public void SetPageColor(string value)
    {
        Session.SetPageColor(value == "auto" ? null : DocColor.Parse(value));
        RefreshFormatState();
    }



    /// <summary>Mirrors the document's track-changes switch (Review tab toggle, Ctrl+Shift+E).</summary>
    [ObservableProperty]
    public partial bool TrackChanges { get; set; }

    partial void OnTrackChangesChanged(bool value)
    {
        if (Session.TrackChanges != value)
        {
            Session.SetTrackChanges(value);
            StatusMessage = value ? "Track Changes on: insertions are underlined, deletions struck through" : "Track Changes off";
        }
    }

    /// <summary>Word's Display for Review: how tracked changes show (All Markup, Simple Markup, No Markup, Original).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RevisionViewLabel))]
    public partial RevisionView RevisionView { get; set; }

    public string RevisionViewLabel => RevisionView switch
    {
        RevisionView.SimpleMarkup => "Simple Markup",
        RevisionView.NoMarkup => "No Markup",
        RevisionView.Original => "Original",
        _ => "All Markup",
    };

    partial void OnRevisionViewChanged(RevisionView value)
    {
        if (Session.RevisionView != value)
        {
            Session.SetRevisionView(value);
            StatusMessage = value switch
            {
                RevisionView.SimpleMarkup => "Simple Markup: the final text, with a bar beside changed lines",
                RevisionView.NoMarkup => "No Markup: the text as it reads with every change accepted",
                RevisionView.Original => "Original: the text before any tracked change",
                _ => "All Markup: insertions underlined, deletions struck through",
            };
        }
    }

    [RelayCommand]
    private void SetRevisionView(string name) => RevisionView = Enum.Parse<RevisionView>(name);



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
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or FileFormatException or ArgumentException or InvalidOperationException or DocumentFormat.OpenXml.Packaging.OpenXmlPackageException)
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

    public ObservableCollection<StyleEntry> ParagraphStyles { get; }

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

    /// <summary>"12 of 345 words" while text is selected, "345 words" otherwise.</summary>
    [ObservableProperty]
    public partial string WordCountText { get; set; } = "0 words";

    private string AuthorName() => string.IsNullOrWhiteSpace(_settings.Author) ? Environment.UserName : _settings.Author;

    private void UpdateWordCountText()

    {
        string total = WordCount.ToString("N0", System.Globalization.CultureInfo.CurrentCulture);
        if (Session.Selection.IsCollapsed || !Session.Selection.Range.IsWithinOneParagraph && Session.Selection.Range.End.Block.TopIndex - Session.Selection.Range.Start.Block.TopIndex > 2000)
        {
            WordCountText = total + " words";
            return;
        }

        int selected = DocumentStatistics.Compute(DocumentEditor.ExtractFragment(Session.Document, Session.Selection.Range).AllParagraphs()).Words;
        WordCountText = selected.ToString("N0", System.Globalization.CultureInfo.CurrentCulture) + " of " + total + " words";
    }

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
    public partial StyleEntry? CurrentStyle { get; set; }

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
    private void SetColumns(string count)
    {
        if (!int.TryParse(count, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int columns))
        {
            return;
        }

        int section = Session.Selection.Story.SectionIndex;
        SectionProperties props = Session.Document.Sections[section].Properties;
        Session.SetSectionProperties(section, props with { ColumnCount = Math.Clamp(columns, 1, 4) });
    }

    [RelayCommand]
    private void ToggleHeadingNumbers()
    {
        Session.NumberHeadings(!Session.HeadingsAreNumbered);
        RefreshStyles();
        StatusMessage = Session.HeadingsAreNumbered ? "Headings are numbered 1, 1.1, 1.1.1" : "Heading numbering removed";
    }

    [RelayCommand]
    private void InsertEndnote() => Session.InsertEndnote();

    [RelayCommand]
    private void InsertFootnote() => Session.InsertFootnote();

    [RelayCommand]
    private void InsertRowAbove() => Session.InsertRow(below: false);

    [RelayCommand]
    private void InsertRowBelow() => Session.InsertRow(below: true);

    [RelayCommand]
    private void InsertColumnLeft() => Session.InsertColumn(right: false);

    [RelayCommand]
    private void InsertColumnRight() => Session.InsertColumn(right: true);

    [RelayCommand]
    private void DeleteRow() => Session.DeleteRow();

    [RelayCommand]
    private void DeleteColumn() => Session.DeleteColumn();

    [RelayCommand]
    private void DeleteTable() => Session.DeleteTable();

    [RelayCommand]
    private void ToggleTableBorders() => Session.ToggleTableBorders();

    [RelayCommand]
    private void ApplyTableStyle(string preset)
    {
        Session.ApplyTableStyle(preset);
        RefreshFormatState();
    }

    [RelayCommand]
    private void SetTableAlignment(string alignment) => Session.SetTableAlignment(Enum.Parse<TableAlignment>(alignment));

    [RelayCommand]
    private void DistributeColumns() => Session.DistributeColumns();

    [RelayCommand]
    private void ConvertTableToText() => Session.ConvertTableToText();



    [RelayCommand]
    private void MergeCellRight() => Session.MergeCellRight();

    [RelayCommand]
    private void MergeCellDown() => Session.MergeCellDown();

    [RelayCommand]
    private void SplitCell() => Session.SplitCell();


    [RelayCommand]
    private void ToggleTrackChanges() => TrackChanges = !TrackChanges;

    [RelayCommand]
    private void AcceptChange() => Session.AcceptChange();

    [RelayCommand]
    private void RejectChange() => Session.RejectChange();

    [RelayCommand]
    private void AcceptAllChanges()
    {
        Session.AcceptAllChanges();
        StatusMessage = "All tracked changes accepted";
    }

    [RelayCommand]
    private void RejectAllChanges()
    {
        Session.RejectAllChanges();
        StatusMessage = "All tracked changes rejected";
    }

    [RelayCommand]
    private void NextChange()
    {
        if (!Session.SelectNextChange())
        {
            StatusMessage = "No tracked changes";
        }
    }

    [RelayCommand]
    private void PreviousChange()
    {
        if (!Session.SelectPreviousChange())
        {
            StatusMessage = "No tracked changes";
        }
    }



    [RelayCommand]
    private void InsertColumnBreak() => Session.InsertBreak(BreakKind.Column);


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

    // ----- Clear formatting and Format Painter -----

    private FormatSample? _copiedFormat;

    /// <summary>Formatting the Format Painter will apply on the next click; null while it is off.</summary>
    public FormatSample? PainterSample { get; private set; }

    /// <summary>Set by a double-click on the Format Painter: it stays on until Esc.</summary>
    public bool FormatPainterSticky { get; set; }

    public event EventHandler? PainterChanged;

    [ObservableProperty]
    public partial bool IsFormatPainterActive { get; set; }

    partial void OnIsFormatPainterActiveChanged(bool value)
    {
        if (value)
        {
            PainterSample = Session.CopyFormat();
            StatusMessage = FormatPainterSticky ? "Format Painter on: click or drag over text; Esc to stop" : "Format Painter: click or drag over the text to format";
        }
        else
        {
            PainterSample = null;
            FormatPainterSticky = false;
        }

        PainterChanged?.Invoke(this, EventArgs.Empty);
    }

    // ----- Comments pane -----

    public ObservableCollection<CommentEntry> Comments { get; } = [];

    [ObservableProperty]
    public partial bool ShowComments { get; set; }

    [ObservableProperty]
    public partial bool HasNoComments { get; set; } = true;

    [ObservableProperty]
    public partial CommentEntry? SelectedComment { get; set; }

    private bool _syncingComments;

    public bool IsSyncingComments => _syncingComments;

    partial void OnShowCommentsChanged(bool value)
    {
        _settings.ShowComments = value;
        _settings.Save();
        if (value)
        {
            RefreshComments();
        }
    }

    /// <summary>Rebuilds the comment list from the document (cheap: comments are few).</summary>
    public void RefreshComments()
    {
        _syncingComments = true;
        try
        {
            int? selectedId = SelectedComment?.Id;
            Comments.Clear();
            foreach (Comment comment in Session.Document.Comments.Values.OrderBy(c => c.Id))
            {
                Comments.Add(new CommentEntry(comment.Id, comment.Author, comment.Date, comment.Text));
            }

            HasNoComments = Comments.Count == 0;
            SelectedComment = Comments.FirstOrDefault(c => c.Id == selectedId);
        }
        finally
        {
            _syncingComments = false;
        }
    }

    /// <summary>Attaches a comment to the selection and shows the pane with it selected.</summary>
    public int AddComment()
    {
        int id = Session.AddComment(AuthorName());
        ShowComments = true;
        RefreshComments();
        SelectedComment = Comments.FirstOrDefault(c => c.Id == id);
        return id;
    }

    public void DeleteComment(int id)
    {
        Session.DeleteComment(id);
        RefreshComments();
    }

    public void SaveCommentText(CommentEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        Session.SetCommentText(entry.Id, entry.Text);
    }

    // ----- Navigation pane -----


    public ObservableCollection<HeadingEntry> Headings { get; } = [];

    private readonly DispatcherTimer _headingsTimer = new(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(400) };
    private bool _syncingHeading;

    /// <summary>True while <see cref="CurrentHeading"/> is being set to follow the caret, so the list does not navigate back.</summary>
    public bool IsSyncingHeading => _syncingHeading;

    /// <summary>Check spelling as you type (Windows Spell Checking API).</summary>
    [ObservableProperty]
    public partial bool CheckSpelling { get; set; } = true;

    partial void OnCheckSpellingChanged(bool value)
    {
        _settings.CheckSpelling = value;
        _settings.Save();
    }

    [ObservableProperty]
    public partial bool ShowRuler { get; set; } = true;

    partial void OnShowRulerChanged(bool value)
    {
        _settings.ShowRuler = value;
        _settings.Save();
    }

    /// <summary>Word's "show tabs only": the ribbon content is hidden until a tab is clicked (Ctrl+F1).</summary>
    [ObservableProperty]
    public partial bool IsRibbonCollapsed { get; set; }

    partial void OnIsRibbonCollapsedChanged(bool value)
    {
        _settings.RibbonCollapsed = value;
        _settings.Save();
    }


    [ObservableProperty]
    public partial bool ShowNavigation { get; set; }



    [ObservableProperty]
    public partial bool HasNoHeadings { get; set; } = true;

    [ObservableProperty]
    public partial HeadingEntry? CurrentHeading { get; set; }

    partial void OnShowNavigationChanged(bool value)
    {
        _settings.ShowNavigation = value;
        _settings.Save();
        if (value)
        {
            RefreshHeadings();
        }
    }

    /// <summary>Rebuilds the heading list from paragraphs with an outline level (Heading 1-3 and styles based on them).</summary>
    public void RefreshHeadings()
    {
        _headingsTimer.Stop();
        Headings.Clear();
        Document document = Session.Document;
        for (int s = 0; s < document.Sections.Count; s++)
        {
            foreach ((BlockPath path, Paragraph paragraph) in BlockWalker.Paragraphs(document.Sections[s].Body))
            {
                if (Session.Resolver.ResolveParagraph(paragraph).OutlineLevel is { } level && level < 9)
                {
                    Headings.Add(new HeadingEntry(paragraph.FlatText.Trim(), level, new TextPosition(StoryId.Body(s), path, 0)));
                }
            }
        }


        HasNoHeadings = Headings.Count == 0;
        UpdateCurrentHeading();
    }

    /// <summary>Highlights the heading the caret is under.</summary>
    public void UpdateCurrentHeading()
    {
        if (!ShowNavigation || Headings.Count == 0)
        {
            return;
        }

        TextPosition caret = Session.Selection.Active;
        HeadingEntry? best = null;
        foreach (HeadingEntry heading in Headings)
        {
            if (heading.Position.Story.SectionIndex < caret.Story.SectionIndex
                || (heading.Position.Story.SectionIndex == caret.Story.SectionIndex && heading.Position.Block.TopIndex <= caret.Block.TopIndex))
            {
                best = heading;
            }
            else
            {
                break;
            }
        }

        _syncingHeading = true;
        try
        {
            CurrentHeading = best;
        }
        finally
        {
            _syncingHeading = false;
        }
    }

    private void ScheduleHeadingsRefresh()
    {
        if (ShowNavigation)
        {
            _headingsTimer.Stop();
            _headingsTimer.Start();
        }
    }

    /// <summary>Saves and applies what the Options dialog returned.</summary>

    public void ApplyOptions(int autosaveMinutes, AutoCorrectSettings autoCorrect, string units, string? author = null)
    {
        ArgumentNullException.ThrowIfNull(autoCorrect);
        _settings.AutosaveMinutes = Math.Clamp(autosaveMinutes, 0, 120);
        _settings.AutoCorrect = autoCorrect;
        _settings.Units = units;
        if (!string.IsNullOrWhiteSpace(author))
        {
            _settings.Author = author;
            Session.Author = AuthorName();
        }

        _settings.Save();
        Session.AutoCorrect = autoCorrect.ToOptions();
        UnitPreference.Apply(units);
        _autosaveTimer.Stop();
        if (_settings.AutosaveMinutes > 0)
        {
            _autosaveTimer.Interval = TimeSpan.FromMinutes(_settings.AutosaveMinutes);
            _autosaveTimer.Start();
        }
    }

    [RelayCommand]
    private void ClearFormatting()

    {
        Session.ClearFormatting();
        RefreshFormatState();
    }

    /// <summary>Ctrl+Shift+C: remember the formatting at the caret for Ctrl+Shift+V.</summary>
    [RelayCommand]
    private void CopyFormat()
    {
        _copiedFormat = Session.CopyFormat();
        StatusMessage = "Formatting copied; Ctrl+Shift+V applies it";
    }

    [RelayCommand]
    private void PasteFormat()
    {
        if (_copiedFormat is { } sample)
        {
            Session.PasteFormat(sample);
            RefreshFormatState();
        }
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
            string extension = Path.GetExtension(path);
            if (extension.Equals(".rtf", StringComparison.OrdinalIgnoreCase) || extension.Equals(".txt", StringComparison.OrdinalIgnoreCase))
            {
                OpenConverted(path, extension);
                return;
            }

            if (extension.Equals(".dotx", StringComparison.OrdinalIgnoreCase))
            {
                NewFromTemplateFile(path); // opening a template starts a new document from it, like Word
                return;
            }



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

    /// <summary>File > New from Template: pick a .dotx and start an untitled document with its content and styles.</summary>
    [RelayCommand]
    private void NewFromTemplate()
    {
        if (!ConfirmDiscard())
        {
            return;
        }

        var dialog = new OpenFileDialog { Filter = "Word Templates (*.dotx)|*.dotx|All files (*.*)|*.*", DefaultExt = ".dotx" };
        if (dialog.ShowDialog() == true)
        {
            try
            {
                NewFromTemplateFile(dialog.FileName);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or FileFormatException or DocumentFormat.OpenXml.Packaging.OpenXmlPackageException)
            {
                MessageBox.Show($"Could not open {dialog.FileName}.\n\n{ex.Message}", "Quill", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    /// <summary>Shows a compare result (or any generated document) as a new, unsaved document in this window.</summary>
    public void ShowGeneratedDocument(Document document, string name, string status)
    {
        ArgumentNullException.ThrowIfNull(document);
        Session.LoadDocument(document, isDirty: true);
        _documentPath = null;
        _documentName = name;
        _loadWasLossy = false;
        StartNewRecoverySession();
        StatusMessage = status;
        UpdateTitle();
    }

    private void NewFromTemplateFile(string path)
    {
        LoadResult result = DocxReader.ReadFile(path);
        Session.LoadDocument(result.Document);
        _documentPath = null;
        _documentName = "Document1";
        _loadWasLossy = result.HasLossyContent;
        StartNewRecoverySession();
        StatusMessage = "New document from template " + Path.GetFileName(path) + (result.HasLossyContent ? " (with limitations: " + string.Join(" ", result.Warnings.Select(w => w.Message)) + ")" : string.Empty);
        UpdateTitle();
    }

    /// <summary>Imports RTF or plain text into a new document; Save As keeps it as .docx.</summary>

    private void OpenConverted(string path, string extension)
    {
        string text = File.ReadAllText(path);
        DocumentFragment fragment = extension.Equals(".rtf", StringComparison.OrdinalIgnoreCase)
            ? Quill.Layout.Wpf.RichTextConverter.FromRtf(text)
            : DocumentFragment.FromPlainText(text);
        Document document = Document.CreateNew(metricPaper: IsMetricRegion());
        if (!fragment.IsEmpty)
        {
            document = DocumentEditor.InsertFragment(document, TextPosition.StartOf(StoryId.Body(0)), fragment).Document;
        }

        Session.LoadDocument(document, isDirty: true);
        _documentPath = null;
        _documentName = Path.GetFileNameWithoutExtension(path);
        _loadWasLossy = false;
        StartNewRecoverySession();
        RememberFile(path);
        StatusMessage = "Imported " + Path.GetFileName(path) + "; use Save As to keep it as a .docx";
        UpdateTitle();
    }

    /// <summary>All body paragraphs (table cells flattened), for RTF and text export.</summary>
    private DocumentFragment WholeDocumentFragment()
    {
        var paragraphs = System.Collections.Immutable.ImmutableArray.CreateBuilder<Paragraph>();
        foreach (Section section in Session.Document.Sections)
        {
            foreach (Block block in section.Body)
            {
                switch (block)
                {
                    case Paragraph paragraph:
                        paragraphs.Add(paragraph);
                        break;
                    case Table table:
                        paragraphs.AddRange(table.AllParagraphs());
                        break;
                }
            }
        }

        return new DocumentFragment(paragraphs.ToImmutable());
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
            Filter = SaveFilter,
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
            string extension = Path.GetExtension(path);
            if (extension.Equals(".rtf", StringComparison.OrdinalIgnoreCase))
            {
                File.WriteAllText(path, Quill.Layout.Wpf.RichTextConverter.ToRtf(WholeDocumentFragment(), Session.Resolver, Session.Document.Lists), System.Text.Encoding.ASCII);
                StatusMessage = "Exported " + Path.GetFileName(path) + " (the open document is still the .docx)";
                return;
            }

            if (extension.Equals(".txt", StringComparison.OrdinalIgnoreCase))
            {
                File.WriteAllText(path, WholeDocumentFragment().ToPlainText().Replace("\n", Environment.NewLine, StringComparison.Ordinal), System.Text.Encoding.UTF8);
                StatusMessage = "Exported " + Path.GetFileName(path) + " (the open document is still the .docx)";
                return;
            }

            DocxWriter.WriteFile(Session.Document, path);

            Session.MarkSaved();
            _documentPath = path;
            _documentName = Path.GetFileNameWithoutExtension(path);
            _loadWasLossy = false;
            RecoveryStore.Delete(_recoveryId);
            _lastAutosaved = Session.Document;
            RememberFile(path);
            StatusMessage = extension.Equals(".dotx", StringComparison.OrdinalIgnoreCase) ? $"Saved template {Path.GetFileName(path)}; File > New from Template starts documents from it" : $"Saved {Path.GetFileName(path)}";
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
            if (PrintService.Print(Session.Document, Session.Resolver, _documentName, CurrentPage))
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
    private void ToggleFormattingMarks() => ShowFormattingMarks = !ShowFormattingMarks;

    [RelayCommand]
    private void ToggleBullets() => IsBulleted = !IsBulleted;

    [RelayCommand]
    private void ResetParagraph()
    {
        Session.ResetParagraphFormatting();
        RefreshFormatState();
    }

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

    partial void OnCurrentStyleChanged(StyleEntry? value)
    {
        if (!_syncingFormat && value is not null)
        {
            Session.SetParagraphStyle(value.Id);
        }
    }

    /// <summary>Name shown in the style's own font, weight, slant and colour, with the size clamped so the list stays readable.</summary>
    private StyleEntry ToStyleEntry(Style style)
    {
        ResolvedRunProperties run = Session.ResolveStyleRun(style.Id);
        Brush? foreground = null;
        if (run.Color is { IsAuto: false } color)
        {
            var brush = new SolidColorBrush(Color.FromRgb(color.R, color.G, color.B));
            brush.Freeze();
            foreground = brush;
        }

        return new StyleEntry(style, run.FontFamily, Math.Clamp(run.FontSize.ToPoints() * 96 / 72, 12, 24), run.Bold ? FontWeights.SemiBold : FontWeights.Normal, run.Italic ? FontStyles.Italic : FontStyles.Normal, foreground);
    }

    private StyleSheet? _knownStyles;


    /// <summary>Rebuilds the style list when the document's style sheet changes (open, Modify Style).</summary>
    public void RefreshStyles()
    {
        StyleSheet styles = Session.Document.Styles;
        _knownStyles = styles;
        _syncingFormat = true;
        try
        {
            ParagraphStyles.Clear();
            foreach (Style style in styles.ParagraphStyles.Where(s => s.QuickFormat).OrderBy(s => s.Priority).ThenBy(s => s.Name, StringComparer.CurrentCultureIgnoreCase))
            {
                ParagraphStyles.Add(ToStyleEntry(style));
            }
        }
        finally
        {
            _syncingFormat = false;
        }

        RefreshFormatState();
    }

    private void OnDocumentChanged()
    {
        CanUndo = Session.CanUndo;
        CanRedo = Session.CanRedo;
        if (!ReferenceEquals(_knownStyles, Session.Document.Styles))
        {
            RefreshStyles();
        }

        WordCount = CountWords(Session.Document);
        ScheduleHeadingsRefresh();
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
            ParagraphShadingSwatch = paragraph.Shading is { IsAuto: false } paragraphShading ? Color.FromRgb(paragraphShading.R, paragraphShading.G, paragraphShading.B) : Colors.White;
            LineSpacingValue = paragraph.LineSpacing.Rule == LineSpacingRule.Auto ? Math.Round(paragraph.LineSpacing.Factor, 2) : 0;
            SpaceBeforePoints = Math.Round(paragraph.SpaceBefore.ToPoints(), 1);
            SpaceAfterPoints = Math.Round(paragraph.SpaceAfter.ToPoints(), 1);
            DocColor color = caret.Color;
            FontColor = color.IsAuto ? Colors.Black : Color.FromRgb(color.R, color.G, color.B);
            HighlightSwatch = Quill.Layout.Wpf.FontCatalog.Shared.GetHighlightBrush(caret.Highlight)?.Color ?? Colors.Yellow;
            Paragraph current = Session.Document.GetParagraph(Session.Selection.Active);
            CurrentStyle = ParagraphStyles.FirstOrDefault(s => s.Id == (current.StyleId ?? Session.Document.Styles.DefaultParagraphStyleId));
            bool? listKind = Session.ListKind(current);
            IsPictureSelected = !Session.Selection.IsCollapsed && Session.SelectedImage() is not null;
            IsInTable = Session.IsInTable;
            IsInHeaderFooter = Session.IsInHeaderFooter;
            if (IsInHeaderFooter)
            {
                int pageSection = HeaderFooterSection;
                DifferentFirstPage = Session.HasDifferentFirstPage(pageSection);
                DifferentOddEven = Session.HasDifferentOddEven;
                CanLinkToPrevious = pageSection > 0;
                IsLinkedToPrevious = Session.IsLinkedToPrevious(pageSection);
            }

            IsHeaderRow = Session.IsHeaderRowAtCaret;
            PageColorSwatch = Session.PageColor is { IsAuto: false } pageColor ? Color.FromRgb(pageColor.R, pageColor.G, pageColor.B) : Colors.White;
            CellShadingSwatch = Session.CellShadingAtCaret is { IsAuto: false } shading ? Color.FromRgb(shading.R, shading.G, shading.B) : Colors.White;
            TrackChanges = Session.TrackChanges;
            RevisionView = Session.RevisionView;
            UpdateCurrentHeading();
            UpdateWordCountText();
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

/// <summary>A heading shown in the navigation pane.</summary>
public sealed record HeadingEntry(string Text, int Level, TextPosition Position)
{
    public string Display => Text.Length == 0 ? "(empty heading)" : Text;

    public Thickness Margin => new(8 + Level * 14, 2, 4, 2);
}

/// <summary>A comment shown in the comments pane; <see cref="Text"/> is edited in place.</summary>
public sealed partial class CommentEntry(int id, string author, DateTimeOffset? date, string text) : ObservableObject
{
    public int Id { get; } = id;

    public string Author { get; } = author;

    public string DateText { get; } = date is { } when ? when.ToLocalTime().ToString("g", System.Globalization.CultureInfo.CurrentCulture) : string.Empty;

    [ObservableProperty]
    public partial string Text { get; set; } = text;
}

/// <summary>A paragraph style in the Styles gallery with how its name should look there (Word shows each style in its own formatting).</summary>
public sealed record StyleEntry(Style Style, string FontFamily, double FontSize, FontWeight FontWeight, FontStyle FontStyle, Brush? Foreground)
{
    public string Id => Style.Id;

    public string Name => Style.Name;

    public bool HasColor => Foreground is not null;
}

/// <summary>One match in the navigation pane's Results list: where it is and the text around it.</summary>
public sealed record SearchResultEntry(TextRange Range, string Context);

/// <summary>One page in the navigation pane's Pages tab.</summary>
public sealed record PageThumbnail(int Number, ImageSource Image, string Label);
