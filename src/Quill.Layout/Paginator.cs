using System.Collections.Immutable;
using System.Globalization;
using Quill.Core.Model;
using Quill.Core.Styles;
using Quill.Core.Text;
using Quill.Core.Units;

namespace Quill.Layout;

public sealed record LayoutOptions(double PixelsPerDip = 1.0, int OrphanLines = 2, int WidowLines = 2)
{
    public static readonly LayoutOptions Default = new();
}

/// <summary>
/// Turns a document into pages: formats paragraphs through an <see cref="ILineFormatter"/> (cached by paragraph
/// identity) and fills page bodies honoring page-break-before, keep-with-next, keep-lines-together, widow/orphan
/// control, explicit page breaks, section starts and headers/footers with page-number fields.
/// </summary>
public sealed class Paginator
{
    private readonly ILineFormatter _formatter;
    private readonly LayoutCache _cache;
    private readonly LayoutOptions _options;

    public Paginator(ILineFormatter formatter, LayoutCache cache, LayoutOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(formatter);
        ArgumentNullException.ThrowIfNull(cache);
        _formatter = formatter;
        _cache = cache;
        _options = options ?? LayoutOptions.Default;
    }

    public LayoutDocument Layout(Document document, StyleResolver resolver)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(resolver);

        _cache.BeginSweep();
        try
        {
            // Pass 1 with a page-count hint; pass 2 only if a header/footer field depends on the total.
            var run = new Run(document, resolver, _formatter, _cache, _options, totalPagesHint: 1, sectionPagesHint: null);
            LayoutDocument first = run.Execute();
            if (!run.UsedTotalDependentField || first.PageCount == 1)
            {
                return first;
            }

            var second = new Run(document, resolver, _formatter, _cache, _options, first.PageCount, run.SectionPageCounts);
            return second.Execute();
        }
        finally
        {
            _cache.EndSweep();
        }
    }

    private sealed class Run
    {
        private readonly Document _document;
        private readonly StyleResolver _resolver;
        private readonly ILineFormatter _formatter;
        private readonly LayoutCache _cache;
        private readonly LayoutOptions _options;
        private readonly int _totalPagesHint;
        private readonly IReadOnlyList<int>? _sectionPagesHint;
        private readonly List<PageLayout> _pages = [];
        private readonly List<int> _sectionPageCounts = [];
        private readonly FieldValues _bodyFields;
        private readonly Dictionary<int, StoryLayout> _measuredNotes = [];
        private const double FootnoteSeparatorHeight = 12;


        public Run(Document document, StyleResolver resolver, ILineFormatter formatter, LayoutCache cache, LayoutOptions options, int totalPagesHint, IReadOnlyList<int>? sectionPagesHint)
        {
            _document = document;
            _resolver = resolver;
            _formatter = formatter;
            _cache = cache;
            _options = options;
            _totalPagesHint = totalPagesHint;
            _sectionPagesHint = sectionPagesHint;
            _bodyFields = FieldValues.Placeholder with { FootnoteNumbers = FootnoteSupport.Numbers(document), EndnoteNumbers = FootnoteSupport.EndnoteNumbers(document) };
        }

        public bool UsedTotalDependentField { get; private set; }

        public IReadOnlyList<int> SectionPageCounts => _sectionPageCounts;

        public LayoutDocument Execute()
        {
            int nextPageNumber = 1;
            for (int s = 0; s < _document.Sections.Count; s++)
            {
                Section section = _document.Sections[s];
                SectionProperties props = section.Properties;
                if (s > 0)
                {
                    // Odd/even starts are about the physical sheet (duplex printing), so parity uses the physical
                    // page position. The filler page belongs to the previous section, as in Word.
                    int physicalNext = _pages.Count + 1;
                    bool needsFiller = (props.Start == SectionStart.EvenPage && physicalNext % 2 == 1)
                        || (props.Start == SectionStart.OddPage && physicalNext % 2 == 0);
                    if (needsFiller)
                    {
                        AddBlankPage(s - 1, nextPageNumber, isFirstOfSection: false);
                        nextPageNumber++;
                    }
                }

                if (props.PageNumberStart is { } start)
                {
                    nextPageNumber = start;
                }

                int pagesBefore = _pages.Count;

                nextPageNumber = LayoutSection(s, nextPageNumber);
                _sectionPageCounts.Add(_pages.Count - pagesBefore);
            }

            return new LayoutDocument(_pages.ToImmutableArray(), _document);
        }

        private int LayoutSection(int sectionIndex, int pageNumber)
        {
            Section section = _document.Sections[sectionIndex];
            var body = new List<Placement>();
            foreach ((Block block, int index) in section.Body.Select((b, i) => (b, i)))
            {
                body.Add(new Placement(block, BlockPath.Of(index), StoryId.Body(sectionIndex)));
            }

            if (sectionIndex == _document.Sections.Count - 1)
            {
                bool first = true;
                foreach (int noteId in FootnoteSupport.ReferencedEndnoteIds(_document))
                {
                    ImmutableList<Block> note = _document.Endnotes[noteId];
                    for (int b = 0; b < note.Count; b++)
                    {
                        body.Add(new Placement(note[b], BlockPath.Of(b), StoryId.Endnote(noteId)) { StartsNotes = first && b == 0 });
                        first = false;
                    }
                }
            }

            IReadOnlyDictionary<int, ListMarker> markers = ListNumbering.Compute(section.Body, _document.Lists, _resolver);

            var page = NewPage(sectionIndex, pageNumber, isFirstOfSection: true);
            bool afterHardBreak = true; // document/section start keeps space-before
            int i = 0;
            while (i < body.Count)
            {
                Placement placement = body[i];
                if (placement.StartsNotes)
                {
                    const double separator = 12;
                    if (!page.IsEmpty && page.Y + separator + 20 > page.Column.Bottom)
                    {
                        page = NextColumn(page, ref pageNumber, sectionIndex, ref afterHardBreak);
                        continue;
                    }

                    page.EndnoteSeparatorY = page.Y + separator / 2;
                    page.Y += separator;
                    page.IsEmpty = false;
                    placement.StartsNotes = false;
                }

                if (placement.Block is Table table)

                {
                    TableLayout tableLayout = GetTableLayout(table, page.Column.Width, _bodyFields);
                    if (tableLayout.Rows.IsEmpty)
                    {
                        i++;
                        continue;
                    }

                    int firstRow = placement.NextLine;
                    int headerRows = LeadingHeaderRows(table);
                    int repeated = firstRow > 0 && firstRow >= headerRows ? headerRows : 0;
                    double availableHeight = page.Column.Bottom - page.Y - tableLayout.RowTops[repeated];

                    int rows = 0;
                    while (firstRow + rows < tableLayout.Rows.Length && tableLayout.HeightOfRows(firstRow, firstRow + rows) <= availableHeight + 0.001)
                    {
                        rows++;
                    }

                    if (rows == 0)
                    {
                        if (!page.IsEmpty)
                        {
                            page = NextColumn(page, ref pageNumber, sectionIndex, ref afterHardBreak);
                            continue;
                        }

                        rows = 1; // a row taller than the page is placed anyway
                    }

                    int lastRow = firstRow + rows - 1;
                    var tableFragment = new TableFragment(placement.Story, placement.Path, tableLayout, firstRow, lastRow, page.Y, page.Column.Left, repeated);
                    page.Fragments.Add(tableFragment);
                    page.Y = tableFragment.Bounds.Bottom;
                    page.IsEmpty = false;
                    if (lastRow >= tableLayout.Rows.Length - 1)
                    {
                        placement.NextLine = 0;
                        i++;
                    }
                    else
                    {
                        placement.NextLine = lastRow + 1;
                        page = NextColumn(page, ref pageNumber, sectionIndex, ref afterHardBreak);
                    }

                    continue;
                }

                if (placement.Block is OpaqueBlock opaque)

                {
                    double height = OpaqueFragment.PlaceholderHeight;
                    if (!page.IsEmpty && page.Y + height > page.Column.Bottom)
                    {
                        page = NextColumn(page, ref pageNumber, sectionIndex, ref afterHardBreak);
                        continue;
                    }

                    page.Fragments.Add(new OpaqueFragment(placement.Story, placement.Path, opaque, new RectD(page.Column.Left, page.Y, page.Column.Width, height)));
                    page.Y += height;
                    page.IsEmpty = false;
                    i++;
                    continue;
                }

                var paragraph = (Paragraph)placement.Block;
                ParagraphLayout layout = GetLayout(paragraph, page.Column.Width, _bodyFields, markers.TryGetValue(i, out ListMarker bodyMarker) ? bodyMarker : null);
                ResolvedParagraphProperties pp = layout.Properties;

                if (pp.PageBreakBefore && !page.IsEmpty && placement.NextLine == 0)
                {
                    page = NextColumn(page, ref pageNumber, sectionIndex, ref afterHardBreak, forcePage: true);
                    afterHardBreak = true;
                    continue;
                }

                double spaceBefore = placement.NextLine == 0 ? EffectiveSpaceBefore(section, i, layout, page.IsEmpty, afterHardBreak) : 0;
                double available = page.Column.Bottom - page.Y - spaceBefore;
                int first = placement.NextLine;
                int last = layout.LineCount - 1;

                // How many lines fit (honoring forced page breaks inside the paragraph).
                int fit = CountFittingLines(layout, first, available, out BreakKind? forcedBreak, out int forcedAt);
                bool wholeParagraphFits = first + fit > last && forcedBreak is null;

                int take = fit;
                if (!wholeParagraphFits && forcedBreak is null)
                {
                    take = ApplyKeepRules(layout, first, fit, page.IsEmpty, pp);
                }

                if (forcedBreak is not null)
                {
                    take = Math.Min(take, forcedAt - first + 1);
                }

                // Footnotes referenced on the lines being placed need room at the bottom of the page.
                List<int> newNotes = NotesInLines(paragraph, layout, first, first + take - 1, page);
                while (take > 0 && newNotes.Count > 0 && layout.HeightOfLines(first, first + take - 1) + MeasureNotes(newNotes, page) > available + 0.001)
                {
                    take--;
                    newNotes = NotesInLines(paragraph, layout, first, first + take - 1, page);
                }

                if (take <= 0 && page.IsEmpty && newNotes.Count == 0 && fit > 0)
                {
                    take = 1;
                    newNotes = NotesInLines(paragraph, layout, first, first, page);
                }



                if (take <= 0)
                {
                    if (page.IsEmpty)
                    {
                        take = Math.Max(1, fit); // nothing fits even on an empty page: place at least one line
                    }
                    else
                    {
                        // Move this paragraph (and any keep-with-next chain ending here) to the next page.
                        int rewindTo = RewindKeepWithNextChain(page, body, i);
                        if (rewindTo < i)
                        {
                            i = rewindTo;
                        }

                        page = NextColumn(page, ref pageNumber, sectionIndex, ref afterHardBreak);
                        continue;
                    }
                }

                int lastLine = first + take - 1;
                bool endsParagraph = lastLine == last;
                double spaceAfter = endsParagraph ? EffectiveSpaceAfter(section, i, layout) : 0;
                double top = page.Y;
                var fragment = new ParagraphFragment(placement.Story, placement.Path, layout, first, lastLine, top, page.Column.Left, spaceBefore, spaceAfter);
                page.Fragments.Add(fragment);
                page.Y = fragment.Bounds.Bottom;
                page.IsEmpty = false;
                foreach (int noteId in newNotes)
                {
                    page.AddNote(placement.Path, noteId, MeasureNote(noteId, page));
                }



                // Keep-with-next bookkeeping: remember that this paragraph (entire) sits on this page.
                placement.PlacedWholeOnPage = first == 0 && endsParagraph;
                placement.FragmentsOnPage++;

                if (endsParagraph)
                {
                    if (forcedBreak is not null)
                    {
                        page = NextColumn(page, ref pageNumber, sectionIndex, ref afterHardBreak, forcePage: forcedBreak == BreakKind.Page);
                        afterHardBreak = true;
                        placement.NextLine = 0;
                    }

                    i++;
                }
                else
                {
                    placement.NextLine = lastLine + 1;
                    page = NextColumn(page, ref pageNumber, sectionIndex, ref afterHardBreak, forcePage: forcedBreak == BreakKind.Page);
                    afterHardBreak = forcedBreak is not null;
                }

                afterHardBreak = afterHardBreak && (forcedBreak is not null);
            }

            if (page.Fragments.Count > 0 || _pages.Count == 0)
            {
                FinishPage(page, pageNumber++, sectionIndex);
            }

            return pageNumber;
        }

        /// <summary>Space before is suppressed at the top of a page that results from natural flow.</summary>
        private double EffectiveSpaceBefore(Section section, int blockIndex, ParagraphLayout layout, bool pageIsEmpty, bool afterHardBreak)
        {
            if (pageIsEmpty && !afterHardBreak)
            {
                return 0;
            }

            if (layout.Properties.ContextualSpacing && blockIndex > 0 && blockIndex < section.Body.Count && section.Body[blockIndex - 1] is Paragraph previous
                && string.Equals(previous.StyleId, layout.Paragraph.StyleId, StringComparison.Ordinal))
            {
                return 0;
            }

            return layout.SpaceBefore;
        }

        private double EffectiveSpaceAfter(Section section, int blockIndex, ParagraphLayout layout)
        {
            if (layout.Properties.ContextualSpacing && blockIndex + 1 < section.Body.Count && section.Body[blockIndex + 1] is Paragraph next
                && string.Equals(next.StyleId, layout.Paragraph.StyleId, StringComparison.Ordinal))
            {
                return 0;
            }

            return layout.SpaceAfter;
        }

        private static int CountFittingLines(ParagraphLayout layout, int first, double available, out BreakKind? forcedBreak, out int forcedAt)
        {
            forcedBreak = null;
            forcedAt = -1;
            double used = 0;
            int count = 0;
            for (int i = first; i < layout.LineCount; i++)
            {
                double h = layout.LineHeights[i];
                if (used + h > available + 0.001)
                {
                    break;
                }

                used += h;
                count++;
                BreakKind? forced = layout.Lines[i].ForcedBreakAfter;
                if (forced is BreakKind.Page or BreakKind.Column)
                {
                    forcedBreak = forced;
                    forcedAt = i;
                    break;
                }
            }

            return count;
        }

        /// <summary>Widow/orphan and keep-lines-together: returns how many lines to place on this page (0 = move whole paragraph).</summary>
        private int ApplyKeepRules(ParagraphLayout layout, int first, int fit, bool pageIsEmpty, ResolvedParagraphProperties pp)
        {
            int remainingLines = layout.LineCount - first;
            if (pp.KeepLinesTogether)
            {
                return pageIsEmpty ? fit : 0;
            }

            if (!pp.WidowControl || first > 0)
            {
                // Continuations of an already-split paragraph only need the widow rule.
                if (pp.WidowControl && remainingLines - fit < _options.WidowLines && remainingLines - fit > 0)
                {
                    int reduced = remainingLines - _options.WidowLines;
                    return reduced > 0 ? reduced : (pageIsEmpty ? fit : 0);
                }

                return fit;
            }

            int take = fit;
            if (take < _options.OrphanLines)
            {
                return 0;
            }

            int left = remainingLines - take;
            if (left < _options.WidowLines)
            {
                take = remainingLines - _options.WidowLines;
            }

            if (take < _options.OrphanLines)
            {
                return 0;
            }

            return take;
        }

        /// <summary>
        /// When paragraph <paramref name="index"/> moves to the next page, preceding paragraphs with keep-with-next that
        /// were placed whole on this page follow it. Returns the index to resume from.
        /// </summary>
        private int RewindKeepWithNextChain(PageBuilder page, List<Placement> body, int index)
        {
            int resume = index;
            while (resume > 0)
            {
                Placement previous = body[resume - 1];
                if (previous.Block is not Paragraph prevParagraph || !previous.PlacedWholeOnPage || previous.FragmentsOnPage == 0)
                {
                    break;
                }

                if (!_resolver.ResolveParagraph(prevParagraph).KeepWithNext)
                {
                    break;
                }

                if (page.Fragments.Count == 0 || page.Fragments[^1] is not ParagraphFragment lastFragment || lastFragment.Path != previous.Path)
                {
                    break;
                }

                // Never empty the page entirely: a chain longer than a page just breaks.
                if (page.Fragments.Count == 1)
                {
                    break;
                }

                page.Fragments.RemoveAt(page.Fragments.Count - 1);
                page.RemoveNotesOf(previous.Path);
                page.Y = page.Fragments.Count == 0 ? page.BodyArea.Top : page.Fragments[^1].Bounds.Bottom;
                previous.PlacedWholeOnPage = false;
                previous.FragmentsOnPage = 0;
                previous.NextLine = 0;
                resume--;
            }

            // Everything from `resume` onward restarts on the next page.
            for (int i = resume; i <= index && i < body.Count; i++)
            {
                body[i].FragmentsOnPage = 0;
                body[i].PlacedWholeOnPage = false;
            }

            return resume;
        }

        /// <summary>Moves to the next column of a multi-column page, or (when the columns are used up or a page break demands it) to a new page.</summary>
        private PageBuilder NextColumn(PageBuilder page, ref int pageNumber, int sectionIndex, ref bool afterHardBreak, bool forcePage = false)
        {
            if (!forcePage && page.ColumnIndex < page.ColumnCount - 1)
            {
                page.ColumnIndex++;
                page.Y = page.BodyArea.Top;
                page.IsEmpty = true;
                afterHardBreak = false;
                return page;
            }

            PageBuilder next = Flush(page, pageNumber, sectionIndex, ref afterHardBreak);
            pageNumber++;
            return next;
        }

        private PageBuilder Flush(PageBuilder page, int pageNumber, int sectionIndex, ref bool afterHardBreak)

        {
            FinishPage(page, pageNumber, sectionIndex);
            afterHardBreak = false;
            return NewPage(sectionIndex, pageNumber + 1, isFirstOfSection: false);
        }

        private PageBuilder NewPage(int sectionIndex, int pageNumber, bool isFirstOfSection)
        {
            SectionProperties props = _document.Sections[sectionIndex].Properties;
            var size = new SizeD(props.PageWidth.ToDips(), props.PageHeight.ToDips());
            (StoryLayout? header, StoryLayout? footer, HeaderFooterVariant variant) = LayoutHeaderFooter(sectionIndex, pageNumber, isFirstOfSection, size);

            double bodyTop = props.MarginTop.ToDips();
            if (header is not null)
            {
                bodyTop = Math.Max(bodyTop, header.Bounds.Top + header.Height);
            }

            double bodyBottom = size.Height - props.MarginBottom.ToDips();
            if (footer is not null)
            {
                bodyBottom = Math.Min(bodyBottom, footer.Bounds.Top);
            }

            double left = (props.MarginLeft + props.Gutter).ToDips();
            double width = Math.Max(1, size.Width - left - props.MarginRight.ToDips());
            var bodyArea = RectD.FromEdges(left, bodyTop, left + width, Math.Max(bodyTop + 1, bodyBottom));
            return new PageBuilder(sectionIndex, pageNumber, size, bodyArea, header, footer, isFirstOfSection, variant)
            {
                ColumnCount = Math.Clamp(props.ColumnCount, 1, 12),
                ColumnGap = Math.Max(0, props.ColumnSpacing.ToDips()),
            };
        }

        /// <summary>Ids of notes referenced on lines [first, last] of the paragraph that are not yet on the page, in document order.</summary>
        private List<int> NotesInLines(Paragraph paragraph, ParagraphLayout layout, int first, int last, PageBuilder page)
        {
            var ids = new List<int>();
            if (last < first || _document.Footnotes.IsEmpty)
            {
                return ids;
            }

            int start = layout.Lines[first].Start;
            int end = layout.Lines[last].End;
            foreach (InlineSpan span in paragraph.Spans())
            {
                if (span.Inline is FootnoteReference { IsNoteMark: false, IsEndnote: false } note && span.Start >= start && span.Start < end
                    && _document.Footnotes.ContainsKey(note.NoteId) && !page.HasNote(note.NoteId) && !ids.Contains(note.NoteId))
                {
                    ids.Add(note.NoteId);
                }
            }

            return ids;
        }

        private double MeasureNotes(List<int> ids, PageBuilder page)
        {
            double height = page.Footnotes.Count == 0 && ids.Count > 0 ? FootnoteSeparatorHeight : 0;
            foreach (int id in ids)
            {
                height += MeasureNote(id, page).Height;
            }

            return height;
        }

        private StoryLayout MeasureNote(int id, PageBuilder page)
        {
            if (!_measuredNotes.TryGetValue(id, out StoryLayout? measured))
            {
                measured = LayoutStory(StoryId.Footnote(id), _document.Footnotes[id], new RectD(page.BodyArea.Left, 0, page.BodyArea.Width, 0), _bodyFields);
                _measuredNotes[id] = measured;
            }

            return measured;
        }

        /// <summary>Lays the page's footnotes out at their final place: a short separator rule, then the notes in order.</summary>
        private (ImmutableArray<StoryLayout> Notes, double? SeparatorY) PlaceFootnotes(PageBuilder page)
        {
            if (page.Footnotes.Count == 0)
            {
                return (ImmutableArray<StoryLayout>.Empty, null);
            }

            double top = page.BodyArea.Bottom - page.FootnoteHeight;
            double y = top + FootnoteSeparatorHeight;
            var notes = ImmutableArray.CreateBuilder<StoryLayout>(page.Footnotes.Count);
            foreach (int id in page.Footnotes)
            {
                StoryLayout placed = LayoutStory(StoryId.Footnote(id), _document.Footnotes[id], new RectD(page.BodyArea.Left, y, page.BodyArea.Width, 0), _bodyFields);
                notes.Add(placed);
                y += placed.Height;
            }

            return (notes.ToImmutable(), top + FootnoteSeparatorHeight / 2);
        }

        private void FinishPage(PageBuilder page, int pageNumber, int sectionIndex)
        {
            (ImmutableArray<StoryLayout> notes, double? separator) = PlaceFootnotes(page);
            _pages.Add(new PageLayout(
                _pages.Count,
                sectionIndex,
                pageNumber,
                FormatPageNumber(pageNumber, _document.Sections[sectionIndex].Properties.PageNumberFormat),
                page.Size,
                page.BodyArea,
                page.Fragments.ToImmutableArray(),
                page.Header,
                page.Footer,
                isBlankFiller: false,
                page.Variant,
                notes,
                separator,
                page.EndnoteSeparatorY));
        }


        private void AddBlankPage(int sectionIndex, int pageNumber, bool isFirstOfSection)
        {
            PageBuilder page = NewPage(sectionIndex, pageNumber, isFirstOfSection);
            _pages.Add(new PageLayout(
                _pages.Count,
                sectionIndex,
                pageNumber,
                FormatPageNumber(pageNumber, _document.Sections[sectionIndex].Properties.PageNumberFormat),
                page.Size,
                page.BodyArea,
                ImmutableArray<BlockFragment>.Empty,
                page.Header,
                page.Footer,
                isBlankFiller: true,
                page.Variant));
        }

        private (StoryLayout? Header, StoryLayout? Footer, HeaderFooterVariant Variant) LayoutHeaderFooter(int sectionIndex, int pageNumber, bool isFirstOfSection, SizeD pageSize)
        {
            SectionProperties props = _document.Sections[sectionIndex].Properties;
            HeaderFooterVariant variant = HeaderFooterVariant.Default;
            if (isFirstOfSection && props.TitlePage)
            {
                variant = HeaderFooterVariant.First;
            }
            else if (_document.Settings.EvenAndOddHeaders && pageNumber % 2 == 0)
            {
                variant = HeaderFooterVariant.Even;
            }

            int sectionPages = _sectionPagesHint is not null && sectionIndex < _sectionPagesHint.Count ? _sectionPagesHint[sectionIndex] : 1;
            var fields = new FieldValues(
                FormatPageNumber(pageNumber, props.PageNumberFormat),
                _totalPagesHint.ToString(CultureInfo.InvariantCulture),
                sectionPages.ToString(CultureInfo.InvariantCulture));

            double left = (props.MarginLeft + props.Gutter).ToDips();
            double width = Math.Max(1, pageSize.Width - left - props.MarginRight.ToDips());

            StoryLayout? header = null;
            (StoryId headerStory, ImmutableList<Block>? headerBlocks) = ResolveStory(sectionIndex, isHeader: true, variant);
            if (headerBlocks is not null)
            {
                double top = props.HeaderDistance.ToDips();
                header = LayoutStory(headerStory, headerBlocks, new RectD(left, top, width, pageSize.Height), fields);
            }

            StoryLayout? footer = null;
            (StoryId footerStory, ImmutableList<Block>? footerBlocks) = ResolveStory(sectionIndex, isHeader: false, variant);
            if (footerBlocks is not null)
            {
                // Lay out at y=0 to measure, then shift so the bottom sits at the footer distance.
                StoryLayout measured = LayoutStory(footerStory, footerBlocks, new RectD(left, 0, width, pageSize.Height), fields);
                double bottom = pageSize.Height - props.FooterDistance.ToDips();
                double top = bottom - measured.Height;
                footer = LayoutStory(footerStory, footerBlocks, new RectD(left, top, width, measured.Height), fields);
            }

            return (header, footer, variant);
        }

        /// <summary>Walks back through "linked to previous" sections to find the story to show.</summary>
        private (StoryId Story, ImmutableList<Block>? Blocks) ResolveStory(int sectionIndex, bool isHeader, HeaderFooterVariant variant)
        {
            StoryKind kind = (isHeader, variant) switch
            {
                (true, HeaderFooterVariant.Default) => StoryKind.HeaderDefault,
                (true, HeaderFooterVariant.First) => StoryKind.HeaderFirst,
                (true, HeaderFooterVariant.Even) => StoryKind.HeaderEven,
                (false, HeaderFooterVariant.Default) => StoryKind.FooterDefault,
                (false, HeaderFooterVariant.First) => StoryKind.FooterFirst,
                _ => StoryKind.FooterEven,
            };

            for (int s = sectionIndex; s >= 0; s--)
            {
                var story = new StoryId(s, kind);
                ImmutableList<Block>? blocks = _document.TryGetStory(story);
                if (blocks is not null)
                {
                    return (story, blocks);
                }
            }

            // No fallback to the default variant: like Word, a title page or even page whose own variant is
            // undefined everywhere shows an empty header/footer.
            return (new StoryId(sectionIndex, kind), null);
        }

        private StoryLayout LayoutStory(StoryId story, ImmutableList<Block> blocks, RectD area, FieldValues fields)
        {
            var fragments = ImmutableArray.CreateBuilder<BlockFragment>();
            IReadOnlyDictionary<int, ListMarker> markers = ListNumbering.Compute(blocks, _document.Lists, _resolver);
            double y = area.Top;
            for (int i = 0; i < blocks.Count; i++)
            {
                if (blocks[i] is not Paragraph paragraph)
                {
                    continue;
                }

                ParagraphLayout layout = GetLayout(paragraph, area.Width, fields, markers.TryGetValue(i, out ListMarker storyMarker) ? storyMarker : null);
                if (layout.Input.ContainsFields && layout.Input.Runs.Any(r => r.Kind == RunKind.Field && r.FieldText != null)
                    && paragraph.Inlines.OfType<Field>().Any(f => f.Kind is FieldKind.NumPages or FieldKind.SectionPages))
                {
                    UsedTotalDependentField = true;
                }

                double spaceBefore = i == 0 ? 0 : layout.SpaceBefore;
                var fragment = new ParagraphFragment(story, BlockPath.Of(i), layout, 0, layout.LineCount - 1, y, area.Left, spaceBefore, layout.SpaceAfter);
                fragments.Add(fragment);
                y = fragment.Bounds.Bottom;
            }

            double height = y - area.Top;
            return new StoryLayout(story, new RectD(area.Left, area.Top, area.Width, height), fragments.ToImmutable());
        }

        /// <summary>Header rows at the top of a table (w:tblHeader) that repeat on every page the table continues on; never all of them.</summary>
        private static int LeadingHeaderRows(Table table)
        {
            int count = 0;
            while (count < table.Rows.Length && table.Rows[count].IsHeader)
            {
                count++;
            }

            return count >= table.Rows.Length ? 0 : count;
        }

        /// <summary>Lays out a table at the column width: grid columns scaled to fit, each cell's paragraphs formatted at the cell width.</summary>

        private TableLayout GetTableLayout(Table table, double columnWidth, FieldValues fields)
        {
            double indent = Math.Max(0, table.Indent.ToDips());
            double available = Math.Max(10, columnWidth - indent);
            int columnCount = Math.Max(1, table.ColumnCount);
            var widths = new double[columnCount];
            for (int c = 0; c < columnCount; c++)
            {
                widths[c] = c < table.ColumnWidths.Length ? Math.Max(0, table.ColumnWidths[c].ToDips()) : 0;
            }

            double scale = 1;
            double total = widths.Sum();
            if (total <= 0.5)
            {
                Array.Fill(widths, available / columnCount);
            }
            else if (total > available)
            {
                scale = available / total;
                for (int c = 0; c < columnCount; c++)
                {
                    widths[c] *= scale;
                }
            }

            var rows = ImmutableArray.CreateBuilder<TableRowLayout>(table.Rows.Length);
            foreach (TableRow row in table.Rows)
            {
                var cells = ImmutableArray.CreateBuilder<TableCellLayout>(row.Cells.Length);
                int column = 0;
                double rowHeight = 0;
                foreach (TableCell cell in row.Cells)
                {
                    if (column >= columnCount)
                    {
                        break;
                    }

                    int span = Math.Clamp(cell.GridSpan, 1, columnCount - column);
                    double left = widths.Take(column).Sum();
                    double width = widths.Skip(column).Take(span).Sum();
                    double contentWidth = Math.Max(1, width - 2 * TableLayout.CellPadding);
                    var blocks = ImmutableArray.CreateBuilder<CellBlockLayout>(cell.Blocks.Count);
                    double y = 0;
                    if (cell.VerticalMerge != VerticalMerge.Continue)
                    {
                        IReadOnlyDictionary<int, ListMarker> markers = ListNumbering.Compute(cell.Blocks, _document.Lists, _resolver);
                        for (int b = 0; b < cell.Blocks.Count; b++)
                        {
                            switch (cell.Blocks[b])
                            {
                                case Paragraph paragraph:
                                {
                                    ParagraphLayout layout = GetLayout(paragraph, contentWidth, fields, markers.TryGetValue(b, out ListMarker marker) ? marker : null);
                                    double height = layout.SpaceBefore + layout.ContentHeight + layout.SpaceAfter;
                                    blocks.Add(new CellBlockLayout(layout, null, y, height));
                                    y += height;
                                    break;
                                }

                                case Table nested:
                                {
                                    TableLayout layout = GetTableLayout(nested, contentWidth, fields);
                                    blocks.Add(new CellBlockLayout(null, layout, y, layout.Height));
                                    y += layout.Height;
                                    break;
                                }
                            }
                        }
                    }

                    cells.Add(new TableCellLayout(cell, left, width, blocks.ToImmutable(), y));
                    rowHeight = Math.Max(rowHeight, y);
                    column += span;
                }

                if (row.Height is { } minimum)
                {
                    rowHeight = row.ExactHeight ? minimum.ToDips() : Math.Max(rowHeight, minimum.ToDips());
                }

                rows.Add(new TableRowLayout(row, Math.Max(4, rowHeight), cells.ToImmutable()));
            }

            return new TableLayout(table, indent, ImmutableArray.Create(widths), rows.ToImmutable(), scale);
        }

        private ParagraphLayout GetLayout(Paragraph paragraph, double width, FieldValues fields, ListMarker? marker)

        {
            string signature = (paragraph.Inlines.Any(i => i is Field or FootnoteReference) ? fields.Signature : string.Empty)
                + (paragraph.Inlines.OfType<FootnoteReference>().Any() ? "|fn:" + string.Join(",", paragraph.Inlines.OfType<FootnoteReference>().Select(n => fields.NoteNumber(n))) : string.Empty)
                + (marker is { } m ? "|" + m.Text : string.Empty);
            return _cache.GetOrAdd(paragraph, width, signature, () =>
            {
                ParagraphLayoutInput input = ParagraphLayoutInput.Create(paragraph, _resolver, fields, width, _document.Settings.DefaultTabStop, _options.PixelsPerDip, images: _document.Images);
                IFormattedLine? markerLine = null;
                double markerX = 0;
                if (marker is { Text.Length: > 0 } listMarker)
                {
                    (markerLine, markerX, double textStart) = FormatMarker(input, listMarker);
                    input = input with { FirstLineStart = textStart, Marker = listMarker };
                }

                IReadOnlyList<IFormattedLine> lines = _formatter.FormatParagraph(input);
                return new ParagraphLayout(input, lines, signature, markerLine, markerX);
            });
        }

        /// <summary>
        /// Formats a list marker with the paragraph mark's font. The marker sits at the number position (left indent
        /// minus hanging); the text starts at the left indent, or further right when the marker does not fit.
        /// </summary>
        private (IFormattedLine Line, double X, double TextStart) FormatMarker(ParagraphLayoutInput input, ListMarker marker)
        {
            ResolvedRunProperties run = input.MarkProperties with { FontFamily = marker.Font ?? input.MarkProperties.FontFamily };
            var paragraph = new Paragraph([new Quill.Core.Model.Run(marker.Text)]);
            ResolvedParagraphProperties props = input.Properties with
            {
                Alignment = Alignment.Left,
                LeftIndent = Twips.Zero,
                RightIndent = Twips.Zero,
                FirstLineIndent = Twips.Zero,
                Tabs = TabStops.Empty,
                List = null,
            };
            var markerInput = new ParagraphLayoutInput(
                paragraph,
                marker.Text,
                [new RunSpan(0, marker.Text.Length, run, RunKind.Text)],
                run,
                props,
                Math.Max(1000, input.ColumnWidth * 4),
                input.DefaultTabStop,
                input.FlowDirection,
                input.PixelsPerDip);
            IReadOnlyList<IFormattedLine> lines = _formatter.FormatParagraph(markerInput);
            IFormattedLine line = lines[0];
            for (int i = 1; i < lines.Count; i++)
            {
                lines[i].Dispose();
            }

            double anchor = input.LeftIndent + input.FirstLineIndent;
            double x = marker.Alignment switch
            {
                Alignment.Right => anchor - line.Width,
                Alignment.Center => anchor - line.Width / 2,
                _ => anchor,
            };
            const double minimumGap = 6;
            return (line, x, Math.Max(input.LeftIndent, x + line.Width + minimumGap));
        }

        private static string FormatPageNumber(int number, PageNumberFormat format) => NumberText.Format(number, format);

        private sealed class Placement(Block block, BlockPath path, StoryId story)
        {
            public Block Block { get; } = block;

            public BlockPath Path { get; } = path;

            public StoryId Story { get; } = story;

            /// <summary>True for the first endnote placement: a separator rule goes before it.</summary>
            public bool StartsNotes { get; set; }

            /// <summary>Next line of the paragraph to place (continuation after a page break).</summary>
            public int NextLine { get; set; }

            public bool PlacedWholeOnPage { get; set; }

            public int FragmentsOnPage { get; set; }
        }

        private sealed class PageBuilder(int sectionIndex, int pageNumber, SizeD size, RectD bodyArea, StoryLayout? header, StoryLayout? footer, bool isFirstOfSection, HeaderFooterVariant variant)
        {
            public HeaderFooterVariant Variant { get; } = variant;

            public int SectionIndex { get; } = sectionIndex;

            public int PageNumber { get; } = pageNumber;

            public SizeD Size { get; } = size;

            public RectD BodyArea { get; } = bodyArea;

            public StoryLayout? Header { get; } = header;

            public StoryLayout? Footer { get; } = footer;

            public bool IsFirstOfSection { get; } = isFirstOfSection;

            public List<BlockFragment> Fragments { get; } = [];

            public double Y { get; set; } = bodyArea.Top;

            public bool IsEmpty { get; set; } = true;

            public int ColumnCount { get; init; } = 1;

            public double ColumnGap { get; init; }

            public int ColumnIndex { get; set; }

            /// <summary>y of the rule above the endnotes when they start on this page.</summary>
            public double? EndnoteSeparatorY { get; set; }

            /// <summary>Ids of the footnotes placed on this page, in order; their measured height shortens every column.</summary>
            public List<int> Footnotes { get; } = [];

            private readonly List<(BlockPath Path, int NoteId, double Height)> _notePlacements = [];

            public double FootnoteHeight { get; private set; }

            public bool HasNote(int noteId) => Footnotes.Contains(noteId);

            public void AddNote(BlockPath path, int noteId, StoryLayout measured)
            {
                if (HasNote(noteId))
                {
                    return;
                }

                Footnotes.Add(noteId);
                _notePlacements.Add((path, noteId, measured.Height));
                RecomputeFootnoteHeight();
            }

            public void RemoveNotesOf(BlockPath path)
            {
                if (_notePlacements.RemoveAll(p => p.Path == path) > 0)
                {
                    Footnotes.Clear();
                    Footnotes.AddRange(_notePlacements.Select(p => p.NoteId));
                    RecomputeFootnoteHeight();
                }
            }

            private void RecomputeFootnoteHeight() =>
                FootnoteHeight = _notePlacements.Count == 0 ? 0 : 12 + _notePlacements.Sum(p => p.Height);

            /// <summary>The column being filled; the whole body when the section has a single column. Footnotes shorten it.</summary>
            public RectD Column
            {
                get
                {
                    double bottom = Math.Max(BodyArea.Top + 1, BodyArea.Bottom - FootnoteHeight);
                    if (ColumnCount <= 1)
                    {
                        return RectD.FromEdges(BodyArea.Left, BodyArea.Top, BodyArea.Right, bottom);
                    }

                    double width = Math.Max(1, (BodyArea.Width - ColumnGap * (ColumnCount - 1)) / ColumnCount);
                    double left = BodyArea.Left + ColumnIndex * (width + ColumnGap);
                    return RectD.FromEdges(left, BodyArea.Top, left + width, bottom);
                }
            }


        }
    }
}
