using System.IO;
using System.Windows.Media.Imaging;
using Quill.Core.Model;
using Quill.Core.Units;

namespace Quill.App.Imaging;

/// <summary>A picture ready to insert: its bytes and its natural size on paper.</summary>
public readonly record struct PictureSource(ImageData Data, Twips Width, Twips Height);

/// <summary>Loads picture files and clipboard bitmaps into <see cref="ImageData"/>.</summary>
public static class ImageFiles
{
    public const string Filter = "Pictures|*.png;*.jpg;*.jpeg;*.gif;*.bmp;*.tif;*.tiff;*.webp;*.ico|PNG|*.png|JPEG|*.jpg;*.jpeg|GIF|*.gif|Bitmap|*.bmp|TIFF|*.tif;*.tiff|All files|*.*";

    private static readonly HashSet<string> NativeExtensions = new(StringComparer.OrdinalIgnoreCase) { ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".tif", ".tiff" };
    private static readonly HashSet<string> PictureExtensions = new(NativeExtensions, StringComparer.OrdinalIgnoreCase) { ".webp", ".ico" };

    public static bool IsPictureFile(string path) => PictureExtensions.Contains(Path.GetExtension(path));

    /// <summary>Reads a picture file; formats Word does not store (WebP, ICO) are re-encoded as PNG.</summary>
    public static PictureSource Load(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        byte[] bytes = File.ReadAllBytes(path);
        BitmapFrame frame = Decode(bytes);
        if (!NativeExtensions.Contains(Path.GetExtension(path)))
        {
            return FromBitmap(frame);
        }

        (Twips width, Twips height) = NaturalSize(frame);
        return new PictureSource(new ImageData(bytes, ImageData.ContentTypeFor(path), frame.PixelWidth, frame.PixelHeight), width, height);
    }

    /// <summary>Encodes a bitmap (for example from the clipboard) as PNG.</summary>
    public static PictureSource FromBitmap(BitmapSource bitmap)
    {
        ArgumentNullException.ThrowIfNull(bitmap);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var memory = new MemoryStream();
        encoder.Save(memory);
        (Twips width, Twips height) = NaturalSize(bitmap);
        return new PictureSource(new ImageData(memory.ToArray(), "image/png", bitmap.PixelWidth, bitmap.PixelHeight), width, height);
    }

    /// <summary>Shrinks a size proportionally so that it is no wider than <paramref name="maxWidth"/>.</summary>
    public static (Twips Width, Twips Height) FitWithin(Twips width, Twips height, Twips maxWidth)
    {
        if (width.Value <= maxWidth.Value || width.Value <= 0)
        {
            return (width, height);
        }

        double scale = maxWidth.Value / (double)width.Value;
        return (maxWidth, new Twips(Math.Max(1, (int)Math.Round(height.Value * scale))));
    }

    /// <summary>The size a picture has at its own resolution (96 DPI when the file does not say), or null when it cannot be decoded.</summary>
    public static (Twips Width, Twips Height)? NaturalSize(ImageData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (Quill.Layout.Wpf.ImageCache.Get(data) is { } bitmap)
        {
            return NaturalSize(bitmap);
        }

        if (data.PixelWidth > 0 && data.PixelHeight > 0)
        {
            return (Twips.FromInches(data.PixelWidth / 96.0), Twips.FromInches(data.PixelHeight / 96.0));
        }

        return null;
    }

    private static BitmapFrame Decode(byte[] bytes)

    {
        using var stream = new MemoryStream(bytes, writable: false);
        BitmapDecoder decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat | BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.OnLoad);
        return decoder.Frames[0];
    }

    private static (Twips Width, Twips Height) NaturalSize(BitmapSource bitmap)
    {
        double dpiX = bitmap.DpiX > 1 ? bitmap.DpiX : 96;
        double dpiY = bitmap.DpiY > 1 ? bitmap.DpiY : 96;
        return (Twips.FromInches(bitmap.PixelWidth / dpiX), Twips.FromInches(bitmap.PixelHeight / dpiY));
    }
}
