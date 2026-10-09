using System.Collections.Immutable;
using Quill.Core.Model;
using Quill.Core.Styles;
using Quill.Core.Text;
using Quill.Core.Units;

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
        _resolver = new StyleResolver(document.Styles, document.Lists);
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

    /// <summary>Replaces everything (open file / new document) and resets undo history. A recovered document starts out dirty.</summary>
    public void LoadDocument(Document document, bool isDirty = false)
    {
        ArgumentNullException.ThrowIfNull(document);
        _undo.Clear();
        PendingFormat = null;
        _savedDocument = isDirty ? Document.CreateNew() : document;
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

    /// <summary>Adds the picture to the document and inserts it at the caret as one undo step.</summary>
    public void InsertImage(ImageData image, Twips width, Twips height)
    {
        ArgumentNullException.ThrowIfNull(image);
        (Document working, TextPosition at, ChangeSet? deletion) = DeleteSelectionIfAny();
        string id = working.Images.NewId();
        working = working.WithImages(working.Images.With(id, image));
        EditResult result = DocumentEditor.InsertInline(working, at, new InlineImage(id, width, height));
        Commit(WithDeletion(result, deletion), EditKind.Other, startsNewGroup: true);
        PendingFormat = null;
    }

    /// <summary>The picture the selection covers exactly, or the one touching a collapsed caret (before it first), if any.</summary>
    public (TextPosition Start, InlineImage Image)? SelectedImage()
    {
        TextRange range = Selection.Range;
        if (!range.IsWithinOneParagraph || Document.TryGetParagraph(range.Start) is not { } paragraph)
        {
            return null;
        }

        int length = range.End.Offset - range.Start.Offset;
        if (length == 1)
        {
            return ImageAt(paragraph, range.Start);
        }

        if (length != 0)
        {
            return null;
        }

        return ImageAt(paragraph, range.Start.WithOffset(range.Start.Offset - 1)) ?? ImageAt(paragraph, range.Start);
    }

    /// <summary>Changes the size of the picture <see cref="SelectedImage"/> finds and leaves it selected.</summary>
    public void ResizeImage(Twips width, Twips height)
    {
        if (SelectedImage() is not ({ } at, { } image) || (image.Width == width && image.Height == height))
        {
            return;
        }

        TextPosition end = at.WithOffset(at.Offset + 1);
        EditResult deleted = DocumentEditor.DeleteRange(Document, new TextRange(at, end));
        EditResult inserted = DocumentEditor.InsertInline(deleted.Document, at, image.WithSize(width, height));
        Commit(inserted with { Change = inserted.Change.Union(deleted.Change), Selection = new Selection(at, end) }, EditKind.Other, startsNewGroup: true);
    }

    private static (TextPosition Start, InlineImage Image)? ImageAt(Paragraph paragraph, TextPosition at) =>
        at.Offset >= 0 && paragraph.TryGetInlineAt(at.Offset, out InlineSpan span) && span.Inline is InlineImage image
            ? (at.WithOffset(span.Start), image)
            : null;

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

    /// <summary>
    /// Puts the selected paragraphs in a bullet or numbered list, or takes them out when they all already are in
    /// one of that kind. Joins an adjacent list of the same kind rather than starting a new one.
    /// </summary>
    public void ToggleList(bool bulleted)
    {
        TextRange range = Selection.Range;
        ImmutableList<Block> blocks = Document.GetStory(range.Story);
        int first = range.Start.Block.TopIndex;
        int last = range.End.Block.TopIndex;
        bool allAlready = true;
        for (int i = first; i <= last; i++)
        {
            if (blocks[i] is Paragraph paragraph && ListKind(paragraph) != bulleted)
            {
                allAlready = false;
            }
        }

        if (allAlready)
        {
            Commit(DocumentEditor.ApplyParagraphFormat(Document, range, new ParagraphProperties { List = ListFormat.None }) with { Selection = Selection }, EditKind.Formatting, startsNewGroup: true);
            return;
        }

        int? numberingId = NeighbourList(blocks, first - 1, bulleted) ?? NeighbourList(blocks, last + 1, bulleted);
        Document document = Document;
        if (numberingId is null)
        {
            (ListStore store, int id) = document.Lists.AddList(bulleted ? DefaultLists.BulletLevels() : DefaultLists.NumberedLevels());
            document = document.WithLists(store);
            numberingId = id;
        }

        EditResult result = DocumentEditor.ApplyParagraphFormat(document, range, new ParagraphProperties { List = new ListFormat(numberingId.Value, 0) });
        Commit(result with { Selection = Selection, Change = ChangeSet.Structural() }, EditKind.Formatting, startsNewGroup: true);
    }

    /// <summary>Moves list paragraphs one level deeper or shallower; non-list paragraphs get their left indent changed by half an inch.</summary>
    public void ChangeIndent(int delta)
    {
        TextRange range = Selection.Range;
        ImmutableList<Block> blocks = Document.GetStory(range.Story);
        Document document = Document;
        ImmutableList<Block>.Builder builder = blocks.ToBuilder();
        bool changed = false;
        for (int i = range.Start.Block.TopIndex; i <= range.End.Block.TopIndex; i++)
        {
            if (blocks[i] is not Paragraph paragraph)
            {
                continue;
            }

            ResolvedParagraphProperties resolved = _resolver.ResolveParagraph(paragraph);
            ParagraphProperties update;
            if (resolved.List is { IsNone: false } list)
            {
                int level = Math.Clamp(list.Level + delta, 0, ListDefinition.LevelCount - 1);
                if (level == list.Level)
                {
                    continue;
                }

                update = new ParagraphProperties { List = list with { Level = level } };
            }
            else
            {
                Twips left = Twips.Max(Twips.Zero, resolved.LeftIndent + Twips.FromInches(0.5 * delta));
                if (left == resolved.LeftIndent)
                {
                    continue;
                }

                update = new ParagraphProperties { LeftIndent = left };
            }

            builder[i] = paragraph.WithProperties(paragraph.Properties.Merge(update));
            changed = true;
        }

        if (changed)
        {
            Commit(new EditResult(document.WithStory(range.Story, builder.ToImmutable()), Selection, ChangeSet.From(range.Story, range.Start.Block.TopIndex)), EditKind.Formatting, startsNewGroup: true);
        }
    }

    /// <summary>True for bullet, false for numbered, null when the paragraph is not in a list.</summary>
    public bool? ListKind(Paragraph paragraph)
    {
        ArgumentNullException.ThrowIfNull(paragraph);
        ResolvedParagraphProperties resolved = _resolver.ResolveParagraph(paragraph);
        if (resolved.List is not { IsNone: false } list)
        {
            return null;
        }

        return Document.Lists.GetLevel(list.NumberingId, list.Level)?.IsBullet;
    }

    private int? NeighbourList(ImmutableList<Block> blocks, int index, bool bulleted)
    {
        if (index < 0 || index >= blocks.Count || blocks[index] is not Paragraph paragraph)
        {
            return null;
        }

        ResolvedParagraphProperties resolved = _resolver.ResolveParagraph(paragraph);
        return resolved.List is { IsNone: false } list && Document.Lists.GetLevel(list.NumberingId, list.Level)?.IsBullet == bulleted ? list.NumberingId : null;
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

    // ----- Find and replace -----

    /// <summary>Replaces a range with text that takes the formatting of the first replaced character; the new text is selected afterwards.</summary>
    public void ReplaceRange(TextRange range, string replacement)
    {
        ArgumentNullException.ThrowIfNull(replacement);
        if (!Document.IsValid(range.Start) || !Document.IsValid(range.End))
        {
            return;
        }

        (string? styleId, RunProperties properties) = TypingFormat(Document, new Selection(range.Start, range.End));
        EditResult deleted = DocumentEditor.DeleteRange(Document, range);
        Document document = deleted.Document;
        ChangeSet change = deleted.Change;
        if (replacement.Length > 0)
        {
            EditResult inserted = DocumentEditor.InsertText(document, range.Start, replacement, properties, styleId);
            document = inserted.Document;
            change = change.Union(inserted.Change);
        }

        Selection selection = replacement.Length > 0
            ? new Selection(range.Start, range.Start.WithOffset(range.Start.Offset + replacement.Length))
            : Selection.Caret(range.Start);
        Commit(new EditResult(document, selection, change), EditKind.Other, startsNewGroup: true);
        PendingFormat = null;
    }

    /// <summary>Replaces every match as one undo step and returns how many were replaced.</summary>
    public int ReplaceAll(string query, string replacement, SearchOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(replacement);
        IReadOnlyList<TextRange> matches = TextSearch.FindAll(Document, query, options);
        if (matches.Count == 0)
        {
            return 0;
        }

        // Replace from the last match backwards so earlier offsets stay valid.
        Document document = Document;
        for (int i = matches.Count - 1; i >= 0; i--)
        {
            TextRange match = matches[i];
            (string? styleId, RunProperties properties) = TypingFormat(document, new Selection(match.Start, match.End));
            document = DocumentEditor.DeleteRange(document, match).Document;
            if (replacement.Length > 0)
            {
                document = DocumentEditor.InsertText(document, match.Start, replacement, properties, styleId).Document;
            }
        }

        Selection selection = document.IsValid(Selection.Anchor) && document.IsValid(Selection.Active)
            ? Selection
            : Selection.Caret(matches[0].Start);
        Commit(new EditResult(document, selection, ChangeSet.Structural()), EditKind.Other, startsNewGroup: true);
        PendingFormat = null;
        return matches.Count;
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
        if (!ReferenceEquals(old.Styles, document.Styles) || !ReferenceEquals(old.Lists, document.Lists))
        {
            _resolver = new StyleResolver(document.Styles, document.Lists);
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
