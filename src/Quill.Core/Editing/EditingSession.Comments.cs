using System.Collections.Immutable;
using Quill.Core.Model;
using Quill.Core.Styles;
using Quill.Core.Text;

namespace Quill.Core.Editing;

public sealed partial class EditingSession
{
    /// <summary>Attaches a new comment to the selection (or the word at the caret) and returns its id.</summary>
    public int AddComment(string author, string text = "")
    {
        ArgumentNullException.ThrowIfNull(author);
        ArgumentNullException.ThrowIfNull(text);
        TextRange range = Selection.IsCollapsed ? TextNavigation.WordAt(Document, Selection.Active) : Selection.Range;
        Document document = Document;
        foreach (Style style in CommentSupport.RequiredStyles(document.Styles))
        {
            if (!document.Styles.Contains(style.Id))
            {
                document = document.WithStyles(document.Styles.With(style));
            }
        }

        int id = document.Comments.IsEmpty ? 0 : document.Comments.Keys.Max() + 1;
        ImmutableList<Block> blocks = CommentBlocks(text);
        var comment = new Comment(id, author, Initials(author), DateTimeOffset.Now, blocks);
        document = document.WithComments(document.Comments.SetItem(id, comment));
        EditResult marked = range.IsEmpty
            ? EditResult.NoOp(document, Selection)
            : DocumentEditor.ApplyRunFormat(document, range, new RunProperties { CommentId = id });
        Document result = marked.IsNoOp ? document : marked.Document;
        Commit(new EditResult(result, Selection, ChangeSet.Structural()), EditKind.Other, startsNewGroup: true);
        return id;
    }

    /// <summary>Replaces the text of a comment.</summary>
    public void SetCommentText(int id, string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (!Document.Comments.TryGetValue(id, out Comment? comment) || string.Equals(comment.Text, text.Replace("\r\n", "\n", StringComparison.Ordinal), StringComparison.Ordinal))
        {
            return;
        }

        Comment updated = comment with { Blocks = CommentBlocks(text), Date = DateTimeOffset.Now };
        Commit(new EditResult(Document.WithComments(Document.Comments.SetItem(id, updated)), Selection, ChangeSet.Structural()), EditKind.Other, startsNewGroup: true);
    }

    /// <summary>Removes a comment and its marks from every run.</summary>
    public void DeleteComment(int id)
    {
        Document document = Document;
        if (!document.Comments.ContainsKey(id))
        {
            return;
        }

        document = document.WithComments(document.Comments.Remove(id));
        foreach ((StoryId story, ImmutableList<Block> blocks) in DocumentEditor.AllStories(Document))
        {
            ImmutableList<Block>.Builder? builder = null;
            for (int b = 0; b < blocks.Count; b++)
            {
                if (blocks[b] is not Paragraph paragraph || !paragraph.Inlines.Any(i => i.Properties.CommentId == id))
                {
                    continue;
                }

                builder ??= blocks.ToBuilder();
                builder[b] = paragraph.WithInlines(InlineOps.Normalize(paragraph.Inlines.Select(i => i.Properties.CommentId == id ? i.WithProperties(i.Properties with { CommentId = null }) : i)));
            }

            if (builder is not null)
            {
                document = document.WithStory(story, builder.ToImmutable());
            }
        }

        Commit(new EditResult(document, Selection, ChangeSet.Structural()), EditKind.Other, startsNewGroup: true);
    }

    /// <summary>The first run of text a comment is attached to, as a range (within one story), or null when it has none.</summary>
    public TextRange? CommentRange(int id)
    {
        foreach ((StoryId story, ImmutableList<Block> blocks) in DocumentEditor.AllStories(Document))
        {
            TextPosition? start = null;
            TextPosition? end = null;
            for (int b = 0; b < blocks.Count; b++)
            {
                if (blocks[b] is not Paragraph paragraph)
                {
                    continue;
                }

                foreach (InlineSpan span in paragraph.Spans())
                {
                    if (span.Inline.Properties.CommentId != id)
                    {
                        continue;
                    }

                    start ??= new TextPosition(story, BlockPath.Of(b), span.Start);
                    end = new TextPosition(story, BlockPath.Of(b), span.End);
                }
            }

            if (start is { } s && end is { } e)
            {
                return new TextRange(s, e);
            }
        }

        return null;
    }

    /// <summary>Comment ids attached at the caret (or anywhere in the selection).</summary>
    public IReadOnlyList<int> CommentsAtSelection()
    {
        var ids = new List<int>();
        foreach (ResolvedRunProperties run in SelectionFormats())
        {
            if (run.CommentId is { } id && !ids.Contains(id) && Document.Comments.ContainsKey(id))
            {
                ids.Add(id);
            }
        }

        return ids;
    }

    private static ImmutableList<Block> CommentBlocks(string text)
    {
        DocumentFragment fragment = DocumentFragment.FromPlainText(text, null, CommentSupport.TextStyleId);
        return ImmutableList.CreateRange<Block>(fragment.Paragraphs);
    }

    private static string? Initials(string author)
    {
        string initials = string.Concat(author.Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(p => p.Length > 0).Select(p => char.ToUpperInvariant(p[0])));
        return initials.Length == 0 ? null : initials;
    }
}
