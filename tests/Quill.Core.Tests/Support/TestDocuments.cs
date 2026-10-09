using System.Collections.Immutable;
using Quill.Core.Model;
using Quill.Core.Styles;
using Quill.Core.Text;

namespace Quill.Core.Tests.Support;

internal static class TestDocuments
{
    public static readonly StoryId Body = StoryId.Body(0);

    /// <summary>A one-section document whose body is the given paragraphs (plain text, Normal style).</summary>
    public static Document WithParagraphs(params string[] texts)
    {
        ImmutableList<Block> blocks = texts.Select(t => (Block)Paragraph.FromText(t)).ToImmutableList();
        if (blocks.Count == 0)
        {
            blocks = ImmutableList.Create<Block>(Paragraph.Empty());
        }

        return new Document(ImmutableList.Create(new Section(SectionProperties.Letter, blocks)), DefaultStyleSheet.Create());
    }

    public static Document WithBlocks(params Block[] blocks) =>
        new(ImmutableList.Create(new Section(SectionProperties.Letter, blocks.ToImmutableList())), DefaultStyleSheet.Create());

    public static TextPosition Pos(int block, int offset) => new(Body, BlockPath.Of(block), offset);

    public static TextRange Range(int startBlock, int startOffset, int endBlock, int endOffset) =>
        new(Pos(startBlock, startOffset), Pos(endBlock, endOffset));

    public static Paragraph Para(this Document document, int index) => (Paragraph)document.Sections[0].Body[index];

    public static string Text(this Document document, int index) => document.Para(index).FlatText;

    public static IReadOnlyList<string> BodyTexts(this Document document) =>
        document.Sections[0].Body.Select(b => b is Paragraph p ? p.FlatText : $"<{b}>").ToList();
}

/// <summary>A TimeProvider whose clock only advances when told to.</summary>
internal sealed class FakeTime : TimeProvider
{
    private long _ticks;

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    public override long GetTimestamp() => _ticks;

    public void Advance(TimeSpan by) => _ticks += by.Ticks;
}
