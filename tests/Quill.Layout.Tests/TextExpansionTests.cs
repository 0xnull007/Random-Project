using Quill.Core.Model;
using Quill.Core.Styles;
using Quill.Core.Units;
using Quill.Layout;
using Xunit;

namespace Quill.Layout.Tests;

public class TextExpansionTests
{
    private static TextExpansion Build(Paragraph paragraph, FieldValues? fields = null)
    {
        ParagraphLayoutInput input = ParagraphLayoutInput.Create(paragraph, new StyleResolver(StyleSheet.Empty), fields ?? FieldValues.Placeholder, 500, Twips.FromInches(0.5), 1.0);
        return TextExpansion.Build(input);
    }

    [Fact]
    public void Plain_text_maps_one_to_one()
    {
        TextExpansion expansion = Build(Paragraph.FromText("hello"));
        Assert.Equal("hello", expansion.LayoutText);
        Assert.Equal(3, expansion.ToLayout(3));
        Assert.Equal(3, expansion.ToFlat(3));
        Assert.Equal(5, expansion.ToLayout(99));
        Assert.Equal(5, expansion.ToFlat(99));
    }

    [Fact]
    public void Fields_expand_to_their_values_and_snap_offsets()
    {
        var paragraph = new Paragraph([new Run("p"), Field.Page(), new Run("/"), Field.NumPages()]);
        TextExpansion expansion = Build(paragraph, new FieldValues("12", "345", "1"));
        Assert.Equal("p12/345", expansion.LayoutText);
        Assert.Equal(4, expansion.FlatLength);
        Assert.Equal(1, expansion.ToLayout(1));   // before PAGE
        Assert.Equal(3, expansion.ToLayout(2));   // after PAGE
        Assert.Equal(4, expansion.ToLayout(3));   // after "/"
        Assert.Equal(7, expansion.ToLayout(4));   // end
        Assert.Equal(1, expansion.ToFlat(1));
        Assert.Equal(1, expansion.ToFlat(2));     // inside "12", nearer the start
        Assert.Equal(2, expansion.ToFlat(3));
        Assert.Equal(3, expansion.ToFlat(4));
        Assert.Equal(4, expansion.ToFlat(6));     // deep inside "345": snaps to the end
        Assert.Equal(4, expansion.ToFlat(7));
        (RunSpan run, int end) = expansion.RunAtLayout(5);
        Assert.Equal(RunKind.Field, run.Kind);
        Assert.Equal(7, end);
    }

    [Fact]
    public void All_caps_runs_are_upper_cased_without_changing_length()
    {
        var paragraph = new Paragraph([new Run("ab "), new Run("shout", new RunProperties { AllCaps = true }), new Run(" cd")]);
        TextExpansion expansion = Build(paragraph);
        Assert.Equal("ab SHOUT cd", expansion.LayoutText);
        Assert.Equal(paragraph.Length, expansion.LayoutLength);
        Assert.Equal(6, expansion.ToFlat(6));
    }

    [Fact]
    public void Empty_field_results_do_not_swallow_following_text()
    {
        var paragraph = new Paragraph([new Field(FieldKind.Unknown, "X", string.Empty), new Run("tail")]);
        TextExpansion expansion = Build(paragraph);
        Assert.Equal("tail", expansion.LayoutText);
        (RunSpan run, _) = expansion.RunAtLayout(0);
        Assert.Equal(RunKind.Text, run.Kind);
        Assert.Equal(0, expansion.ToFlat(0));
        Assert.Equal(0, expansion.ToLayout(1));
    }
}
