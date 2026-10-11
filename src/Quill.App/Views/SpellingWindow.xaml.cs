using System.ComponentModel;
using System.Windows;
using Run = System.Windows.Documents.Run;
using System.Windows.Media;
using Quill.App.Spelling;
using Quill.Core.Editing;
using Quill.Core.Model;
using Quill.Core.Text;

namespace Quill.App.Views;

/// <summary>
/// Word's Spelling dialog (F7): walks the misspellings from the caret onwards, shows each in its sentence with
/// suggestions, and offers Ignore Once / Ignore All / Add to Dictionary / Change / Change All.
/// </summary>
public partial class SpellingWindow : Window
{
    private readonly EditingSession _session;
    private bool _allowClose;
    private TextPosition? _startedAt;
    private (TextRange Range, string Word, string LanguageTag)? _current;

    public SpellingWindow(EditingSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        InitializeComponent();
        _session = session;
        Closing += OnClosing;
    }

    /// <summary>The editor to scroll; set by the window.</summary>
    public DocumentView? Editor { get; set; }

    /// <summary>Starts (or restarts) checking from the caret.</summary>
    public void Start()
    {
        _startedAt = _session.Selection.Active;
        Show();
        Activate();
        if (!SpellService.Shared.IsAvailable)
        {
            ShowDone("Spell checking is not available: Windows has no dictionary for this language.");
            return;
        }

        FindNext(_session.Selection.Active);
    }

    public void ForceClose()
    {
        _allowClose = true;
        Close();
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (!_allowClose)
        {
            e.Cancel = true;
            Hide();
        }
    }

    /// <summary>The next misspelling at or after <paramref name="from"/>, wrapping once to the start of the document.</summary>
    private void FindNext(TextPosition from)
    {
        Document document = _session.Document;
        List<(StoryId Story, BlockPath Path, Paragraph Paragraph)> paragraphs = DocumentEditor.AllParagraphs(document).ToList();
        int startIndex = paragraphs.FindIndex(p => p.Story == from.Story && p.Path == from.Block);
        if (startIndex < 0)
        {
            startIndex = 0;
        }

        for (int step = 0; step <= paragraphs.Count; step++)
        {
            int index = (startIndex + step) % paragraphs.Count;
            (StoryId story, BlockPath path, Paragraph paragraph) = paragraphs[index];
            int minOffset = step == 0 ? from.Offset : 0;
            if (step == paragraphs.Count && _startedAt is { } origin && origin.Story == story && origin.Block == path)
            {
                minOffset = 0; // back where we started: check the part of the first paragraph before the caret
            }

            ParagraphSpelling spelling = SpellService.Shared.Check(paragraph, _session.Resolver);
            foreach (Misspelling error in spelling.Errors)
            {
                if (error.Start < minOffset || (step == paragraphs.Count && _startedAt is { } o && o.Block == path && error.Start >= o.Offset))
                {
                    continue;
                }

                var range = new TextRange(new TextPosition(story, path, error.Start), new TextPosition(story, path, error.End));
                Present(range, error.Word, spelling.LanguageTag, paragraph);
                return;
            }

            if (step == paragraphs.Count)
            {
                break;
            }
        }

        ShowDone("Spelling check complete.");
    }

    private void Present(TextRange range, string word, string languageTag, Paragraph paragraph)
    {
        _current = (range, word, languageTag);
        _session.MoveCaret(range.Start, extend: false);
        _session.MoveCaret(range.End, extend: true);
        string text = paragraph.FlatText;
        int from = Math.Max(0, range.Start.Offset - 80);
        int to = Math.Min(text.Length, range.End.Offset + 80);
        ContextText.Inlines.Clear();
        ContextText.Inlines.Add(new Run((from > 0 ? "…" : string.Empty) + text[from..range.Start.Offset]));
        ContextText.Inlines.Add(new Run(word) { FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Color.FromRgb(0xC0, 0x00, 0x00)) });
        ContextText.Inlines.Add(new Run(text[range.End.Offset..to] + (to < text.Length ? "…" : string.Empty)));

        SuggestionList.Items.Clear();
        foreach (string suggestion in SpellService.Shared.Suggest(word, languageTag))
        {
            SuggestionList.Items.Add(suggestion);
        }

        bool any = SuggestionList.Items.Count > 0;
        if (any)
        {
            SuggestionList.SelectedIndex = 0;
        }

        StatusText.Text = any ? string.Empty : "(No suggestions)";
        foreach (UIElement button in new UIElement[] { IgnoreButton, IgnoreAllButton, AddButton })
        {
            button.IsEnabled = true;
        }

        ChangeButton.IsEnabled = any;
        ChangeAllButton.IsEnabled = any;
    }

    private void ShowDone(string message)
    {
        _current = null;
        ContextText.Inlines.Clear();
        SuggestionList.Items.Clear();
        StatusText.Text = message;
        foreach (UIElement button in new UIElement[] { IgnoreButton, IgnoreAllButton, AddButton, ChangeButton, ChangeAllButton })
        {
            button.IsEnabled = false;
        }
    }

    private void OnIgnore(object sender, RoutedEventArgs e)
    {
        if (_current is { } current)
        {
            FindNext(current.Range.End);
        }
    }

    private void OnIgnoreAll(object sender, RoutedEventArgs e)
    {
        if (_current is { } current)
        {
            SpellService.Shared.Ignore(current.Word, current.LanguageTag);
            FindNext(current.Range.End);
        }
    }

    private void OnAdd(object sender, RoutedEventArgs e)
    {
        if (_current is { } current)
        {
            SpellService.Shared.Add(current.Word, current.LanguageTag);
            FindNext(current.Range.End);
        }
    }

    private void OnChange(object sender, RoutedEventArgs e)
    {
        if (_current is not { } current || SuggestionList.SelectedItem is not string replacement)
        {
            return;
        }

        _session.ReplaceRange(current.Range, replacement);
        FindNext(current.Range.Start.WithOffset(current.Range.Start.Offset + replacement.Length));
    }

    private void OnChangeAll(object sender, RoutedEventArgs e)
    {
        if (_current is not { } current || SuggestionList.SelectedItem is not string replacement)
        {
            return;
        }

        _session.ReplaceAll(current.Word, replacement, new SearchOptions(MatchCase: true, WholeWord: true));
        FindNext(current.Range.Start);
    }

    private void OnClose(object sender, RoutedEventArgs e)
    {
        Hide();
        Editor?.Focus();
    }
}
