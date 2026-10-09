using System.Collections.Immutable;

namespace Quill.Core.Model;

/// <summary>Encoded image bytes plus what is known about them. Immutable; compared by reference.</summary>
public sealed class ImageData
{
    public ImageData(byte[] bytes, string contentType, int pixelWidth = 0, int pixelHeight = 0)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        ArgumentNullException.ThrowIfNull(contentType);
        Bytes = bytes;
        ContentType = contentType;
        PixelWidth = pixelWidth;
        PixelHeight = pixelHeight;
    }

    /// <summary>The encoded file (PNG, JPEG, GIF, BMP...). Treat as read-only.</summary>
    public byte[] Bytes { get; }

    public string ContentType { get; }

    public int PixelWidth { get; }

    public int PixelHeight { get; }

    public string FileExtension => ContentType switch
    {
        "image/jpeg" => ".jpg",
        "image/gif" => ".gif",
        "image/bmp" => ".bmp",
        "image/tiff" => ".tif",
        _ => ".png",
    };

    public static string ContentTypeFor(string fileName)
    {
        string extension = Path.GetExtension(fileName).ToLowerInvariant();
        return extension switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".bmp" => "image/bmp",
            ".tif" or ".tiff" => "image/tiff",
            _ => "image/png",
        };
    }
}

/// <summary>All images of a document keyed by id; paragraphs reference ids through <see cref="InlineImage"/>.</summary>
public sealed class ImageStore
{
    public static readonly ImageStore Empty = new(ImmutableDictionary<string, ImageData>.Empty);

    public ImageStore(ImmutableDictionary<string, ImageData> images)
    {
        ArgumentNullException.ThrowIfNull(images);
        Images = images;
    }

    public ImmutableDictionary<string, ImageData> Images { get; }

    public bool IsEmpty => Images.IsEmpty;

    public ImageData? Get(string id) => Images.TryGetValue(id, out ImageData? data) ? data : null;

    public ImageStore With(string id, ImageData data)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(data);
        return new ImageStore(Images.SetItem(id, data));
    }

    public string NewId() => MakeId(Images.Count + 1);

    /// <summary>An id that is unique across documents, so pasted pictures never collide with existing ones.</summary>
    public static string MakeId(int ordinal) => "image" + ordinal.ToString(System.Globalization.CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N")[..8];
}
