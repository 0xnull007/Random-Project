using System.Collections.Immutable;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using Quill.Core.Model;
using Quill.Core.Styles;
using Quill.Core.Units;
using Xunit;

namespace Quill.Docx.Tests;

public class DocxImageTests
{
    private static readonly byte[] Png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==");

    private static Document WithPictures()
    {
        ImageStore images = ImageStore.Empty.With("pic1", new ImageData(Png, "image/png", 1, 1));
        var body = new Paragraph([new Run("Before "), new InlineImage("pic1", Twips.FromInches(2), Twips.FromInches(1)), new Run(" after")]);
        var header = ImmutableList.Create<Block>(new Paragraph([new InlineImage("pic1", Twips.FromInches(0.5), Twips.FromInches(0.25))]));
        var section = new Section(SectionProperties.Letter, ImmutableList.Create<Block>(body), new HeaderFooterSet(header, null, null), HeaderFooterSet.Empty);
        return new Document(ImmutableList.Create(section), DefaultStyleSheet.Create(), images: images);
    }

    [Fact]
    public void Pictures_round_trip_through_docx()
    {
        byte[] bytes = DocxWriter.ToBytes(WithPictures());
        using (var stream = new MemoryStream(bytes))
        using (WordprocessingDocument package = WordprocessingDocument.Open(stream, false))
        {
            List<ValidationErrorInfo> errors = new OpenXmlValidator(FileFormatVersions.Office2019).Validate(package).ToList();
            Assert.True(errors.Count == 0, string.Join("\n", errors.Select(e => e.Description + " @ " + e.Path?.XPath)));
            Assert.Single(package.MainDocumentPart!.ImageParts);
            Assert.Single(package.MainDocumentPart.HeaderParts.Single().ImageParts);
        }

        LoadResult result = DocxReader.Read(new MemoryStream(bytes));
        Assert.Empty(result.Warnings);
        var paragraph = (Paragraph)result.Document.Sections[0].Body[0];
        var image = Assert.IsType<InlineImage>(paragraph.Inlines[1]);
        Assert.Equal(Twips.FromInches(2), image.Width);
        Assert.Equal(Twips.FromInches(1), image.Height);
        ImageData? data = result.Document.Images.Get(image.ImageId);
        Assert.NotNull(data);
        Assert.Equal(Png, data.Bytes);
        Assert.Equal("image/png", data.ContentType);

        var headerParagraph = (Paragraph)result.Document.Sections[0].Headers.Get(HeaderFooterVariant.Default)![0];
        var headerImage = Assert.IsType<InlineImage>(headerParagraph.Inlines[0]);
        Assert.Equal(Twips.FromInches(0.5), headerImage.Width);
        Assert.NotNull(result.Document.Images.Get(headerImage.ImageId));
    }

    [Fact]
    public void Alt_text_and_picture_borders_round_trip()
    {
        ImageStore images = ImageStore.Empty.With("pic1", new ImageData(Png, "image/png", 1, 1));
        var dashed = new BorderLine(PageBorderStyle.Dashed, 12, DocColor.FromRgb(0x2F, 0x54, 0x96), 0);
        var solid = new BorderLine(PageBorderStyle.Single, 24, null, 0);
        var body = ImmutableList.Create<Block>(
            new Paragraph([new InlineImage("pic1", Twips.FromInches(1), Twips.FromInches(1), altText: "A blue square", border: dashed)]),
            new Paragraph([new InlineImage("pic1", Twips.FromInches(1), Twips.FromInches(1), anchor: ImageAnchor.Default, altText: "Floating", border: solid)]),
            new Paragraph([new InlineImage("pic1", Twips.FromInches(1), Twips.FromInches(1))]));
        var document = new Document(ImmutableList.Create(new Section(SectionProperties.Letter, body)), DefaultStyleSheet.Create(), images: images);

        byte[] bytes = DocxWriter.ToBytes(document);
        using (var stream = new MemoryStream(bytes))
        using (WordprocessingDocument package = WordprocessingDocument.Open(stream, false))
        {
            List<ValidationErrorInfo> errors = new OpenXmlValidator(FileFormatVersions.Office2019).Validate(package).ToList();
            Assert.True(errors.Count == 0, string.Join("\n", errors.Select(e => e.Description + " @ " + e.Path?.XPath)));
            List<DocumentFormat.OpenXml.Drawing.Wordprocessing.DocProperties> docPrs = package.MainDocumentPart!.Document!.Descendants<DocumentFormat.OpenXml.Drawing.Wordprocessing.DocProperties>().ToList();
            Assert.Equal("A blue square", docPrs[0].Description!.Value);
            Assert.Null(docPrs[2].Description);
            Assert.Equal(19050, package.MainDocumentPart.Document.Descendants<DocumentFormat.OpenXml.Drawing.Outline>().First().Width!.Value); // 1.5 pt
        }

        LoadResult result = DocxReader.Read(new MemoryStream(bytes));
        Assert.Empty(result.Warnings);
        var first = (InlineImage)((Paragraph)result.Document.Sections[0].Body[0]).Inlines[0];
        Assert.Equal("A blue square", first.AltText);
        Assert.Equal(dashed, first.Border);
        var second = (InlineImage)((Paragraph)result.Document.Sections[0].Body[1]).Inlines[0];
        Assert.Equal("Floating", second.AltText);
        Assert.Equal(solid with { Color = DocColor.Black }, second.Border);
        Assert.True(second.IsFloating);
        var third = (InlineImage)((Paragraph)result.Document.Sections[0].Body[2]).Inlines[0];
        Assert.Null(third.AltText);
        Assert.Null(third.Border);
    }

    [Fact]
    public void Floating_pictures_round_trip_with_their_anchors()
    {
        ImageAnchor[] anchors =
        [
            new ImageAnchor { HorizontalAlignment = ImageHorizontalAlignment.Right, HorizontalRelativeTo = HorizontalAnchor.Margin, OffsetY = new Twips(720), DistanceX = new Twips(90), DistanceY = new Twips(45) },
            new ImageAnchor { Wrap = ImageWrap.TopAndBottom, OffsetX = new Twips(1440), VerticalRelativeTo = VerticalAnchor.Page, OffsetY = new Twips(2880), DistanceX = Twips.Zero },
            new ImageAnchor { Wrap = ImageWrap.BehindText, HorizontalRelativeTo = HorizontalAnchor.Page, HorizontalAlignment = ImageHorizontalAlignment.Center, VerticalRelativeTo = VerticalAnchor.Margin },
            new ImageAnchor { Wrap = ImageWrap.InFrontOfText, OffsetX = new Twips(-360), OffsetY = new Twips(-180) },
        ];
        ImageStore images = ImageStore.Empty.With("pic1", new ImageData(Png, "image/png", 1, 1));
        ImmutableList<Block> body = anchors
            .Select(a => (Block)new Paragraph([new Run("Text "), new InlineImage("pic1", Twips.FromInches(1), Twips.FromInches(1), anchor: a), new Run(" more")]))
            .ToImmutableList();
        var document = new Document(ImmutableList.Create(new Section(SectionProperties.Letter, body)), DefaultStyleSheet.Create(), images: images);

        byte[] bytes = DocxWriter.ToBytes(document);
        using (var stream = new MemoryStream(bytes))
        using (WordprocessingDocument package = WordprocessingDocument.Open(stream, false))
        {
            List<ValidationErrorInfo> errors = new OpenXmlValidator(FileFormatVersions.Office2019).Validate(package).ToList();
            Assert.True(errors.Count == 0, string.Join("\n", errors.Select(e => e.Description + " @ " + e.Path?.XPath)));
            Assert.Equal(4, package.MainDocumentPart!.Document!.Descendants<DocumentFormat.OpenXml.Drawing.Wordprocessing.Anchor>().Count());
        }

        LoadResult result = DocxReader.Read(new MemoryStream(bytes));
        Assert.Empty(result.Warnings);
        for (int i = 0; i < anchors.Length; i++)
        {
            var paragraph = (Paragraph)result.Document.Sections[0].Body[i];
            var image = Assert.IsType<InlineImage>(paragraph.Inlines[1]);
            Assert.Equal(anchors[i], image.Anchor);
            Assert.Equal(Twips.FromInches(1), image.Width);
        }
    }
}
