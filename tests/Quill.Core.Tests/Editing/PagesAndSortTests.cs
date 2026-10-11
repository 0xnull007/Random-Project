using Quill.Core.Editing;
using Quill.Core.Model;
using Quill.Core.Styles;
using Xunit;
using static Quill.Core.Tests.Support.TestDocuments;

namespace Quill.Core.Tests.Editing;

public class PagesAndSortTests
{
    [Fact]
    public void Sorting_orders_selected_paragraphs_by_text_or_number_and_keeps_their_formatting()
    {
        var session = new EditingSession(WithParagraphs("pear", "Apple", "banana", "keep last"));
        session.MoveCaret(Pos(0, 0), extend: false);
        session.MoveCaret(Pos(3, 0), extend: true); // the fourth paragraph is only touched at its start: not sorted
        session.SortParagraphs();
        Assert.Equal(["Apple", "banana", "pear", "keep last"], session.Document.BodyTexts());
        Assert.Equal(Range(0, 0, 2, 4), session.Selection.Range);

        session.SortParagraphs(descending: true);
        Assert.Equal(["pear", "banana", "Apple", "keep last"], session.Document.BodyTexts());

        var numbers = new EditingSession(WithParagraphs("10 ten", "9 nine", "100 hundred"));
        numbers.SortParagraphs();
        Assert.Equal(["9 nine", "10 ten", "100 hundred"], numbers.Document.BodyTexts());

        var styled = new EditingSession(WithBlocks(Paragraph.FromText("zeta", DefaultStyleSheet.Heading1Id), Paragraph.FromText("alpha")));
        styled.SortParagraphs();
        Assert.Equal(["alpha", "zeta"], styled.Document.BodyTexts());
        Assert.Equal(DefaultStyleSheet.Heading1Id, styled.Document.Para(1).StyleId);
        styled.Undo();
        Assert.Equal(["zeta", "alpha"], styled.Document.BodyTexts());
    }

    [Fact]
    public void Blank_page_is_two_page_breaks_around_an_empty_paragraph()
    {
        var session = new EditingSession(WithParagraphs("Hello world"));
        session.MoveCaret(Pos(0, 5), extend: false);
        session.InsertBlankPage();
        Assert.Equal(3, session.Document.Sections[0].Body.Count);
        Assert.Equal("Hello￼", session.Document.Text(0));
        Assert.Equal("", session.Document.Text(1));
        Assert.Equal("￼ world", session.Document.Text(2));
        Assert.IsType<Break>(session.Document.Para(2).Inlines[0]);
        Assert.Equal(Pos(2, 1), session.Selection.Active);
    }

    [Fact]
    public void Cover_page_goes_in_front_with_title_subtitle_author_and_date_and_a_different_first_page()
    {
        var session = new EditingSession(WithParagraphs("Body"));
        session.MoveCaret(Pos(0, 4), extend: false);
        session.InsertCoverPage("Ada Lovelace");
        IReadOnlyList<string> texts = session.Document.BodyTexts();
        Assert.Equal(6, texts.Count);
        Assert.Equal("Title", texts[0]);
        Assert.Equal(DefaultStyleSheet.TitleId, session.Document.Para(0).StyleId);
        Assert.Equal("Subtitle", texts[1]);
        Assert.Equal(CoverPageSupport.SubtitleStyleId, session.Document.Para(1).StyleId);
        Assert.True(session.Document.Styles.Contains(CoverPageSupport.SubtitleStyleId));
        Assert.Equal("Ada Lovelace", texts[2]);
        Assert.Equal(DateTime.Today.ToString("D", System.Globalization.CultureInfo.CurrentCulture), texts[3]);
        Assert.Equal("￼", texts[4]);
        Assert.Equal("Body", texts[5]);
        Assert.True(session.Document.Sections[0].Properties.TitlePage);
        Assert.Equal(Range(0, 0, 0, 5), session.Selection.Range);

        session.InsertText("My Report");
        Assert.Equal("My Report", session.Document.Text(0));
        session.Undo();
        session.Undo();
        Assert.Equal(["Body"], session.Document.BodyTexts());
    }
}
