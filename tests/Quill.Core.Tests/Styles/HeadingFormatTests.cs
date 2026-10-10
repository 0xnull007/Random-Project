using Quill.Core.Editing;
using Quill.Core.Model;
using Quill.Core.Styles;
using Quill.Core.Tests.Support;
using Xunit;
using static Quill.Core.Tests.Support.TestDocuments;

namespace Quill.Core.Tests.Styles;

public class HeadingFormatTests
{
    [Fact]
    public void Direct_bold_and_italic_apply_inside_headings()
    {
        var session = new EditingSession(WithParagraphs("Topics:", "Body"), new UndoStack(time: new FakeTime()));
        session.SetParagraphStyle(DefaultStyleSheet.Heading1Id);
        session.SelectParagraphAt(Pos(0, 1));
        session.ToggleBold();
        session.ToggleItalic();
        Paragraph heading = session.Document.Para(0);
        Assert.Equal(DefaultStyleSheet.Heading1Id, heading.StyleId);
        ResolvedRunProperties resolved = session.Resolver.ResolveRun(heading, heading.Inlines[0]);
        Assert.True(resolved.Bold);
        Assert.True(resolved.Italic);
        Assert.All(session.SelectionFormats(), f => Assert.True(f.Bold && f.Italic));
    }
}
