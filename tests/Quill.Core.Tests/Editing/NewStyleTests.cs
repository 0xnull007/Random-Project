using Quill.Core.Editing;
using Quill.Core.Model;
using Quill.Core.Styles;
using Quill.Core.Units;
using Xunit;
using static Quill.Core.Tests.Support.TestDocuments;

namespace Quill.Core.Tests.Editing;

public class NewStyleTests
{
    [Fact]
    public void A_style_created_from_the_selection_takes_the_direct_formatting_and_is_applied()
    {
        var session = new EditingSession(WithParagraphs("Quote text", "Other"));
        session.MoveCaret(Pos(0, 0), extend: false);
        session.MoveCaret(Pos(0, 10), extend: true);
        session.ApplyRunFormat(new RunProperties { Italic = true, Color = DocColor.Parse("595959") });
        session.ApplyParagraphFormat(new ParagraphProperties { LeftIndent = Twips.FromInches(0.5), Alignment = Alignment.Center });
        session.MoveCaret(Pos(0, 3), extend: false);

        string? id = session.CreateStyleFromSelection("My Quote");
        Assert.Equal("MyQuote", id);
        Style style = session.Document.Styles.Get("MyQuote")!;
        Assert.Equal("My Quote", style.Name);
        Assert.Equal(StyleSheet.NormalStyleId, style.BasedOn);
        Assert.True(style.QuickFormat);
        Assert.True(style.RunProperties.Italic);
        Assert.Equal(DocColor.Parse("595959"), style.RunProperties.Color);
        Assert.Equal(Twips.FromInches(0.5), style.ParagraphProperties.LeftIndent);
        Assert.Equal(Alignment.Center, style.ParagraphProperties.Alignment);

        Paragraph paragraph = session.Document.Para(0);
        Assert.Equal("MyQuote", paragraph.StyleId);
        Assert.Null(paragraph.Properties.LeftIndent);
        Assert.Equal(Alignment.Center, session.CaretParagraphFormat().Alignment);
        Assert.True(session.ResolveStyleRun("MyQuote").Italic);
        Assert.Null(session.Document.Para(1).StyleId);

        Assert.Null(session.CreateStyleFromSelection("my quote"));
        Assert.Equal("MyQuote2", session.CreateStyleFromSelection("My-Quote!"));
        session.Undo();
        session.Undo();
        Assert.Null(session.Document.Styles.Get("MyQuote"));
        Assert.Null(session.Document.Para(0).StyleId);
    }
}
