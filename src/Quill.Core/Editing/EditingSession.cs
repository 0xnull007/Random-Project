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
public sealed partial class EditingSession
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

    /// <summary>How tracked changes are displayed (Word's Display for Review); not saved with the document.</summary>
    public RevisionView RevisionView { get; private set; } = RevisionView.AllMarkup;

    /// <summary>Switches the markup view; the resolver changes, so listeners re-lay out everything.</summary>
    public void SetRevisionView(RevisionView view)
    {
        if (view == RevisionView)
        {
            return;
        }

        RevisionView = view;
        _resolver = new StyleResolver(Document.Styles, Document.Lists, view);
        DocumentChanged?.Invoke(this, new DocumentChangedEventArgs(Document, Document, ChangeSet.Structural()));
    }

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

        properties = WithTypingMarks(properties);

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

        properties = WithTypingMarks(properties);

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

        Commit(ReplaceImage(Document, at, image.WithSize(width, height)), EditKind.Other, startsNewGroup: true);
    }

    /// <summary>
    /// Makes the selected picture float with the given placement, or puts it back in the line when
    /// <paramref name="anchor"/> is null (Word's Wrap Text menu). Pictures inside table cells stay inline.
    /// </summary>
    public void SetImageAnchor(ImageAnchor? anchor)
    {
        if (SelectedImage() is not ({ } at, { } image) || Equals(image.Anchor, anchor) || (anchor is not null && at.Block.TablePath is not null))
        {
            return;
        }

        Commit(ReplaceImage(Document, at, image.WithAnchor(anchor)), EditKind.Formatting, startsNewGroup: true);
    }

    /// <summary>
    /// Insert > Shapes: a drawn shape with Word's default fill and outline, floating in front of the text at the
    /// top-left of the caret paragraph (a line is inserted in the line of text). One undo step; the shape is selected.
    /// </summary>
    public void InsertShape(ShapeKind kind, Twips width, Twips height)
    {
        (Document working, TextPosition at, ChangeSet? deletion) = DeleteSelectionIfAny();
        ImageAnchor? anchor = kind == ShapeKind.Line || at.Block.TablePath is not null ? null : ImageAnchor.Default with { Wrap = ImageWrap.InFrontOfText, DistanceX = Twips.Zero };
        InlineImage shape = InlineImage.CreateShape(kind, width, height, anchor);
        EditResult result = DocumentEditor.InsertInline(working, at, shape, inheritFormat: false);
        Commit(WithDeletion(result with { Selection = new Selection(at, at.WithOffset(at.Offset + 1)) }, deletion), EditKind.Other, startsNewGroup: true);
        PendingFormat = null;
    }

    /// <summary>Shape Fill for the selected shape; null means no fill. Pictures are left alone. One undo step.</summary>
    public void SetShapeFill(DocColor? fill)
    {
        DocColor? value = fill is { IsAuto: true } ? null : fill;
        if (SelectedImage() is ({ } at, { Shape: { } shape } image) && shape.Fill != value)
        {
            Commit(ReplaceImage(Document, at, image.WithShape(shape with { Fill = value })), EditKind.Formatting, startsNewGroup: true);
        }
    }

    /// <summary>Word's Picture Border for the selected picture; null removes it. One undo step.</summary>
    public void SetImageBorder(BorderLine? border)
    {
        if (SelectedImage() is ({ } at, { } image) && !Equals(image.Border, border))
        {
            Commit(ReplaceImage(Document, at, image.WithBorder(border)), EditKind.Formatting, startsNewGroup: true);
        }
    }

    /// <summary>Alt text (description) of the selected picture; empty clears it. One undo step.</summary>
    public void SetImageAltText(string? altText)
    {
        string? value = string.IsNullOrWhiteSpace(altText) ? null : altText.Trim();
        if (SelectedImage() is ({ } at, { } image) && !string.Equals(image.AltText, value, StringComparison.Ordinal))
        {
            Commit(ReplaceImage(Document, at, image.WithAltText(value)), EditKind.Other, startsNewGroup: true);
        }
    }

    /// <summary>Changes the selected picture's wrapping while keeping its position; no-op for an inline picture.</summary>
    public void SetImageWrap(ImageWrap wrap)
    {
        if (SelectedImage() is ({ } _, { Anchor: { } anchor }))
        {
            SetImageAnchor(anchor with { Wrap = wrap });
        }
    }

    /// <summary>Moves the floating picture at <paramref name="at"/> during a drag without recording undo; <see cref="EndImageDrag"/> commits the whole drag.</summary>
    public void PreviewImageAnchor(TextPosition at, ImageAnchor anchor)
    {
        ArgumentNullException.ThrowIfNull(anchor);
        if (ImageAt(Document.GetParagraph(at), at) is not (_, { Anchor: not null } image))
        {
            return;
        }

        _resizeBase ??= Document;
        EditResult result = ReplaceImage(Document, at, image.WithAnchor(anchor));
        ApplyState(result.Document, result.Selection, result.Change);
    }

    /// <summary>Resizes the picture at <paramref name="at"/> during a handle drag without recording undo (optionally moving a floating picture so the opposite edge stays put); <see cref="EndImageDrag"/> commits the whole drag.</summary>
    public void PreviewImageSize(TextPosition at, Twips width, Twips height, ImageAnchor? anchor = null)
    {
        if (ImageAt(Document.GetParagraph(at), at) is not (_, { } image))
        {
            return;
        }

        width = Twips.Max(width, new Twips(Twips.PerPoint));
        height = Twips.Max(height, new Twips(Twips.PerPoint));
        InlineImage replacement = image.WithSize(width, height);
        if (anchor is not null && image.IsFloating)
        {
            replacement = replacement.WithAnchor(anchor);
        }

        _resizeBase ??= Document;
        EditResult result = ReplaceImage(Document, at, replacement);
        ApplyState(result.Document, result.Selection, result.Change);
    }

    /// <summary>Ends a picture move or resize drag: the whole drag becomes one undo step.</summary>
    public void EndImageDrag() => EndColumnResize();

    private static EditResult ReplaceImage(Document document, TextPosition at, InlineImage replacement)
    {
        TextPosition end = at.WithOffset(at.Offset + 1);
        EditResult deleted = DocumentEditor.DeleteRange(document, new TextRange(at, end));
        EditResult inserted = DocumentEditor.InsertInline(deleted.Document, at, replacement, inheritFormat: false);
        return inserted with { Change = inserted.Change.Union(deleted.Change), Selection = new Selection(at, end) };
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
        Commit(DeleteOrMark(Document, new TextRange(previous.Value, position), caretAfter: false), EditKind.Backspace, joinsParagraphs);
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
        Commit(DeleteOrMark(Document, new TextRange(position, next.Value), caretAfter: true), EditKind.ForwardDelete, joinsParagraphs);
    }

    public void DeleteWordBackward() => DeleteTo(TextNavigation.PreviousWord(Document, Selection.Active));

    public void DeleteWordForward() => DeleteTo(TextNavigation.NextWord(Document, Selection.Active));

    public void DeleteSelection(EditKind kind = EditKind.Other)
    {
        if (Selection.IsCollapsed)
        {
            return;
        }

        Commit(DeleteOrMark(Document, Selection.Range, caretAfter: false), kind, startsNewGroup: true);
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
            else if (WordAroundCaret() is { } word)
            {
                // Like Word: with the caret inside a word, the whole word takes the formatting.
                Commit(DocumentEditor.ApplyRunFormat(Document, word, delta) with { Selection = Selection }, EditKind.Formatting, startsNewGroup: true);
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

    /// <summary>Ctrl+Q: back to the paragraph style's formatting.</summary>
    public void ResetParagraphFormatting()
    {
        EditResult result = DocumentEditor.ClearParagraphFormat(Document, Selection.Range);
        Commit(result with { Selection = Selection, Change = ChangeSet.Structural() }, EditKind.Formatting, startsNewGroup: true);
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
        ImmutableList<Block> blocks = Document.GetContainer(range.Story, range.Start.Block);
        int first = range.Start.Block.Last;
        int last = TextRanges.LastBlockIndex(range);
        bool allAlready = true;
        foreach (ParagraphSpan span in TextRanges.Paragraphs(blocks, range))
        {
            if (ListKind(span.Paragraph) != bulleted)
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
        ImmutableList<Block> blocks = Document.GetContainer(range.Story, range.Start.Block);
        Document document = Document;
        ImmutableList<Block>.Builder builder = blocks.ToBuilder();
        bool changed = false;
        foreach (ParagraphSpan span in TextRanges.Paragraphs(blocks, range))
        {
            Paragraph paragraph = span.Paragraph;
            int i = span.Index;
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
            Commit(new EditResult(document.WithContainer(range.Story, range.Start.Block, builder.ToImmutable()), Selection, ChangeSet.From(range.Story, range.Start.Block.TopIndex)), EditKind.Formatting, startsNewGroup: true);
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
        ImmutableList<Block> blocks = Document.GetContainer(range.Story, range.Start.Block);
        foreach (ParagraphSpan span in TextRanges.Paragraphs(blocks, range))
        {
            bool any = false;
            foreach (InlineSpan inline in span.Paragraph.Spans())
            {
                if (inline.End > span.Start && inline.Start < span.End)
                {
                    any = true;
                    yield return _resolver.ResolveRun(span.Paragraph, inline.Inline);
                }
            }

            if (!any || span.IncludesMark)
            {
                yield return _resolver.ResolveParagraphMark(span.Paragraph);
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

        properties = WithTypingMarks(properties);

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
        fragment = WithInsertionMarks(fragment);
        EditResult result = DocumentEditor.InsertFragment(working, at, fragment);
        Commit(WithDeletion(result, deletion), EditKind.Paste, startsNewGroup: true);
        PendingFormat = null;
    }

    /// <summary>Replaces the document properties (title, author...) as an undoable step.</summary>
    public void SetMetadata(DocumentMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        if (metadata == Document.Metadata)
        {
            return;
        }

        Commit(new EditResult(Document.WithMetadata(metadata), Selection, ChangeSet.From(Selection.Story, Selection.Active.Block.TopIndex)), EditKind.Other, startsNewGroup: true);
    }

    /// <summary>Numbers Heading 1-3 as 1, 1.1, 1.1.1 through their styles (or removes that numbering).</summary>
    public void NumberHeadings(bool on)
    {
        Document document = Document;
        string[] ids = [DefaultStyleSheet.Heading1Id, DefaultStyleSheet.Heading2Id, DefaultStyleSheet.Heading3Id];
        StyleSheet styles = document.Styles;
        if (on)
        {
            var levels = ImmutableArray.CreateBuilder<ListLevel>(ListDefinition.LevelCount);
            for (int i = 0; i < ListDefinition.LevelCount; i++)
            {
                string template = string.Join(".", Enumerable.Range(1, i + 1).Select(n => "%" + n.ToString(System.Globalization.CultureInfo.InvariantCulture)));
                levels.Add(new ListLevel { Format = NumberFormat.Decimal, Text = template, LeftIndent = Twips.Zero, Hanging = Twips.Zero });
            }

            (ListStore store, int numberingId) = document.Lists.AddList(levels.MoveToImmutable());
            document = document.WithLists(store);
            for (int level = 0; level < ids.Length; level++)
            {
                if (styles.Get(ids[level]) is { } style)
                {
                    styles = styles.With(style with { ParagraphProperties = style.ParagraphProperties with { List = new ListFormat(numberingId, level) } });
                }
            }
        }
        else
        {
            foreach (string id in ids)
            {
                if (styles.Get(id) is { } style && style.ParagraphProperties.List is not null)
                {
                    styles = styles.With(style with { ParagraphProperties = style.ParagraphProperties with { List = null } });
                }
            }
        }

        if (ReferenceEquals(styles, document.Styles) && ReferenceEquals(document, Document))
        {
            return;
        }

        Commit(new EditResult(document.WithStyles(styles), Selection, ChangeSet.Structural()), EditKind.Formatting, startsNewGroup: true);
    }

    /// <summary>True when Heading 1 is numbered through its style.</summary>
    public bool HeadingsAreNumbered => Document.Styles.Get(DefaultStyleSheet.Heading1Id)?.ParagraphProperties.List is { IsNone: false };

    // ----- Footnotes -----


    /// <summary>Inserts a footnote reference at the caret and starts the note; the caret moves into the note text.</summary>
    public void InsertFootnote()
    {
        if (Selection.Story.IsFootnote)
        {
            return;
        }

        (Document working, TextPosition at, ChangeSet? deletion) = DeleteSelectionIfAny();
        foreach (Style style in FootnoteSupport.RequiredStyles(working.Styles))
        {
            if (!working.Styles.Contains(style.Id))
            {
                working = working.WithStyles(working.Styles.With(style));
            }
        }

        int id = working.Footnotes.IsEmpty ? 1 : working.Footnotes.Keys.Max() + 1;
        var mark = new FootnoteReference(id, isNoteMark: true, null, FootnoteSupport.ReferenceStyleId);
        var note = ImmutableList.Create<Block>(new Paragraph([mark, new Run(" ")], FootnoteSupport.TextStyleId));
        working = working.WithFootnotes(working.Footnotes.SetItem(id, note));
        EditResult inserted = DocumentEditor.InsertInline(working, at, new FootnoteReference(id, false, null, FootnoteSupport.ReferenceStyleId));
        var caret = Selection.Caret(new TextPosition(StoryId.Footnote(id), BlockPath.Of(0), 2));
        Commit(new EditResult(inserted.Document, caret, ChangeSet.Structural()), EditKind.Other, startsNewGroup: true);
        _ = deletion;
        PendingFormat = null;
    }

    /// <summary>Inserts an endnote reference at the caret and starts the note at the end of the document; the caret moves there.</summary>
    public void InsertEndnote()
    {
        if (Selection.Story.IsNote)
        {
            return;
        }

        (Document working, TextPosition at, ChangeSet? deletion) = DeleteSelectionIfAny();
        foreach (Style style in FootnoteSupport.RequiredEndnoteStyles(working.Styles))
        {
            if (!working.Styles.Contains(style.Id))
            {
                working = working.WithStyles(working.Styles.With(style));
            }
        }

        int id = working.Endnotes.IsEmpty ? 1 : working.Endnotes.Keys.Max() + 1;
        var mark = new FootnoteReference(id, isNoteMark: true, null, FootnoteSupport.EndnoteReferenceStyleId, isEndnote: true);
        var note = ImmutableList.Create<Block>(new Paragraph([mark, new Run(" ")], FootnoteSupport.EndnoteTextStyleId));
        working = working.WithEndnotes(working.Endnotes.SetItem(id, note));
        EditResult inserted = DocumentEditor.InsertInline(working, at, new FootnoteReference(id, false, null, FootnoteSupport.EndnoteReferenceStyleId, isEndnote: true));
        var caret = Selection.Caret(new TextPosition(StoryId.Endnote(id), BlockPath.Of(0), 2));
        Commit(new EditResult(inserted.Document, caret, ChangeSet.Structural()), EditKind.Other, startsNewGroup: true);
        _ = deletion;
        PendingFormat = null;
    }

    /// <summary>The body position just after the reference of the note the caret is in, or null.</summary>
    public TextPosition? FootnoteReferencePosition()
    {
        if (!Selection.Story.IsNote)
        {
            return null;
        }

        int id = Selection.Story.Note;
        bool endnote = Selection.Story.IsEndnote;
        for (int s = 0; s < Document.Sections.Count; s++)
        {
            foreach ((BlockPath path, Paragraph paragraph) in BlockWalker.Paragraphs(Document.Sections[s].Body))
            {
                foreach (InlineSpan span in paragraph.Spans())
                {
                    if (span.Inline is FootnoteReference { IsNoteMark: false } note && note.NoteId == id && note.IsEndnote == endnote)
                    {
                        return new TextPosition(StoryId.Body(s), path, span.End);
                    }
                }

            }
        }

        return null;
    }

    // ----- Drag and drop -----


    /// <summary>Moves (or copies) the text of <paramref name="source"/> to <paramref name="target"/> as one undo step; the dropped text ends up selected.</summary>
    public void MoveText(TextRange source, TextPosition target, bool copy)
    {
        if (source.IsEmpty || !Document.IsValid(source.Start) || !Document.IsValid(source.End) || !Document.IsValid(target))
        {
            return;
        }

        if (!copy && source.Story == target.Story && (source.Contains(target) || target == source.End))
        {
            return; // dropped onto itself
        }

        DocumentFragment fragment = WithInsertionMarks(DocumentEditor.ExtractFragment(Document, source));
        Document document = Document;
        TextPosition at = target;
        ChangeSet? change = null;
        if (!copy)
        {
            if (TrackChanges)
            {
                EditResult marked = MarkDeleted(document, source, caretAfter: false, out int dropped);
                document = marked.Document;
                change = marked.Change;
                if (target.Story == source.Story && target.Block == source.End.Block && target.Offset >= source.End.Offset)
                {
                    at = target.WithOffset(target.Offset - dropped);
                }
            }
            else
            {
                EditResult deleted = DocumentEditor.DeleteRange(document, source);
                document = deleted.Document;
                change = deleted.Change;
                at = AdjustAfterDelete(target, source);
            }
        }


        EditResult inserted = DocumentEditor.InsertFragment(document, at, fragment);
        ChangeSet total = change is { } first ? first.Union(inserted.Change) : inserted.Change;
        Commit(new EditResult(inserted.Document, new Selection(at, inserted.Selection.Active), total), EditKind.Paste, startsNewGroup: true);
        PendingFormat = null;
    }

    /// <summary>Where a position lands after the text before it was removed.</summary>
    private static TextPosition AdjustAfterDelete(TextPosition target, TextRange removed)
    {
        if (target.Story != removed.Story)
        {
            return target;
        }

        // Compare at the depth of the removed blocks: the target may sit inside a table that follows them.
        int depth = removed.Start.Block.Depth;
        ImmutableArray<int> container = removed.Start.Block.ContainerIndices;
        if (target.Block.Depth < depth || !target.Block.Indices.AsSpan()[..container.Length].SequenceEqual(container.AsSpan()))
        {
            return target;
        }

        int index = target.Block.Indices[depth - 1];
        int first = removed.Start.Block.Last;
        int last = removed.End.Block.Last;
        if (index < first || (index == first && target.Block.Depth == depth && target.Offset <= removed.Start.Offset))
        {
            return target;
        }

        if (index < last || (index == last && target.Block.Depth == depth && target.Offset < removed.End.Offset))
        {
            return removed.Start;
        }

        if (index == last)
        {
            return new TextPosition(target.Story, removed.Start.Block, removed.Start.Offset + (target.Offset - removed.End.Offset));
        }

        ImmutableArray<int> indices = target.Block.Indices.SetItem(depth - 1, index - (last - first));
        return new TextPosition(target.Story, new BlockPath(indices), target.Offset);
    }

    // ----- Table of contents -----


    /// <summary>Inserts a table of contents before the caret paragraph, or replaces the existing one in place; the caret lands after it.</summary>
    public void InsertTableOfContents(IReadOnlyList<TocEntry> entries, Twips columnWidth, string? title = "Contents")
    {
        ArgumentNullException.ThrowIfNull(entries);
        Document document = Document;
        foreach (Style style in TableOfContents.RequiredStyles(document.Styles))
        {
            if (!document.Styles.Contains(style.Id))
            {
                document = document.WithStyles(document.Styles.With(style));
            }
        }

        ImmutableArray<Paragraph> paragraphs = TableOfContents.Build(entries, columnWidth, title);
        StoryId story;
        int index;
        ImmutableList<Block> blocks;
        if (TableOfContents.FindExisting(document) is { } existing)
        {
            story = existing.Story;
            index = existing.First;
            blocks = document.GetStory(story).RemoveRange(index, existing.Last - existing.First + 1);
        }
        else
        {
            story = StoryId.Body(Selection.Story.SectionIndex);
            index = Selection.Story == story ? Selection.Range.Start.Block.TopIndex : 0;
            blocks = document.GetStory(story);
        }

        blocks = blocks.InsertRange(index, paragraphs);
        document = document.WithStory(story, blocks);
        int after = Math.Min(index + paragraphs.Length, blocks.Count - 1);
        Commit(new EditResult(document, Selection.Caret(new TextPosition(story, BlockPath.Of(after), 0)), ChangeSet.Structural()), EditKind.Paste, startsNewGroup: true);
        PendingFormat = null;
    }

    // ----- Styles -----


    /// <summary>Adds or replaces a style (Modify Style); every paragraph in that style follows.</summary>
    public void ModifyStyle(Style style)
    {
        ArgumentNullException.ThrowIfNull(style);
        if (Equals(Document.Styles.Get(style.Id), style))
        {
            return;
        }

        Commit(new EditResult(Document.WithStyles(Document.Styles.With(style)), Selection, ChangeSet.Structural()), EditKind.Formatting, startsNewGroup: true);
    }

    /// <summary>Word's "Update style to match selection": the caret paragraph's direct formatting moves into its style.</summary>
    public void UpdateStyleFromSelection()
    {
        Paragraph paragraph = Document.GetParagraph(Selection.Active);
        string styleId = paragraph.StyleId ?? Document.Styles.DefaultParagraphStyleId;
        if (Document.Styles.Get(styleId) is not { } style)
        {
            return;
        }

        (string? _, RunProperties runDirect) = paragraph.GetTypingFormatAt(Selection.Active.Offset);
        Style updated = style with
        {
            ParagraphProperties = style.ParagraphProperties.Merge(paragraph.Properties with { List = null }),
            RunProperties = style.RunProperties.Merge(runDirect with { Link = null }),
        };
        Document document = Document.WithStyles(Document.Styles.With(updated));
        Paragraph cleared = paragraph.WithProperties(new ParagraphProperties { List = paragraph.Properties.List });
        document = DocumentEditor.ReplaceBlock(document, Selection.Story, Selection.Active.Block, cleared);
        Commit(new EditResult(document, Selection, ChangeSet.Structural()), EditKind.Formatting, startsNewGroup: true);
    }

    /// <summary>
    /// Word's "Create a Style": a new paragraph style named <paramref name="name"/>, based on the caret paragraph's style and
    /// carrying its direct paragraph and character formatting; the paragraph switches to it. Returns the new style id, or
    /// null when a style of that name exists.
    /// </summary>
    public string? CreateStyleFromSelection(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        name = name.Trim();
        if (Document.Styles.Styles.Values.Any(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            return null;
        }

        string baseId = new(name.Where(char.IsLetterOrDigit).ToArray());
        if (baseId.Length == 0)
        {
            baseId = "Style";
        }

        string id = baseId;
        for (int n = 2; Document.Styles.Contains(id); n++)
        {
            id = baseId + n.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        Paragraph paragraph = Document.GetParagraph(Selection.Active);
        string basedOn = paragraph.StyleId ?? Document.Styles.DefaultParagraphStyleId;
        (string? _, RunProperties runDirect) = paragraph.GetTypingFormatAt(Selection.Active.Offset);
        var style = new Style
        {
            Id = id,
            Name = name,
            BasedOn = basedOn,
            Next = Document.Styles.DefaultParagraphStyleId,
            QuickFormat = true,
            Priority = 50,
            ParagraphProperties = paragraph.Properties with { List = null },
            RunProperties = runDirect with { Link = null, CommentId = null, Revision = null },
        };
        Document document = Document.WithStyles(Document.Styles.With(style));
        Paragraph restyled = paragraph.WithStyle(id).WithProperties(new ParagraphProperties { List = paragraph.Properties.List });
        document = DocumentEditor.ReplaceBlock(document, Selection.Story, Selection.Active.Block, restyled);
        Commit(new EditResult(document, Selection, ChangeSet.Structural()), EditKind.Formatting, startsNewGroup: true);
        return id;
    }

    /// <summary>The character formatting a style produces on its own (for the Modify Style dialog).</summary>

    public ResolvedRunProperties ResolveStyleRun(string styleId) => _resolver.ResolveRun(styleId, null, RunProperties.Empty);

    public ResolvedParagraphProperties ResolveStyleParagraph(string styleId) => _resolver.ResolveParagraph(new Paragraph(ImmutableArray<Inline>.Empty, styleId));

    // ----- List numbering -----


    /// <summary>Restarts the list at the caret from 1 at this paragraph; later items of the same list follow on.</summary>
    public void RestartNumbering()
    {
        if (CurrentList() is not ({ } index, { } format) || Document.Lists.GetInstance(format.NumberingId) is not { } instance)
        {
            return;
        }

        int newId = Document.Lists.NextInstanceId;
        var restarted = new ListInstance(newId, instance.DefinitionId, ImmutableDictionary<int, int>.Empty.Add(format.Level, 1));
        RepointList(Document.WithLists(Document.Lists.With(restarted)), index, format.NumberingId, newId);
    }

    /// <summary>Makes the list at the caret continue the previous list of the same kind instead of starting over.</summary>
    public void ContinueNumbering()
    {
        if (CurrentList() is not ({ } index, { } format))
        {
            return;
        }

        ImmutableList<Block> blocks = Document.GetContainer(Selection.Story, Selection.Active.Block);
        bool bulleted = Document.Lists.GetLevel(format.NumberingId, 0)?.IsBullet ?? false;
        for (int i = index - 1; i >= 0; i--)
        {
            if (blocks[i] is not Paragraph paragraph || _resolver.ResolveParagraph(paragraph).List is not { IsNone: false } other)
            {
                continue;
            }

            if (other.NumberingId == format.NumberingId)
            {
                return; // already one list
            }

            if ((Document.Lists.GetLevel(other.NumberingId, 0)?.IsBullet ?? false) == bulleted)
            {
                RepointList(Document, index, format.NumberingId, other.NumberingId);
                return;
            }
        }
    }

    /// <summary>Changes how the current level of the list at the caret is numbered or bulleted (every item of that list follows).</summary>
    public void SetListStyle(NumberFormat format, string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (CurrentList() is not (_, { } list) || Document.Lists.GetDefinition(list.NumberingId) is not { } definition || definition.GetLevel(list.Level) is not { } level)
        {
            return;
        }

        ListLevel updated = level with
        {
            Format = format,
            Text = text,
            MarkerFont = null,
            Alignment = format is NumberFormat.LowerRoman or NumberFormat.UpperRoman ? Alignment.Right : Alignment.Left,
        };
        ListDefinition changed = definition with { Levels = definition.Levels.SetItem(list.Level, updated) };
        Commit(new EditResult(Document.WithLists(Document.Lists.With(changed)), Selection, ChangeSet.Structural()), EditKind.Formatting, startsNewGroup: true);
    }

    /// <summary>
    /// Word's Multilevel List gallery: the selected paragraphs join a new list built from the preset, keeping their
    /// levels; when the caret paragraph already belongs to a list, that whole list (in this block list) moves to the
    /// new definition. One undo step.
    /// </summary>
    public void ApplyMultilevelList(string presetName)
    {
        if (MultilevelLists.Find(presetName) is not { } preset)
        {
            return;
        }

        TextRange range = Selection.Range;
        if (!range.Start.Block.SameContainer(range.End.Block))
        {
            return;
        }

        (ListStore store, int newId) = Document.Lists.AddList(preset.Levels);
        Document document = Document.WithLists(store);
        ImmutableList<Block> blocks = document.GetContainer(range.Story, range.Start.Block);
        ImmutableList<Block>.Builder builder = blocks.ToBuilder();
        var resolver = new StyleResolver(document.Styles, document.Lists);
        int? oldId = CurrentList() is (_, { } current) ? current.NumberingId : null;
        int first = range.Start.Block.Last;
        int last = range.End.Block.Last;
        if (last > first && range.End.Offset == 0)
        {
            last--;
        }

        for (int i = 0; i < blocks.Count; i++)
        {
            if (blocks[i] is not Paragraph paragraph)
            {
                continue;
            }

            ListFormat? existing = resolver.ResolveParagraph(paragraph).List is { IsNone: false } format ? format : null;
            bool selected = i >= first && i <= last;
            bool sameList = oldId is { } id && existing?.NumberingId == id;
            if (!selected && !sameList)
            {
                continue;
            }

            builder[i] = paragraph.WithProperties(paragraph.Properties.Merge(new ParagraphProperties { List = new ListFormat(newId, existing?.Level ?? 0) }));
        }

        Commit(new EditResult(document.WithContainer(range.Story, range.Start.Block, builder.ToImmutable()), Selection, ChangeSet.Structural()), EditKind.Formatting, startsNewGroup: true);
    }

    private (int Index, ListFormat Format)? CurrentList()

    {
        Paragraph paragraph = Document.GetParagraph(Selection.Active);
        return _resolver.ResolveParagraph(paragraph).List is { IsNone: false } format ? (Selection.Active.Block.Last, format) : null;
    }

    /// <summary>Moves this paragraph and every later paragraph of list <paramref name="oldId"/> in the story to list <paramref name="newId"/>.</summary>
    private void RepointList(Document document, int fromIndex, int oldId, int newId)
    {
        ImmutableList<Block> blocks = document.GetContainer(Selection.Story, Selection.Active.Block);
        ImmutableList<Block>.Builder builder = blocks.ToBuilder();
        for (int i = fromIndex; i < blocks.Count; i++)
        {
            if (blocks[i] is Paragraph paragraph && _resolver.ResolveParagraph(paragraph).List is { IsNone: false } format && format.NumberingId == oldId)
            {
                builder[i] = paragraph.WithProperties(paragraph.Properties.Merge(new ParagraphProperties { List = new ListFormat(newId, format.Level) }));
            }
        }

        Commit(new EditResult(document.WithContainer(Selection.Story, Selection.Active.Block, builder.ToImmutable()), Selection, ChangeSet.Structural()), EditKind.Formatting, startsNewGroup: true);
    }

    // ----- Hyperlinks -----


    /// <summary>The hyperlink at <paramref name="position"/> (or just before it): its full extent and target.</summary>
    public (TextRange Range, string Url)? LinkAt(TextPosition position)
    {
        if (Document.TryGetParagraph(position) is not { } paragraph)
        {
            return null;
        }

        List<InlineSpan> spans = paragraph.Spans().ToList();
        int offset = position.Offset;
        int index = spans.FindIndex(s => s.Start <= offset && offset < s.End && s.Inline.Properties.Link is not null);
        if (index < 0 && offset > 0)
        {
            index = spans.FindIndex(s => s.Start <= offset - 1 && offset - 1 < s.End && s.Inline.Properties.Link is not null);
        }

        if (index < 0)
        {
            return null;
        }

        string url = spans[index].Inline.Properties.Link!;
        int first = index;
        while (first > 0 && string.Equals(spans[first - 1].Inline.Properties.Link, url, StringComparison.Ordinal))
        {
            first--;
        }

        int last = index;
        while (last + 1 < spans.Count && string.Equals(spans[last + 1].Inline.Properties.Link, url, StringComparison.Ordinal))
        {
            last++;
        }

        return (new TextRange(position.WithOffset(spans[first].Start), position.WithOffset(spans[last].End)), url);
    }

    public (TextRange Range, string Url)? LinkAtCaret() => LinkAt(Selection.Active);

    /// <summary>
    /// Inserts or updates a hyperlink. A selection (or the link under the caret) becomes the link, its text replaced
    /// by <paramref name="text"/> when that differs; with nothing selected the text is inserted at the caret.
    /// </summary>
    public void InsertLink(string text, string url)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(url);
        if (url.Length == 0)
        {
            return;
        }

        if (text.Length == 0)
        {
            text = url;
        }

        TextRange range = Selection.IsCollapsed && LinkAtCaret() is { } existing ? existing.Range : Selection.Range;
        Document document = Document;
        ChangeSet? change = null;
        void Apply(EditResult result)
        {
            if (result.IsNoOp)
            {
                return;
            }

            document = result.Document;
            change = change is null ? result.Change : change.Value.Union(result.Change);
        }

        string current = range.IsEmpty ? string.Empty : DocumentEditor.ExtractFragment(document, range).ToPlainText();
        TextPosition start = range.Start;
        if (!range.IsWithinOneParagraph || !string.Equals(current, text, StringComparison.Ordinal))
        {
            (string? styleId, RunProperties properties) = TypingFormat(document, new Selection(range.Start, range.End));
            if (!range.IsEmpty)
            {
                Apply(DocumentEditor.DeleteRange(document, range));
            }

            string? plainStyle = styleId == DefaultStyleSheet.HyperlinkStyleId ? null : styleId;
            Apply(DocumentEditor.InsertText(document, start, text, properties with { Link = null }, plainStyle));
            range = new TextRange(start, start.WithOffset(start.Offset + text.Length));
        }

        bool hasStyle = document.Styles.Contains(DefaultStyleSheet.HyperlinkStyleId);
        RunProperties? fallback = hasStyle ? null : new RunProperties { Color = DocColor.Parse("0563C1"), Underline = UnderlineStyle.Single };
        Apply(DocumentEditor.ApplyLink(document, range, url, hasStyle ? DefaultStyleSheet.HyperlinkStyleId : null, fallback));
        if (change is { } total)
        {
            Commit(new EditResult(document, Selection.Caret(range.End), total), EditKind.Other, startsNewGroup: true);
        }

        PendingFormat = null;
    }

    /// <summary>Turns the selected links (or the link under the caret) back into plain text.</summary>
    public void RemoveLink()
    {
        TextRange? range = Selection.IsCollapsed ? LinkAtCaret()?.Range : Selection.Range;
        if (range is { } target)
        {
            Commit(DocumentEditor.RemoveLinks(Document, target, DefaultStyleSheet.HyperlinkStyleId) with { Selection = Selection }, EditKind.Formatting, startsNewGroup: true);
        }
    }

    // ----- Clear formatting and Format Painter -----


    /// <summary>Ctrl+Space: strips direct character formatting and character styles from the selection, or from the word the caret is in.</summary>
    public void ClearFormatting()
    {
        PendingFormat = null;
        if (FormatTarget() is { } range)
        {
            Commit(DocumentEditor.ClearRunFormat(Document, range) with { Selection = Selection }, EditKind.Formatting, startsNewGroup: true);
        }
    }

    /// <summary>Picks up the formatting at the caret (or at the start of the selection) for the Format Painter.</summary>
    public FormatSample CopyFormat()
    {
        TextRange range = Selection.Range;
        Paragraph paragraph = Document.GetParagraph(range.Start);
        int probe = Selection.IsCollapsed ? range.Start.Offset : Math.Min(paragraph.Length, range.Start.Offset + 1);
        (string? styleId, RunProperties run) = paragraph.GetTypingFormatAt(probe);
        bool withParagraph = Selection.IsCollapsed || TextRanges.Paragraphs(Document, range).Any(s => s.IncludesMark);
        return new FormatSample(run with { Link = null, CommentId = null }, styleId == DefaultStyleSheet.HyperlinkStyleId ? null : styleId, withParagraph ? paragraph.Properties : null, withParagraph ? paragraph.StyleId : null);
    }

    /// <summary>Applies a Format Painter sample to the selection (or the word at the caret), replacing the character formatting there.</summary>
    public void PasteFormat(FormatSample sample)
    {
        ArgumentNullException.ThrowIfNull(sample);
        TextRange range = FormatTarget() ?? Selection.Range;
        Document document = Document;
        ChangeSet? change = null;
        void Apply(EditResult result)
        {
            if (result.IsNoOp)
            {
                return;
            }

            document = result.Document;
            change = change is null ? result.Change : change.Value.Union(result.Change);
        }

        if (!range.IsEmpty)
        {
            Apply(DocumentEditor.ClearRunFormat(document, range));
            Apply(DocumentEditor.ApplyCharacterStyle(document, range, sample.CharacterStyleId));
            Apply(DocumentEditor.ApplyRunFormat(document, range, sample.Run));
        }

        if (sample.Paragraph is { } paragraphFormat)
        {
            Apply(DocumentEditor.SetParagraphStyle(document, range, sample.ParagraphStyleId));
            Apply(DocumentEditor.ApplyParagraphFormat(document, range, paragraphFormat));
        }

        if (change is { } total)
        {
            Commit(new EditResult(document, Selection, total), EditKind.Formatting, startsNewGroup: true);
        }
    }

    /// <summary>The selection, or the word around the caret; null when the caret is not inside a word.</summary>
    private TextRange? FormatTarget() => Selection.IsCollapsed ? WordAroundCaret() : Selection.Range;

    /// <summary>The word the caret sits strictly inside (a letter on both sides), without its trailing spaces.</summary>
    private TextRange? WordAroundCaret()
    {
        TextPosition at = Selection.Active;
        Paragraph paragraph = Document.GetParagraph(at);
        string text = paragraph.FlatText;
        bool letterBefore = at.Offset > 0 && at.Offset <= text.Length && Words.IsWordCharacter(text[at.Offset - 1]);
        bool letterAfter = at.Offset < text.Length && Words.IsWordCharacter(text[at.Offset]);
        if (!letterBefore || !letterAfter)
        {
            return null;
        }

        TextRange word = TextNavigation.WordAt(Document, at);
        int end = word.End.Offset;
        while (end > word.Start.Offset && !Words.IsWordCharacter(text[end - 1]))
        {
            end--;
        }

        return end > word.Start.Offset ? new TextRange(word.Start, word.Start.WithOffset(end)) : null;
    }

    // ----- Change case -----



    /// <summary>Changes the case of the selection, or of the word at the caret when nothing is selected.</summary>
    public void ChangeCase(CaseChange change)
    {
        TextRange range = CaseRange();
        if (range.IsEmpty)
        {
            return;
        }

        System.Globalization.CultureInfo culture = System.Globalization.CultureInfo.CurrentCulture;
        EditResult result = DocumentEditor.TransformText(Document, range, text => TextCase.Apply(text, change, culture));
        Commit(result with { Selection = Selection }, EditKind.Other, startsNewGroup: true);
    }

    /// <summary>Shift+F3: lowercase, UPPERCASE, Capitalize Each Word, and round again.</summary>
    public void CycleCase()
    {
        TextRange range = CaseRange();
        if (range.IsEmpty)
        {
            return;
        }

        ChangeCase(TextCase.NextInCycle(DocumentEditor.ExtractFragment(Document, range).ToPlainText()));
    }

    private TextRange CaseRange() => Selection.IsCollapsed ? TextNavigation.WordAt(Document, Selection.Active) : Selection.Range;

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
        earlier.Block.SameContainer(later.Block) && later.Block.Last - earlier.Block.Last <= 1;

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

        Commit(DeleteOrMark(Document, new TextRange(Selection.Active, target.Value), caretAfter: target.Value > Selection.Active), EditKind.Other, startsNewGroup: true);
    }

    private (Document Document, TextPosition At, ChangeSet? Deletion) DeleteSelectionIfAny()
    {
        if (Selection.IsCollapsed)
        {
            return (Document, Selection.Active, null);
        }

        EditResult deleted = DeleteOrMark(Document, Selection.Range, caretAfter: true);
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
            _resolver = new StyleResolver(document.Styles, document.Lists, RevisionView);
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
