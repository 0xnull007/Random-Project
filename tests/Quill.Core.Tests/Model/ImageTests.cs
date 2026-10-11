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
    public void Selected_picture_can_float_and_return_to_the_line()
    {
        var session = new EditingSession(WithParagraphs("ab"), new UndoStack(time: new FakeTime()));
        session.MoveCaret(Pos(0, 1), extend: false);
        session.InsertImage(new ImageData(Png, "image/png"), new Twips(1440), new Twips(720));

        session.SetImageAnchor(ImageAnchor.Aligned(ImageHorizontalAlignment.Right) with { Wrap = ImageWrap.TopAndBottom });
        var image = Assert.IsType<InlineImage>(session.Document.Para(0).Inlines[1]);
        Assert.True(image.IsFloating);
        Assert.Equal(ImageWrap.TopAndBottom, image.Anchor!.Wrap);
        Assert.Equal(ImageHorizontalAlignment.Right, image.Anchor.HorizontalAlignment);
        Assert.Equal("a￼b", session.Document.Para(0).FlatText);
        Assert.NotNull(session.SelectedImage());

        session.SetImageWrap(ImageWrap.Square);
        Assert.Equal(ImageWrap.Square, ((InlineImage)session.Document.Para(0).Inlines[1]).Anchor!.Wrap);

        session.Undo();
        Assert.Equal(ImageWrap.TopAndBottom, ((InlineImage)session.Document.Para(0).Inlines[1]).Anchor!.Wrap);

        session.SetImageAnchor(null);
        Assert.False(((InlineImage)session.Document.Para(0).Inlines[1]).IsFloating);
        Assert.NotNull(session.SelectedImage());
    }

    [Fact]
    public void Dragging_a_floating_picture_is_one_undo_step()
    {
        var session = new EditingSession(WithParagraphs("ab"), new UndoStack(time: new FakeTime()));
        session.MoveCaret(Pos(0, 1), extend: false);
        session.InsertImage(new ImageData(Png, "image/png"), new Twips(1440), new Twips(720));
        session.SetImageAnchor(ImageAnchor.Default);

        session.PreviewImageAnchor(Pos(0, 1), ImageAnchor.Default with { OffsetX = new Twips(100) });
        session.PreviewImageAnchor(Pos(0, 1), ImageAnchor.Default with { OffsetX = new Twips(200), OffsetY = new Twips(50) });
        session.EndImageDrag();
        ImageAnchor moved = ((InlineImage)session.Document.Para(0).Inlines[1]).Anchor!;
        Assert.Equal(new Twips(200), moved.OffsetX);
        Assert.Equal(new Twips(50), moved.OffsetY);

        session.Undo();
        ImageAnchor before = ((InlineImage)session.Document.Para(0).Inlines[1]).Anchor!;
        Assert.Equal(Twips.Zero, before.OffsetX);
        Assert.Equal(Twips.Zero, before.OffsetY);

        session.Undo();
        Assert.False(((InlineImage)session.Document.Para(0).Inlines[1]).IsFloating);
    }

    [Fact]
    public void Dragging_a_picture_handle_is_one_undo_step_and_can_move_a_floating_picture()
    {
        var session = new EditingSession(WithParagraphs("ab"), new UndoStack(time: new FakeTime()));
        session.MoveCaret(Pos(0, 1), extend: false);
        session.InsertImage(new ImageData(Png, "image/png"), new Twips(1440), new Twips(720));

        session.PreviewImageSize(Pos(0, 1), new Twips(1000), new Twips(500));
        session.PreviewImageSize(Pos(0, 1), new Twips(720), new Twips(360));
        session.EndImageDrag();
        var image = Assert.IsType<InlineImage>(session.Document.Para(0).Inlines[1]);
        Assert.Equal(new Twips(720), image.Width);
        Assert.Equal(new Twips(360), image.Height);
        Assert.Equal(new Selection(Pos(0, 1), Pos(0, 2)), session.Selection);

        session.Undo();
        Assert.Equal(new Twips(1440), ((InlineImage)session.Document.Para(0).Inlines[1]).Width);

        // A floating picture resized from its left handle keeps its right edge: the offset moves with the width.
        session.SetImageAnchor(ImageAnchor.Default with { OffsetX = new Twips(500) });
        session.PreviewImageSize(Pos(0, 1), new Twips(1000), new Twips(500), ImageAnchor.Default with { OffsetX = new Twips(940) });
        session.EndImageDrag();
        image = (InlineImage)session.Document.Para(0).Inlines[1];
        Assert.Equal(new Twips(1000), image.Width);
        Assert.Equal(new Twips(940), image.Anchor!.OffsetX);

        // Sizes never collapse below a point.
        session.PreviewImageSize(Pos(0, 1), Twips.Zero, new Twips(-50));
        session.EndImageDrag();
        image = (InlineImage)session.Document.Para(0).Inlines[1];
        Assert.Equal(new Twips(20), image.Width);
        Assert.Equal(new Twips(20), image.Height);
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

public class MetadataTests
{
    [Fact]
    public void Setting_properties_is_undoable_and_marks_the_document_dirty()
    {
        var session = new EditingSession(WithParagraphs("x"), new UndoStack(time: new FakeTime()));
        Assert.False(session.IsDirty);
        session.SetMetadata(session.Document.Metadata with { Title = "Report", Author = "Me" });
        Assert.Equal("Report", session.Document.Metadata.Title);
        Assert.True(session.IsDirty);
        session.Undo();
        Assert.Null(session.Document.Metadata.Title);
        Assert.False(session.IsDirty);
    }
}
