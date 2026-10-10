using System.IO;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using Quill.Core.Model;
using Quill.Core.Styles;
using Quill.Core.Units;
using Quill.Layout;
using Quill.Layout.Wpf;

namespace Quill.App.Printing;

/// <summary>
/// Writes the document as a PDF with PDFsharp. Pages are laid out with the same engine as the screen; words are
/// placed at the x positions WPF computed, so line breaks, justification and tab stops match exactly. Only the
/// advances inside a word come from PDFsharp's own font metrics.
/// </summary>
public static class PdfExporter
{
    private const double PointsPerDip = 72.0 / 96.0;

    public static void Export(Document document, StyleResolver resolver, string path)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(resolver);
        ArgumentNullException.ThrowIfNull(path);

        using var cache = new LayoutCache();
        var paginator = new Paginator(new WpfLineFormatter(), cache, new LayoutOptions(PixelsPerDip: 1.0));
        LayoutDocument layout = paginator.Layout(document, resolver);

        using var pdf = new PdfDocument();
        pdf.Info.Title = document.Metadata.Title ?? Path.GetFileNameWithoutExtension(path);
        pdf.Info.Author = document.Metadata.Author ?? string.Empty;
        pdf.Info.Creator = "Quill";

        var fonts = new PdfFontCache();
        using var images = new PdfImageCache();
        foreach (PageLayout page in layout.Pages)
        {
            PdfPage pdfPage = pdf.AddPage();
            pdfPage.Width = XUnitPt.FromPoint(page.Size.Width * PointsPerDip);
            pdfPage.Height = XUnitPt.FromPoint(page.Size.Height * PointsPerDip);
            using XGraphics gfx = XGraphics.FromPdfPage(pdfPage, XGraphicsUnit.Point);
            gfx.ScaleTransform(PointsPerDip); // draw in DIPs, exactly as the layout computed them
            PageRenderer.DrawContent(page, new PdfRenderTarget(gfx, fonts, images));
        }

        string directory = Path.GetDirectoryName(Path.GetFullPath(path)) ?? ".";
        string temp = Path.Combine(directory, "." + Path.GetFileName(path) + "." + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            pdf.Save(temp);
            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp))
            {
                File.Delete(temp);
            }
        }
    }

    /// <summary>Decodes each picture once per export; pictures WPF cannot decode are drawn as placeholders.</summary>
    internal sealed class PdfImageCache : IDisposable
    {
        private readonly Dictionary<ImageData, XImage?> _images = new(ReferenceEqualityComparer.Instance);

        public XImage? Get(ImageData data)
        {
            if (_images.TryGetValue(data, out XImage? cached))
            {
                return cached;
            }

            XImage? image = null;
            try
            {
                image = XImage.FromStream(new MemoryStream(data.Bytes, writable: false));
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                try
                {
                    if (ImageCache.Get(data) is { } bitmap)
                    {
                        image = XImage.FromBitmapSource(bitmap);
                    }
                }
                catch (Exception inner) when (inner is not OutOfMemoryException)
                {
                    image = null;
                }
            }

            _images[data] = image;
            return image;
        }

        public void Dispose()
        {
            foreach (XImage? image in _images.Values)
            {
                image?.Dispose();
            }

            _images.Clear();
        }
    }

    /// <summary>Caches XFont instances per family, size and style; falls back to Arial when a font is not installed.</summary>
    internal sealed class PdfFontCache
    {
        private readonly Dictionary<(string Family, double Size, XFontStyleEx Style), XFont> _fonts = [];
        private readonly XPdfFontOptions _options = new(PdfFontEncoding.Unicode);

        public XFont Get(string family, double emSize, XFontStyleEx style)
        {
            (string, double, XFontStyleEx) key = (family, Math.Round(emSize, 3), style);
            if (_fonts.TryGetValue(key, out XFont? font))
            {
                return font;
            }

            font = Create(family, emSize, style) ?? Create("Arial", emSize, style) ?? new XFont("Arial", emSize);
            _fonts[key] = font;
            return font;
        }

        private XFont? Create(string family, double emSize, XFontStyleEx style)
        {
            try
            {
                return new XFont(family, emSize, style, _options);
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or PdfSharp.PdfSharpException)
            {
                return null;
            }
        }
    }

    /// <summary>Adapts the layout render target to PDFsharp's XGraphics.</summary>
    internal sealed class PdfRenderTarget : IRenderTarget
    {
        private readonly XGraphics _gfx;
        private readonly PdfFontCache _fonts;
        private readonly PdfImageCache _images;
        private readonly Stack<XGraphicsState> _states = new();

        public PdfRenderTarget(XGraphics gfx, PdfFontCache fonts, PdfImageCache images)
        {
            _gfx = gfx;
            _images = images;
            _fonts = fonts;
        }

        public void FillRectangle(RectD rect, DocColor color, double opacity = 1) =>
            _gfx.DrawRectangle(new XSolidBrush(ToColor(color, opacity)), rect.X, rect.Y, rect.Width, rect.Height);

        public void DrawRectangle(RectD rect, DocColor color, double thickness) =>
            _gfx.DrawRectangle(new XPen(ToColor(color), thickness), rect.X, rect.Y, rect.Width, rect.Height);

        public void DrawLine(PointD from, PointD to, DocColor color, double thickness) =>
            _gfx.DrawLine(new XPen(ToColor(color), thickness), from.X, from.Y, to.X, to.Y);

        public void PushClip(RectD rect)
        {
            _states.Push(_gfx.Save());
            _gfx.IntersectClip(new XRect(rect.X, rect.Y, rect.Width, rect.Height));
        }

        public void Pop()
        {
            if (_states.Count > 0)
            {
                _gfx.Restore(_states.Pop());
            }
        }

        public void DrawTextSegments(IEnumerable<TextSegment> segments, PointD origin)
        {
            ArgumentNullException.ThrowIfNull(segments);
            List<TextSegment> list = segments as List<TextSegment> ?? segments.ToList();
            DrawHighlights(list, origin);
            foreach (TextSegment segment in list)
            {
                if (segment.Text.Length == 0)
                {
                    continue;
                }

                ResolvedRunProperties p = segment.Properties;
                double fullSize = p.FontSize.ToDips();
                double emSize = p.VerticalAlignment == VerticalTextAlignment.Baseline ? fullSize : fullSize * 0.65;
                XFontStyleEx style = XFontStyleEx.Regular;
                if (p.Bold)
                {
                    style |= XFontStyleEx.Bold;
                }

                if (p.Italic)
                {
                    style |= XFontStyleEx.Italic;
                }

                if (p.Underline != UnderlineStyle.None)
                {
                    style |= XFontStyleEx.Underline;
                }

                if (p.Strikethrough || p.DoubleStrikethrough)
                {
                    style |= XFontStyleEx.Strikeout;
                }

                double baseline = origin.Y + segment.Baseline;
                if (p.VerticalAlignment == VerticalTextAlignment.Superscript)
                {
                    baseline -= fullSize * 0.33;
                }
                else if (p.VerticalAlignment == VerticalTextAlignment.Subscript)
                {
                    baseline += fullSize * 0.15;
                }

                XFont font = _fonts.Get(p.FontFamily, emSize, style);
                _gfx.DrawString(segment.Text, font, new XSolidBrush(ToColor(p.Color)), new XPoint(origin.X + segment.X, baseline), XStringFormats.BaseLineLeft);
            }
        }

        /// <summary>One rectangle per run of touching segments with the same highlight, so words and the spaces between them form a single band.</summary>
        private void DrawHighlights(List<TextSegment> segments, PointD origin)
        {
            int i = 0;
            while (i < segments.Count)
            {
                TextSegment first = segments[i];
                if (first.Properties.Highlight == HighlightColor.None)
                {
                    i++;
                    continue;
                }

                double left = first.X;
                double right = first.X + first.Width;
                double top = first.Top;
                double bottom = first.Top + first.Height;
                int j = i + 1;
                while (j < segments.Count && segments[j].Properties.Highlight == first.Properties.Highlight && segments[j].X <= right + 0.75)
                {
                    right = Math.Max(right, segments[j].X + segments[j].Width);
                    top = Math.Min(top, segments[j].Top);
                    bottom = Math.Max(bottom, segments[j].Top + segments[j].Height);
                    j++;
                }

                if (FontCatalog.Shared.GetHighlightBrush(first.Properties.Highlight) is { } highlight)
                {
                    System.Windows.Media.Color c = highlight.Color;
                    _gfx.DrawRectangle(new XSolidBrush(XColor.FromArgb(c.A, c.R, c.G, c.B)), origin.X + left, origin.Y + top, right - left, bottom - top);
                }

                i = j;
            }
        }

        public void DrawImage(ImageData image, RectD bounds)
        {
            ArgumentNullException.ThrowIfNull(image);
            if (_images.Get(image) is { } picture)
            {
                _gfx.DrawImage(picture, bounds.X, bounds.Y, bounds.Width, bounds.Height);
            }
            else
            {
                _gfx.DrawRectangle(new XPen(XColors.Gray, 1), XBrushes.LightGray, bounds.X, bounds.Y, bounds.Width, bounds.Height);
            }
        }

        private static XColor ToColor(DocColor color, double opacity = 1)
        {
            if (color.IsAuto)
            {
                return XColor.FromArgb((int)Math.Round(255 * opacity), 0, 0, 0);
            }

            return XColor.FromArgb((int)Math.Round(color.A * opacity), color.R, color.G, color.B);
        }
    }
}
