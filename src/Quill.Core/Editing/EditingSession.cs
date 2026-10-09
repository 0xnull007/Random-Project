using System.Collections.Immutable;
using Quill.Core.Model;
using Quill.Core.Styles;
using Quill.Core.Text;

namespace Quill.Core.Editing;

public sealed class DocumentChangedEventArgs(Document oldDocument, Document newDocument, ChangeSet change) : EventArgs
{
    public Document OldDocument { get; } = oldDocument;

    public Document NewDocument { get; } = newDocument;

    public ChangeSet Change { get; } = change;
}

/// <summary>
/// The stateful, UI-agnostic editor: current document, selection, sticky "pending" character format,
/// undo/redo and dirty tracking. Views call these methods from input handlers; layout-dependent
/// movement (lines, pages) computes a <see cref="TextPosition"/> and calls <see cref="MoveCaret"/>.
/// </summary>
public sealed class EditingSession
{
    private readonly UndoStack _undo;
    private StyleResolver _resolver;
    private Document _savedDocument;

    public EditingSession(Document document, UndoStack? undoStack = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        Document = document;
        _savedDocument = document;
        _undo = undoStack ?? new UndoStack();
        _resolver = new StyleResolver(document.Styles);
        Selection = Selection.Caret(TextNavigation.StoryStart(document, StoryId.Body(0)));
    }

    public event EventHandler<DocumentChangedEventArgs>? DocumentChanged;

    public event EventHandler? SelectionChanged;

    public Document Document { get; private set; }

    public Selection Selection { get; private set; }

    public StyleResolver Resolver => _resolver;

    /// <summary>Character formatting queued by a toggle with a collapsed selection; applied to the next typed text.</summary>
    public RunProperties? PendingFormat { get; private set; }

    public bool IsDirty => !ReferenceEquals(Document, _savedDocument);

    public bool CanUndo => _undo.CanUndo;

    public bool CanRedo => _undo.CanRedo;

    public void MarkSaved() => _savedDocument = Document;

    /// <summary>Replaces everything (open file / new document) and resets undo history.</summary>
    public void LoadDocument(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);
        _undo.Clear();
        PendingFormat = null;
        _savedDocument = document;
        ApplyState(document, Selection.Caret(TextNavigation.StoryStart(document, StoryId.Body(0))), ChangeSet.Structural());
    }

    // ----- Selection -----

    public void SetSelection(Selection selection)
    {
        if (!Document.IsValid(selection.Anchor) || !Document.IsValid(selection.Active))
        {
            throw new ArgumentException($"Selection {selection} is not valid for the current document.", nameof(selection));
        }

        if (selection == Selection)
        {
            return;
        }

        Selection = selection;
        PendingFormat = null;
        _undo.BreakCoalescing();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    public void MoveCaret(TextPosition position, bool extend, CaretAffinity affinity = CaretAffinity.Downstream) =>
        SetSelection(extend ? Selection.WithActive(position, affinity) : Selection.Caret(position, affinity));

    public void SelectAll() => SetSelection(ToSelection(TextNavigation.WholeStory(Document, Selection.Story)));

    public void SelectWordAt(TextPosition position) => SetSelection(ToSelection(TextNavigation.WordAt(Document, position)));

    public void SelectParagraphAt(TextPosition position) => SetSelection(ToSelection(TextNavigation.ParagraphAt(Document, position)));

    // ----- Typing and deleting -----

    /// <summary>Types text (no line separators), replacing the selection and honoring the pending format.</summary>
    public void InsertText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length == 0)
        {
            return;
        }

        (string? styleId, RunProperties properties) = TypingFormat(Document, Selection);
        if (PendingFormat is not null)
        {
            properties = properties.Merge(PendingFormat);
        }

        (Document working, TextPosition at, ChangeSet? deletion) = DeleteSelectionIfAny();
        bool startsNewGroup = deletion is not null;
        if (!startsNewGroup && at.Offset > 0 && !char.IsWhiteSpace(text[0]))
        {
            // Undo by word: the first non-space after a space starts a new undo step.
            string flat = working.GetParagraph(at).FlatText;
            startsNewGroup = char.IsWhiteSpace(flat[at.Offset - 1]);
        }

        EditResult result = DocumentEditor.InsertText(working, at, text, properties, styleId);
        Commit(WithDeletion(result, deletion), EditKind.Typing, startsNewGroup);
        PendingFormat = null;
    }

    /// <summary>Inserts text that may contain line separators: single-line text is typed, multi-line text is pasted as paragraphs.</summary>
    public void InsertPlainText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.AsSpan().IndexOfAny('\r', '\n') < 0)
        {
            InsertText(text);
            return;
        }

        (string? styleId, RunProperties properties) = TypingFormat(Document, Selection);
        if (PendingFormat is not null)
        {
            properties = properties.Merge(PendingFormat);
        }

        DocumentFragment fragment = DocumentFragment.FromPlainText(text, properties);
        if (styleId is not null)
        {
            fragment = new DocumentFragment(fragment.Paragraphs.Select(p => p.WithInlines(p.Inlines.Select(i => i.WithStyle(styleId)).ToImmutableArray())).ToImmutableArray());
        }

        Paste(fragment);
    }

    /// <summary>Enter.</summary>
    public void InsertParagraphBreak()
    {
        (Document working, TextPosition at, ChangeSet? deletion) = DeleteSelectionIfAny();
        EditResult result = DocumentEditor.SplitParagraph(working, at, _resolver);
        Commit(WithDeletion(result, deletion), EditKind.Other, startsNewGroup: true);
        PendingFormat = null;
    }

    /// <summary>Shift+Enter (line), Ctrl+Enter (page).</summary>
    public void InsertBreak(BreakKind kind) => InsertInline(new Break(kind));

    public void InsertInline(Inline inline)
    {
        ArgumentNullException.ThrowIfNull(inline);
        (Document working, TextPosition at, ChangeSet? deletion) = DeleteSelectionIfAny();
        EditResult result = DocumentEditor.InsertInline(working, at, inline);
        Commit(WithDeletion(result, deletion), EditKind.Other, startsNewGroup: true);
        PendingFormat = null;
    }

    public void Backspace()
    {
        if (!Selection.IsCollapsed)
        {
            DeleteSelection(EditKind.Backspace);
            return;
        }

        TextPosition position = Selection.Active;
        TextPosition? previous = TextNavigation.PreviousCharacter(Document, position);
        if (previous is null || !AreAdjacentParagraphs(previous.Value, position))
        {
            return;
        }

        bool joinsParagraphs = previous.Value.Block != position.Block;
        Commit(DocumentEditor.DeleteRange(Document, new TextRange(previous.Value, position)), EditKind.Backspace, joinsParagraphs);
    }

    public void Delete()
    {
        if (!Selection.IsCollapsed)
        {
            DeleteSelection(EditKind.ForwardDelete);
            return;
        }

        TextPosition position = Selection.Active;
        TextPosition? next = TextNavigation.NextCharacter(Document, position);
        if (next is null || !AreAdjacentParagraphs(position, next.Value))
        {
            return;
        }

        bool joinsParagraphs = next.Value.Block != position.Block;
        Commit(DocumentEditor.DeleteRange(Document, new TextRange(position, next.Value)), EditKind.ForwardDelete, joinsParagraphs);
    }

    public void DeleteWordBackward() => DeleteTo(TextNavigation.PreviousWord(Document, Selection.Active));

    public void DeleteWordForward() => DeleteTo(TextNavigation.NextWord(Document, Selection.Active));

    public void DeleteSelection(EditKind kind = EditKind.Other)
    {
        if (Selection.IsCollapsed)
        {
            return;
        }

        Commit(DocumentEditor.DeleteRange(Document, Selection.Range), kind, startsNewGroup: true);
        PendingFormat = null;
    }

    // ----- Formatting -----

    /// <summary>Merges character formatting into the selection, or queues it for the next typed text when collapsed.</summary>
    public void ApplyRunFormat(RunProperties delta)
    {
        ArgumentNullException.ThrowIfNull(delta);
        if (Selection.IsCollapsed)
        {
            Paragraph paragraph = Document.GetParagraph(Selection.Active);
            if (paragraph.IsEmpty)
            {
                Commit(DocumentEditor.ApplyParagraphMarkFormat(Document, Selection.Active, delta), EditKind.Formatting, startsNewGroup: true);
            }
            else
            {
                PendingFormat = (PendingFormat ?? RunProperties.Empty).Merge(delta);
            }

            return;
        }

        EditResult result = DocumentEditor.ApplyRunFormat(Document, Selection.Range, delta);
        Commit(result with { Selection = Selection }, EditKind.Formatting, startsNewGroup: true);
    }

    public void ApplyParagraphFormat(ParagraphProperties delta)
    {
        ArgumentNullException.ThrowIfNull(delta);
        EditResult result = DocumentEditor.ApplyParagraphFormat(Document, Selection.Range, delta);
        Commit(result with { Selection = Selection }, EditKind.Formatting, startsNewGroup: true);
    }

    public void SetParagraphStyle(string? paragraphStyleId)
    {
        EditResult result = DocumentEditor.SetParagraphStyle(Document, Selection.Range, paragraphStyleId);
        Commit(result with { Selection = Selection }, EditKind.Formatting, startsNewGroup: true);
    }

    public void ToggleBold() => ToggleRunProperty(static r => r.Bold, static on => new RunProperties { Bold = on });

    public void ToggleItalic() => ToggleRunProperty(static r => r.Italic, static on => new RunProperties { Italic = on });

    public void ToggleUnderline() => ToggleRunProperty(
        static r => r.Underline != UnderlineStyle.None,
        static on => new RunProperties { Underline = on ? UnderlineStyle.Single : UnderlineStyle.None });

    /// <summary>Resolved character formats of everything selected (or the caret format when collapsed); drives toolbar toggle state.</summary>
    public IEnumerable<ResolvedRunProperties> SelectionFormats()
    {
        if (Selection.IsCollapsed)
        {
            yield return CaretFormat();
            yield break;
        }

        TextRange range = Selection.Range;
        ImmutableList<Block> blocks = Document.GetStory(range.Story);
        int firstIndex = range.Start.Block.TopIndex;
        int lastIndex = range.End.Block.TopIndex;
        for (int i = firstIndex; i <= lastIndex; i++)
        {
            if (blocks[i] is not Paragraph paragraph)
            {
                continue;
            }

            int start = i == firstIndex ? range.Start.Offset : 0;
            int end = i == lastIndex ? range.End.Offset : paragraph.Length;
            bool any = false;
            foreach (InlineSpan span in paragraph.Spans())
            {
                if (span.End > start && span.Start < end)
                {
                    any = true;
                    yield return _resolver.ResolveRun(paragraph, span.Inline);
                }
            }

            if (!any || i < lastIndex)
            {
                yield return _resolver.ResolveParagraphMark(paragraph);
            }
        }
    }

    /// <summary>The format newly typed text would get.</summary>
    public ResolvedRunProperties CaretFormat()
    {
        (string? styleId, RunProperties properties) = TypingFormat(Document, Selection);
        if (PendingFormat is not null)
        {
            properties = properties.Merge(PendingFormat);
        }

        Paragraph paragraph = Document.GetParagraph(Selection.Active);
        return _resolver.ResolveRun(paragraph.StyleId, styleId, properties);
    }

    public ResolvedParagraphProperties CaretParagraphFormat() => _resolver.ResolveParagraph(Document.GetParagraph(Selection.Active));

    // ----- Clipboard -----

    public DocumentFragment Copy() => DocumentEditor.ExtractFragment(Document, Selection.Range);

    public DocumentFragment Cut()
    {
        DocumentFragment fragment = Copy();
        DeleteSelection();
        return fragment;
    }

    public void Paste(DocumentFragment fragment)
    {
        ArgumentNullException.ThrowIfNull(fragment);
        if (fragment.IsEmpty)
        {
            return;
        }

        (Document working, TextPosition at, ChangeSet? deletion) = DeleteSelectionIfAny();
        EditResult result = DocumentEditor.InsertFragment(working, at, fragment);
        Commit(WithDeletion(result, deletion), EditKind.Paste, startsNewGroup: true);
        PendingFormat = null;
    }

    // ----- Undo -----

    public bool Undo()
    {
        EditRecord? record = _undo.Undo();
        if (record is null)
        {
            return false;
        }

        PendingFormat = null;
        ApplyState(record.Before, record.SelectionBefore, record.Change);
        return true;
    }

    public bool Redo()
    {
        EditRecord? record = _undo.Redo();
        if (record is null)
        {
            return false;
        }

        PendingFormat = null;
        ApplyState(record.After, record.SelectionAfter, record.Change);
        return true;
    }

    // ----- Structure -----

    public void InsertSectionBreak(SectionStart start)
    {
        (Document working, TextPosition at, ChangeSet? deletion) = DeleteSelectionIfAny();
        EditResult result = DocumentEditor.InsertSectionBreak(working, at, start, _resolver);
        Commit(WithDeletion(result, deletion), EditKind.Other, startsNewGroup: true);
    }

    public void SetSectionProperties(int sectionIndex, SectionProperties properties) =>
        Commit(DocumentEditor.SetSectionProperties(Document, sectionIndex, properties, Selection), EditKind.Other, startsNewGroup: true);

    /// <summary>Replaces the document as one undoable step (style sheet edits, metadata, external tools).</summary>
    public void ReplaceDocument(Document document, Selection? selection = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        Selection target = selection ?? Selection;
        Commit(new EditResult(document, target, ChangeSet.Structural()), EditKind.Other, startsNewGroup: true);
    }

    /// <summary>Creates an empty header/footer story so it can be edited; no-op when it exists.</summary>
    public void EnsureStory(StoryId story)
    {
        if (Document.TryGetStory(story) is not null)
        {
            return;
        }

        string? styleId = story.IsHeader ? DefaultStyleSheet.HeaderId : story.IsFooter ? DefaultStyleSheet.FooterId : null;
        if (styleId is not null && !Document.Styles.Contains(styleId))
        {
            styleId = null;
        }

        ImmutableList<Block> blocks = ImmutableList.Create<Block>(Paragraph.Empty(styleId));
        Commit(new EditResult(Document.WithStory(story, blocks), Selection, ChangeSet.Structural()), EditKind.Other, startsNewGroup: true);
    }

    // ----- Internals -----

    private static Selection ToSelection(TextRange range) => new(range.Start, range.End);

    private static (string? StyleId, RunProperties Properties) TypingFormat(Document document, Selection selection)
    {
        Paragraph paragraph = document.GetParagraph(selection.Start);
        if (!selection.IsCollapsed && paragraph.TryGetInlineAt(selection.Start.Offset, out InlineSpan span))
        {
            return (span.Inline.StyleId, span.Inline.Properties);
        }

        return paragraph.GetTypingFormatAt(selection.Start.Offset);
    }

    private bool AreAdjacentParagraphs(TextPosition earlier, TextPosition later) =>
        later.Block.TopIndex - earlier.Block.TopIndex <= 1;

    private void DeleteTo(TextPosition? target)
    {
        if (!Selection.IsCollapsed)
        {
            DeleteSelection();
            return;
        }

        if (target is null || !AreAdjacentParagraphs(
                target.Value < Selection.Active ? target.Value : Selection.Active,
                target.Value < Selection.Active ? Selection.Active : target.Value))
        {
            return;
        }

        Commit(DocumentEditor.DeleteRange(Document, new TextRange(Selection.Active, target.Value)), EditKind.Other, startsNewGroup: true);
    }

    private (Document Document, TextPosition At, ChangeSet? Deletion) DeleteSelectionIfAny()
    {
        if (Selection.IsCollapsed)
        {
            return (Document, Selection.Active, null);
        }

        EditResult deleted = DocumentEditor.DeleteRange(Document, Selection.Range);
        return (deleted.Document, deleted.Selection.Active, deleted.Change);
    }

    private static EditResult WithDeletion(EditResult result, ChangeSet? deletion) =>
        deletion is null ? result : result with { Change = result.Change.Union(deletion.Value) };

    private void ToggleRunProperty(Func<ResolvedRunProperties, bool> isOn, Func<bool, RunProperties> make)
    {
        bool allOn = SelectionFormats().All(isOn);
        ApplyRunFormat(make(!allOn));
    }

    private void Commit(EditResult result, EditKind kind, bool startsNewGroup = false)
    {
        if (result.IsNoOp)
        {
            return;
        }

        var record = new EditRecord(Document, Selection, result.Document, result.Selection, result.Change, kind, _undo.Now(), startsNewGroup);
        _undo.Push(record);
        ApplyState(result.Document, result.Selection, result.Change);
    }

    private void ApplyState(Document document, Selection selection, ChangeSet change)
    {
        Document old = Document;
        Document = document;
        if (!ReferenceEquals(old.Styles, document.Styles))
        {
            _resolver = new StyleResolver(document.Styles);
        }

        bool selectionChanged = selection != Selection;
        Selection = selection;
        DocumentChanged?.Invoke(this, new DocumentChangedEventArgs(old, document, change));
        if (selectionChanged)
        {
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
