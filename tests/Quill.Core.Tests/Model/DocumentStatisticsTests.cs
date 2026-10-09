using Quill.Core.Model;
using Xunit;
using static Quill.Core.Tests.Support.TestDocuments;

namespace Quill.Core.Tests.Model;

public class DocumentStatisticsTests
{
    [Fact]
    public void Counts_words_characters_and_paragraphs_like_word()
    {
        Document doc = WithParagraphs("Hello, world!", "", "  two\twords  ", "x");
        DocumentStatistics stats = DocumentStatistics.Compute(doc);
        Assert.Equal(5, stats.Words);
        Assert.Equal(12 + 8 + 1, stats.Characters);
        Assert.Equal(13 + 13 + 1, stats.CharactersWithSpaces);
        Assert.Equal(3, stats.Paragraphs);
    }

    [Fact]
    public void Fields_and_breaks_do_not_count_as_characters()
    {
        var paragraph = new Paragraph([new Run("Page "), Field.Page(), new Break(BreakKind.Line), new Run("end")]);
        DocumentStatistics stats = DocumentStatistics.Compute([paragraph]);
        Assert.Equal(2, stats.Words);
        Assert.Equal(7, stats.Characters);
        Assert.Equal(8, stats.CharactersWithSpaces);
    }

    [Fact]
    public void Empty_document_counts_nothing()
    {
        Assert.Equal(new DocumentStatistics(0, 0, 0, 0), DocumentStatistics.Compute(Document.CreateNew()));
    }
}
