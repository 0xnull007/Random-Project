using Quill.Core.Units;

namespace Quill.Layout.Wpf;

/// <summary>Paints one laid-out page (body, header, footer, placeholders) onto a render target. Shared by the view, print and preview.</summary>
public static class PageRenderer
{
    private static readonly DocColor PlaceholderFill = DocColor.FromRgb(0xF3, 0xF3, 0xF3);
    private static readonly DocColor PlaceholderStroke = DocColor.FromRgb(0xA0, 0xA0, 0xA0);

    public static void DrawContent(PageLayout page, IRenderTarget target)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(target);
        foreach (BlockFragment fragment in page.AllFragments())
        {
            switch (fragment)
            {
                case ParagraphFragment paragraph:
                    DrawParagraph(paragraph, target);
                    break;
                case OpaqueFragment opaque:
                    target.FillRectangle(opaque.Bounds, PlaceholderFill);
                    target.DrawRectangle(opaque.Bounds, PlaceholderStroke, 1);
                    break;
            }
        }
    }

    public static void DrawParagraph(ParagraphFragment fragment, IRenderTarget target)
    {
        ArgumentNullException.ThrowIfNull(fragment);
        ArgumentNullException.ThrowIfNull(target);
        if (fragment.IsParagraphStart && fragment.Layout.MarkerLine is { } marker)
        {
            PointD first = fragment.LineOrigin(fragment.FirstLine);
            double y = first.Y + (fragment.Layout.Lines[fragment.FirstLine].Baseline - marker.Baseline);
            marker.Draw(target, new PointD(fragment.Bounds.Left + fragment.Layout.MarkerX, y));
        }

        for (int i = fragment.FirstLine; i <= fragment.LastLine; i++)
        {
            fragment.Layout.Lines[i].Draw(target, fragment.LineOrigin(i));
        }
    }
}
