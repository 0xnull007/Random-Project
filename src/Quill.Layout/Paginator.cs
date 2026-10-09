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

        public Run(Document document, StyleResolver resolver, ILineFormatter formatter, LayoutCache cache, LayoutOptions options, int totalPagesHint, IReadOnlyList<int>? sectionPagesHint)
        {
            _document = document;
            _resolver = resolver;
            _formatter = formatter;
            _cache = cache;
            _options = options;
            _totalPagesHint = totalPagesHint;
            _sectionPagesHint = sectionPagesHint;
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
                body.Add(new Placement(block, BlockPath.Of(index)));
            }

            IReadOnlyDictionary<int, ListMarker> markers = ListNumbering.Compute(section.Body, _document.Lists, _resolver);
            var page = NewPage(sectionIndex, pageNumber, isFirstOfSection: true);
            bool afterHardBreak = true; // document/section start keeps space-before
            int i = 0;
            while (i < body.Count)
            {
                Placement placement = body[i];
                if (placement.Block is OpaqueBlock opaque)
                {
                    double height = OpaqueFragment.PlaceholderHeight;
                    if (!page.IsEmpty && page.Y + height > page.BodyArea.Bottom)
                    {
                        page = Flush(page, pageNumber++, sectionIndex, ref afterHardBreak);
                        continue;
                    }

                    page.Fragments.Add(new OpaqueFragment(StoryId.Body(sectionIndex), placement.Path, opaque, new RectD(page.BodyArea.Left, page.Y, page.BodyArea.Width, height)));
                    page.Y += height;
                    page.IsEmpty = false;
                    i++;
                    continue;
                }

                var paragraph = (Paragraph)placement.Block;
                ParagraphLayout layout = GetLayout(paragraph, page.BodyArea.Width, FieldValues.Placeholder, markers.TryGetValue(i, out ListMarker bodyMarker) ? bodyMarker : null);
                ResolvedParagraphProperties pp = layout.Properties;

                if (pp.PageBreakBefore && !page.IsEmpty && placement.NextLine == 0)
                {
                    page = Flush(page, pageNumber++, sectionIndex, ref afterHardBreak);
                    afterHardBreak = true;
                    continue;
                }

                double spaceBefore = placement.NextLine == 0 ? EffectiveSpaceBefore(section, i, layout, page.IsEmpty, afterHardBreak) : 0;
                double available = page.BodyArea.Bottom - page.Y - spaceBefore;
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

                        page = Flush(page, pageNumber++, sectionIndex, ref afterHardBreak);
                        continue;
                    }
                }

                int lastLine = first + take - 1;
                bool endsParagraph = lastLine == last;
                double spaceAfter = endsParagraph ? EffectiveSpaceAfter(section, i, layout) : 0;
                double top = page.Y;
                var fragment = new ParagraphFragment(StoryId.Body(sectionIndex), placement.Path, layout, first, lastLine, top, page.BodyArea.Left, spaceBefore, spaceAfter);
                page.Fragments.Add(fragment);
                page.Y = fragment.Bounds.Bottom;
                page.IsEmpty = false;

                // Keep-with-next bookkeeping: remember that this paragraph (entire) sits on this page.
                placement.PlacedWholeOnPage = first == 0 && endsParagraph;
                placement.FragmentsOnPage++;

                if (endsParagraph)
                {
                    if (forcedBreak is not null)
                    {
                        page = Flush(page, pageNumber++, sectionIndex, ref afterHardBreak);
                        afterHardBreak = true;
                        placement.NextLine = 0;
                    }

                    i++;
                }
                else
                {
                    placement.NextLine = lastLine + 1;
                    page = Flush(page, pageNumber++, sectionIndex, ref afterHardBreak);
                    afterHardBreak = forcedBreak is not null;
                }

                afterHardBreak = afterHardBreak && (forcedBreak is not null);
            }

            if (!page.IsEmpty || _pages.Count == 0)
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

            if (layout.Properties.ContextualSpacing && blockIndex > 0 && section.Body[blockIndex - 1] is Paragraph previous
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
            return new PageBuilder(sectionIndex, pageNumber, size, bodyArea, header, footer, isFirstOfSection, variant);
        }

        private void FinishPage(PageBuilder page, int pageNumber, int sectionIndex)
        {
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
                page.Variant));
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

        private ParagraphLayout GetLayout(Paragraph paragraph, double width, FieldValues fields, ListMarker? marker)
        {
            string signature = (paragraph.Inlines.Any(i => i is Field) ? fields.Signature : string.Empty)
                + (marker is { } m ? "|" + m.Text : string.Empty);
            return _cache.GetOrAdd(paragraph, width, signature, () =>
            {
                ParagraphLayoutInput input = ParagraphLayoutInput.Create(paragraph, _resolver, fields, width, _document.Settings.DefaultTabStop, _options.PixelsPerDip);
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

        private sealed class Placement(Block block, BlockPath path)
        {
            public Block Block { get; } = block;

            public BlockPath Path { get; } = path;

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
        }
    }
}
