using System.Collections.Immutable;
using Quill.Core.Editing;
using Quill.Core.Model;
using Quill.Core.Styles;
using Xunit;
using static Quill.Core.Tests.Support.TestDocuments;

namespace Quill.Core.Tests.Editing;

public class MultilevelListTests
{
    private static IReadOnlyList<string> Markers(EditingSession session)
    {
        Document document = session.Document;
        var resolver = new StyleResolver(document.Styles, document.Lists);
        IReadOnlyDictionary<int, ListMarker> markers = ListNumbering.Compute(document.Sections[0].Body, document.Lists, resolver);
        return Enumerable.Range(0, document.Sections[0].Body.Count).Select(i => markers.TryGetValue(i, out ListMarker m) ? m.Text : string.Empty).ToList();
    }

    [Fact]
    public void A_preset_turns_the_selected_paragraphs_into_a_nested_list_with_the_right_markers()
    {
        var session = new EditingSession(WithParagraphs("One", "Two", "Three", "Outside"));
        session.MoveCaret(Pos(0, 0), extend: false);
        session.MoveCaret(Pos(2, 5), extend: true);
        session.ApplyMultilevelList("1 1.1 1.1.1");
        session.MoveCaret(Pos(1, 0), extend: false);
        session.ChangeIndent(1);
        session.MoveCaret(Pos(2, 0), extend: false);
        session.ChangeIndent(1);
        session.ChangeIndent(1);

        Assert.Equal(["1", "1.1", "1.1.1", ""], Markers(session));
        Assert.Equal(2, session.Document.Para(2).Properties.List!.Value.Level);

        // Switching the outline keeps the levels and moves the whole list, not just the caret paragraph.
        session.ApplyMultilevelList("I. A. 1. a) (1)");
        Assert.Equal(["I.", "A.", "1.", ""], Markers(session));
        int id = session.Document.Para(0).Properties.List!.Value.NumberingId;
        Assert.All(new[] { 0, 1, 2 }, i => Assert.Equal(id, session.Document.Para(i).Properties.List!.Value.NumberingId));

        session.ApplyMultilevelList("❖ ➢ ▪ bullets");
        Assert.Equal(["❖", "➢", "▪", ""], Markers(session));
        session.Undo();
        Assert.Equal(["I.", "A.", "1.", ""], Markers(session));

        Document before = session.Document;
        session.ApplyMultilevelList("no such preset");
        Assert.Same(before, session.Document);
        Assert.Equal(6, MultilevelLists.Presets.Length);
    }
}
