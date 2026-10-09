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
}
