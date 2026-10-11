using System.Globalization;
using Quill.Core.Editing;
using Quill.Core.Model;
using Xunit;
using static Quill.Core.Tests.Support.TestDocuments;

namespace Quill.Core.Tests.Editing;

public class FieldTests
{
    [Fact]
    public void Date_pictures_convert_between_word_and_dotnet_and_format_the_given_moment()
    {
        var culture = CultureInfo.InvariantCulture;
        Assert.Equal("DATE \\@ \"d MMMM yyyy\"", FieldSupport.DateInstruction("d MMMM yyyy", time: false));
        Assert.Equal("TIME", FieldSupport.DateInstruction("", time: true));
        Assert.Equal("h:mm tt", FieldSupport.ToDotNetFormat("h:mm am/pm"));
        Assert.Equal("h:mm am/pm", FieldSupport.ToWordPicture("h:mm tt", culture));
        Assert.Equal(culture.DateTimeFormat.LongDatePattern, FieldSupport.ToWordPicture("D", culture));

        var field = new Field(FieldKind.Date, "DATE \\@ \"yyyy-MM-dd\"", "old");
        var when = new DateTime(2026, 10, 11, 14, 5, 0);
        Assert.Equal("yyyy-MM-dd", FieldSupport.Picture(field));
        Assert.Equal("2026-10-11", FieldSupport.FormatDate(field, when, culture));
        Assert.Equal("2026-10-11", FieldSupport.Resolve(field, when, DocumentMetadata.Empty));
        Assert.Equal(when.ToString("t", CultureInfo.CurrentCulture), FieldSupport.FormatDate(new Field(FieldKind.Time, "TIME", ""), when));

        var author = new Field(FieldKind.DocProperty, "AUTHOR", "cached");
        Assert.Equal("Ada", FieldSupport.DocumentPropertyText(author, new DocumentMetadata { Author = "Ada" }));
        Assert.Equal("cached", FieldSupport.Resolve(author, when, DocumentMetadata.Empty));
    }

    [Fact]
    public void Inserted_fields_carry_the_current_text_and_f9_refreshes_them()
    {
        var session = new EditingSession(WithParagraphs("By "));
        session.MoveCaret(Pos(0, 3), extend: false);
        session.InsertDocumentPropertyField("AUTHOR");
        var author = (Field)session.Document.Para(0).Inlines[1];
        Assert.Equal(FieldKind.DocProperty, author.Kind);
        Assert.Equal("AUTHOR", author.Instruction);
        Assert.Equal("", author.CachedResult);

        session.SetMetadata(session.Document.Metadata with { Author = "Grace Hopper" });
        session.UpdateReferenceFields(new Dictionary<string, string>());
        Assert.Equal("Grace Hopper", ((Field)session.Document.Para(0).Inlines[1]).CachedResult);

        session.MoveCaret(Pos(0, session.Document.Para(0).Length), extend: false);
        session.InsertDateField("yyyy", time: false);
        var date = (Field)session.Document.Para(0).Inlines[^1];
        Assert.Equal(FieldKind.Date, date.Kind);
        Assert.Equal("DATE \\@ \"yyyy\"", date.Instruction);
        Assert.Equal(DateTime.Now.Year.ToString(CultureInfo.CurrentCulture), date.CachedResult);
        Assert.Equal(1, date.Length);
    }
}
