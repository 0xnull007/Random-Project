using System.Collections.Immutable;
using Quill.Core.Model;
using Quill.Core.Text;

namespace Quill.Core.Editing;

public sealed partial class EditingSession
{
    /// <summary>Rules applied by <see cref="TypeText"/>; the app sets this from its options.</summary>
    public AutoCorrectOptions AutoCorrect { get; set; } = AutoCorrectOptions.Default;

    /// <summary>Text typed by the user (one keystroke or an IME commit): runs AutoCorrect, then inserts.</summary>
    public void TypeText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length != 1 || !AutoCorrect.Any || !Selection.IsCollapsed || Document.TryGetParagraph(Selection.Active) is not { } paragraph)
        {
            InsertText(text);
            return;
        }

        TextPosition at = Selection.Active;
        char typed = text[0];
        string before = paragraph.FlatText[..Math.Min(at.Offset, paragraph.Length)];
        if (at.Offset > 0 && paragraph.TryGetInlineAt(at.Offset - 1, out InlineSpan previous) && previous.Inline.Properties.Link is not null)
        {
            InsertText(text); // never rewrite inside a hyperlink
            return;
        }

        if (AutoCorrect.AutomaticLists && typed == ' ' && at.Offset == paragraph.Length && AutoCorrector.AutoListKind(before) is { } bulleted
            && !_resolver.ResolveParagraph(paragraph).IsListItem)
        {
            StartAutomaticList(at, bulleted);
            return;
        }

        if (AutoCorrect.SmartQuotes && typed is '"' or '\'')
        {
            InsertText(AutoCorrector.SmartQuote(typed, before).ToString());
            return;
        }

        if (AutoCorrect.CapitalizeSentences && AutoCorrector.SentenceStartToCapitalize(before, typed) is { } wordStart)
        {
            TextPosition letter = at.WithOffset(wordStart);
            ReplaceText(new TextRange(letter, letter.WithOffset(wordStart + 1)), char.ToUpper(before[wordStart], System.Globalization.CultureInfo.CurrentCulture).ToString(), Selection.Caret(at));
            before = string.Concat(before.AsSpan(0, wordStart), char.ToUpper(before[wordStart], System.Globalization.CultureInfo.CurrentCulture).ToString(), before.AsSpan(wordStart + 1));
        }

        if ((AutoCorrect.Symbols || AutoCorrect.Dashes) && AutoCorrector.Replacement(before, typed, AutoCorrect) is { } rewrite)
        {
            TextPosition start = at.WithOffset(rewrite.Offset);
            int caretAfter = at.Offset - rewrite.Length + rewrite.Replacement.Length;
            ReplaceText(new TextRange(start, start.WithOffset(rewrite.Offset + rewrite.Length)), rewrite.Replacement, Selection.Caret(at.WithOffset(caretAfter)));
            if (!rewrite.ConsumesTyped)
            {
                InsertText(text);
            }

            return;
        }

        InsertText(text);
    }

    /// <summary>Replaces a range with text in the formatting of its first character, as one undo step.</summary>
    private void ReplaceText(TextRange range, string replacement, Selection after)
    {
        (string? styleId, RunProperties properties) = TypingFormat(Document, Selection.Caret(range.Start.WithOffset(Math.Min(range.Start.Offset + 1, range.End.Offset))));
        EditResult deleted = DocumentEditor.DeleteRange(Document, range);
        EditResult inserted = DocumentEditor.InsertText(deleted.Document, range.Start, replacement, properties with { Link = null }, styleId);
        Commit(new EditResult(inserted.Document, after, deleted.Change.Union(inserted.Change)), EditKind.Typing, startsNewGroup: true);
        PendingFormat = null;
    }

    /// <summary>"* " or "1. " at the start of a paragraph turns it into a list item; the marker text goes away.</summary>
    private void StartAutomaticList(TextPosition at, bool bulleted)
    {
        ImmutableList<Block> blocks = Document.GetStory(at.Story);
        TextPosition start = at.WithOffset(0);
        EditResult deleted = DocumentEditor.DeleteRange(Document, new TextRange(start, at));
        Document document = deleted.Document;
        int? numberingId = NeighbourList(blocks, at.Block.TopIndex - 1, bulleted);
        if (numberingId is null)
        {
            (ListStore store, int id) = document.Lists.AddList(bulleted ? DefaultLists.BulletLevels() : DefaultLists.NumberedLevels());
            document = document.WithLists(store);
            numberingId = id;
        }

        EditResult listed = DocumentEditor.ApplyParagraphFormat(document, new TextRange(start, start), new ParagraphProperties { List = new ListFormat(numberingId.Value, 0) });
        Commit(new EditResult(listed.Document, Selection.Caret(start), ChangeSet.Structural()), EditKind.Other, startsNewGroup: true);
        PendingFormat = null;
    }
}
