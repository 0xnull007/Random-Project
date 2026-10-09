using System.Globalization;
using Quill.Core.Editing;
using Quill.Core.Model;
using Quill.Core.Tests.Support;
using Quill.Core.Text;
using Xunit;
using static Quill.Core.Tests.Support.TestDocuments;

namespace Quill.Core.Tests.Text;

public class TextCaseTests
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    [Theory]
    [InlineData(CaseChange.Lower, "Hello WORLD. it's me", "hello world. it's me")]
    [InlineData(CaseChange.Upper, "Hello world", "HELLO WORLD")]
    [InlineData(CaseChange.TitleCase, "hello wORLD, it's o'neil 3rd", "Hello World, It's O'neil 3rd")]
    [InlineData(CaseChange.Sentence, "hello WORLD. this is IT! really? yes", "Hello world. This is it! Really? Yes")]
    [InlineData(CaseChange.Toggle, "Hello World", "hELLO wORLD")]
    public void Transforms_keep_the_length(CaseChange change, string input, string expected)
    {
        string result = TextCase.Apply(input, change, Invariant);
        Assert.Equal(expected, result);
        Assert.Equal(input.Length, result.Length);
    }

    [Fact]
    public void Cycle_goes_lower_upper_title()
    {
        Assert.Equal(CaseChange.Upper, TextCase.NextInCycle("hello world"));
        Assert.Equal(CaseChange.TitleCase, TextCase.NextInCycle("HELLO WORLD"));
        Assert.Equal(CaseChange.Lower, TextCase.NextInCycle("Hello World"));
        Assert.Equal(CaseChange.Upper, TextCase.NextInCycle("hELLO"));
    }

    [Fact]
    public void Changing_case_keeps_run_formatting_and_selection()
    {
        var paragraph = new Paragraph([new Run("one "), new Run("two", new RunProperties { Bold = true }), new Run(" three")]);
        var session = new EditingSession(WithBlocks(paragraph), new UndoStack(time: new FakeTime()));
        session.MoveCaret(Pos(0, 2), extend: false);
        session.MoveCaret(Pos(0, 9), extend: true);
        session.ChangeCase(CaseChange.Upper);

        Paragraph result = session.Document.Para(0);
        Assert.Equal("onE TWO Three", result.FlatText);
        Assert.Equal(3, result.Inlines.Length);
        Assert.True(result.Inlines[1].Properties.Bold);
        Assert.Equal(Pos(0, 9), session.Selection.Active);

        session.MoveCaret(Pos(0, 1), extend: false);
        session.CycleCase();
        Assert.Equal("ONE TWO Three", session.Document.Text(0));
        session.Undo();
        Assert.Equal("onE TWO Three", session.Document.Text(0));
    }
}
