using System.Printing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using Quill.Core.Model;
using Quill.Core.Styles;
using Quill.Layout;
using Quill.Layout.Wpf;
using Orientation = Quill.Core.Model.Orientation;

namespace Quill.App.Printing;

/// <summary>Prints a document through the standard WPF print dialog using the same renderer as the screen.</summary>
public static class PrintService
{
    public static bool Print(Document document, StyleResolver resolver, string title)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(resolver);

        var dialog = new PrintDialog { UserPageRangeEnabled = true };
        SectionProperties first = document.Sections[0].Properties;
        dialog.PrintTicket.PageMediaSize = new PageMediaSize(first.PageWidth.ToDips(), first.PageHeight.ToDips());
        dialog.PrintTicket.PageOrientation = first.Orientation == Orientation.Landscape ? PageOrientation.Landscape : PageOrientation.Portrait;
        if (dialog.ShowDialog() != true)
        {
            return false;
        }

        using var cache = new LayoutCache();
        var paginator = new Paginator(new WpfLineFormatter(), cache, new LayoutOptions(PixelsPerDip: 1.0));
        LayoutDocument layout = paginator.Layout(document, resolver);
        int from = 0;
        int to = layout.PageCount - 1;
        if (dialog.PageRangeSelection == PageRangeSelection.UserPages)
        {
            from = Math.Clamp(dialog.PageRange.PageFrom - 1, 0, to);
            to = Math.Clamp(dialog.PageRange.PageTo - 1, from, to);
        }

        dialog.PrintDocument(new LayoutDocumentPaginator(layout, from, to), title);
        return true;
    }

    /// <summary>Exposes laid-out pages to the XPS/print pipeline, one DrawingVisual per page.</summary>
    private sealed class LayoutDocumentPaginator(LayoutDocument layout, int first, int last) : DocumentPaginator
    {
        public override bool IsPageCountValid => true;

        public override int PageCount => last - first + 1;

        public override Size PageSize
        {
            get
            {
                SizeD size = layout.Pages[first].Size;
                return new Size(size.Width, size.Height);
            }
            set
            {
            }
        }

        public override IDocumentPaginatorSource? Source => null;

        public override DocumentPage GetPage(int pageNumber)
        {
            PageLayout page = layout.Pages[first + pageNumber];
            var visual = new DrawingVisual();
            using (DrawingContext dc = visual.RenderOpen())
            {
                PageRenderer.DrawContent(page, new WpfRenderTarget(dc));
            }

            var size = new Size(page.Size.Width, page.Size.Height);
            return new DocumentPage(visual, size, new Rect(size), new Rect(size));
        }
    }
}
