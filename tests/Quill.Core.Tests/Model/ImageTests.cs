using Quill.Core.Editing;
using Quill.Core.Model;
using Quill.Core.Tests.Support;
using Quill.Core.Text;
using Quill.Core.Units;
using Xunit;
using static Quill.Core.Tests.Support.TestDocuments;

namespace Quill.Core.Tests.Model;

public class ImageTests
{
    private static readonly byte[] Png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==");

    [Fact]
    public void Inline_image_occupies_one_character_and_lives_in_the_store()
    {
        var session = new EditingSession(WithParagraphs("ab"), new UndoStack(time: new FakeTime()));
        session.MoveCaret(Pos(0, 1), extend: false);
        session.InsertImage(new ImageData(Png, "image/png", 1, 1), Twips.FromInches(2), Twips.FromInches(1));

        Paragraph paragraph = session.Document.Para(0);
        Assert.Equal("a￼b", paragraph.FlatText);
        var image = Assert.IsType<InlineImage>(paragraph.Inlines[1]);
        Assert.Equal(Twips.FromInches(2), image.Width);
        Assert.NotNull(session.Document.Images.Get(image.ImageId));
        Assert.Equal("image/png", session.Document.Images.Get(image.ImageId)!.ContentType);
        Assert.Equal(Pos(0, 2), session.Selection.Active);

        session.Undo();
        Assert.Equal("ab", session.Document.Text(0));
        Assert.True(session.Document.Images.IsEmpty);
    }

    [Fact]
    public void Copying_a_picture_carries_its_bytes_into_another_document()
    {
        var source = new EditingSession(WithParagraphs(""), new UndoStack(time: new FakeTime()));
        source.InsertImage(new ImageData(Png, "image/png"), new Twips(1440), new Twips(720));
        source.SelectAll();
        DocumentFragment fragment = source.Copy();
        Assert.Single(fragment.Images);

        var target = new EditingSession(WithParagraphs("x"), new UndoStack(time: new FakeTime()));
        target.MoveCaret(Pos(0, 1), extend: false);
        target.Paste(fragment);
        var image = Assert.IsType<InlineImage>(target.Document.Para(0).Inlines[1]);
        Assert.Same(fragment.Images[image.ImageId], target.Document.Images.Get(image.ImageId));
    }

    [Fact]
    public void Selected_picture_can_be_resized_and_undone()
    {
        var session = new EditingSession(WithParagraphs("ab"), new UndoStack(time: new FakeTime()));
        session.MoveCaret(Pos(0, 1), extend: false);
        session.InsertImage(new ImageData(Png, "image/png"), new Twips(1440), new Twips(720));
        Assert.NotNull(session.SelectedImage());

        session.ResizeImage(new Twips(720), new Twips(360));
        var image = Assert.IsType<InlineImage>(session.Document.Para(0).Inlines[1]);
        Assert.Equal(new Twips(720), image.Width);
        Assert.Equal(new Twips(360), image.Height);
        Assert.Equal(new Selection(Pos(0, 1), Pos(0, 2)), session.Selection);

        session.MoveCaret(Pos(0, 0), extend: false);
        Assert.Null(session.SelectedImage());

        session.Undo();
        Assert.Equal(new Twips(1440), Assert.IsType<InlineImage>(session.Document.Para(0).Inlines[1]).Width);
    }

    [Fact]
    public void Deleting_the_image_character_removes_the_inline()

    {
        var session = new EditingSession(WithParagraphs(""), new UndoStack(time: new FakeTime()));
        session.InsertImage(new ImageData(Png, "image/png"), new Twips(1440), new Twips(1440));
        session.Backspace();
        Assert.Equal("", session.Document.Text(0));
        Assert.Equal(".jpg", new ImageData(Png, "image/jpeg").FileExtension);
        Assert.Equal("image/jpeg", ImageData.ContentTypeFor("photo.JPG"));
    }
}
