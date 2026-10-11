using Quill.Core.Editing;
using Quill.Core.Model;
using Quill.Core.Units;
using Xunit;
using static Quill.Core.Tests.Support.TestDocuments;

namespace Quill.Core.Tests.Editing;

public class DesignTests
{
    [Fact]
    public void Page_colour_is_an_undoable_document_setting()
    {
        var session = new EditingSession(WithParagraphs("a"));
        Assert.Null(session.PageColor);
        session.SetPageColor(DocColor.Parse("FFF2CC"));
        Assert.Equal(DocColor.Parse("FFF2CC"), session.Document.Settings.PageColor);
        session.SetPageColor(DocColor.Auto);
        Assert.Null(session.PageColor);
        session.Undo();
        Assert.Equal(DocColor.Parse("FFF2CC"), session.PageColor);
    }

    [Fact]
    public void Page_borders_apply_to_one_section_or_all_of_them()
    {
        var session = new EditingSession(WithParagraphs("one", "two"));
        session.MoveCaret(Pos(1, 0), extend: false);
        session.InsertSectionBreak(SectionStart.NextPage);
        Assert.Equal(2, session.Document.Sections.Count);
        Assert.Equal(1, session.Selection.Story.SectionIndex);

        var box = new PageBorders { Style = PageBorderStyle.Double, Width = 8, Color = DocColor.Parse("0070C0"), Space = 20 };
        session.SetPageBorders(box, wholeDocument: false);
        Assert.Null(session.Document.Sections[0].Properties.PageBorders);
        Assert.Equal(box, session.Document.Sections[1].Properties.PageBorders);
        Assert.Equal(box, session.PageBordersAtCaret);

        session.SetPageBorders(box, wholeDocument: true);
        Assert.Equal(box, session.Document.Sections[0].Properties.PageBorders);
        Assert.Equal(8.0 / 8 * 96 / 72, box.ThicknessDips, 6);

        session.SetPageBorders(null, wholeDocument: true);
        Assert.All(session.Document.Sections, s => Assert.Null(s.Properties.PageBorders));
        session.Undo();
        Assert.Equal(box, session.Document.Sections[0].Properties.PageBorders);
    }
}
