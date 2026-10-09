using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows.Media.Imaging;
using Quill.Core.Model;

namespace Quill.Layout.Wpf;

/// <summary>Decodes picture bytes once per <see cref="ImageData"/> instance; the decoded bitmap dies with the data.</summary>
public static class ImageCache
{
    private static readonly ConditionalWeakTable<ImageData, Entry> s_entries = new();

    /// <summary>The frozen bitmap for the picture, or null when WPF cannot decode it (EMF, WMF, corrupt data).</summary>
    public static BitmapSource? Get(ImageData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        return s_entries.GetValue(data, Decode).Bitmap;
    }

    private static Entry Decode(ImageData data)
    {
        try
        {
            using var stream = new MemoryStream(data.Bytes, writable: false);
            BitmapDecoder decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat | BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.OnLoad);
            BitmapFrame frame = decoder.Frames[0];
            if (frame.CanFreeze)
            {
                frame.Freeze();
            }

            return new Entry(frame);
        }
        catch (Exception ex) when (ex is NotSupportedException or FileFormatException or IOException or ArgumentException or InvalidOperationException or COMException or OverflowException)
        {
            return new Entry(null);
        }
    }

    private sealed class Entry(BitmapSource? bitmap)
    {
        public BitmapSource? Bitmap { get; } = bitmap;
    }
}
