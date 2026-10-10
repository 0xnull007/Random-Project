using System.Collections.Immutable;
using System.Text;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Automation.Text;
using Quill.Core.Editing;
using Quill.Core.Model;
using Quill.Core.Styles;
using Quill.Core.Text;

namespace Quill.App.Views;

/// <summary>Exposes the document to screen readers and automation as a Document control with the Text pattern.</summary>
public sealed class DocumentViewAutomationPeer : FrameworkElementAutomationPeer, ITextProvider
{
    public DocumentViewAutomationPeer(DocumentView owner)
        : base(owner)
    {
    }

    internal DocumentView View => (DocumentView)Owner;

    internal IRawElementProviderSimple Provider() => ProviderFromPeer(this);

    internal Document? Document => View.Session?.Document;

    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Document;

    protected override string GetClassNameCore() => "DocumentView";

    protected override string GetNameCore()
    {
        string name = base.GetNameCore();
        return string.IsNullOrEmpty(name) ? "Document" : name;
    }

    protected override bool IsContentElementCore() => true;

    protected override bool IsControlElementCore() => true;

    protected override bool IsKeyboardFocusableCore() => true;

    public override object? GetPattern(PatternInterface patternInterface) =>
        patternInterface == PatternInterface.Text ? this : base.GetPattern(patternInterface);

    internal TextPosition DocumentStart()
    {
        Document document = Document ?? Core.Model.Document.CreateNew();
        return TextNavigation.StoryStart(document, StoryId.Body(0));
    }

    internal TextPosition DocumentEnd()
    {
        Document document = Document ?? Core.Model.Document.CreateNew();
        return TextNavigation.StoryEnd(document, StoryId.Body(document.Sections.Count - 1));
    }

    // ---------------------------------------------------------------- ITextProvider

    public ITextRangeProvider DocumentRange => new TextRangeProvider(this, DocumentStart(), DocumentEnd());

    public SupportedTextSelection SupportedTextSelection => SupportedTextSelection.Single;

    public ITextRangeProvider[] GetSelection()
    {
        if (View.Session is not { } session)
        {
            return [];
        }

        Selection selection = session.Selection;
        return [new TextRangeProvider(this, selection.Start, selection.End)];
    }

    public ITextRangeProvider[] GetVisibleRanges() =>
        View.VisibleBounds() is { } bounds ? [new TextRangeProvider(this, bounds.Start, bounds.End)] : [];

    public ITextRangeProvider? RangeFromChild(IRawElementProviderSimple childElement) => null;

    public ITextRangeProvider? RangeFromPoint(Point screenLocation)
    {
        Point view = View.PointFromScreen(screenLocation);
        return View.PositionAt(view) is { } position ? new TextRangeProvider(this, position, position) : null;
    }
}

/// <summary>A span of the document between two positions (possibly in different sections' bodies).</summary>
internal sealed class TextRangeProvider : ITextRangeProvider
{
    private readonly DocumentViewAutomationPeer _peer;

    public TextRangeProvider(DocumentViewAutomationPeer peer, TextPosition start, TextPosition end)
    {
        _peer = peer;
        Start = start;
        End = end;
        Normalize();
    }

    public TextPosition Start { get; private set; }

    public TextPosition End { get; private set; }

    private Document Document => _peer.Document ?? Core.Model.Document.CreateNew();

    private DocumentView View => _peer.View;

    // ---------------------------------------------------------------- ordering

    internal static int Compare(TextPosition a, TextPosition b)
    {
        if (a.Story == b.Story)
        {
            return a.CompareTo(b);
        }

        int bySection = a.Story.SectionIndex.CompareTo(b.Story.SectionIndex);
        return bySection != 0 ? bySection : string.CompareOrdinal(a.Story.ToString(), b.Story.ToString());
    }

    private void Normalize()
    {
        if (Compare(Start, End) > 0)
        {
            (Start, End) = (End, Start);
        }
    }

    private TextPosition Clamp(TextPosition position)
    {
        Document document = Document;
        if (document.IsValid(position))
        {
            return position;
        }

        return position.Story.SectionIndex < document.Sections.Count ? TextNavigation.StoryEnd(document, StoryId.Body(Math.Max(0, position.Story.SectionIndex))) : _peer.DocumentEnd();
    }

    private TextPosition? NextStoryStart(TextPosition position)
    {
        Document document = Document;
        int next = position.Story.SectionIndex + 1;
        return next < document.Sections.Count ? TextNavigation.StoryStart(document, StoryId.Body(next)) : null;
    }

    private TextPosition? PreviousStoryEnd(TextPosition position)
    {
        Document document = Document;
        int previous = position.Story.SectionIndex - 1;
        return previous >= 0 ? TextNavigation.StoryEnd(document, StoryId.Body(previous)) : null;
    }

    private TextPosition? StepForward(TextPosition position, TextUnit unit)
    {
        Document document = Document;
        switch (unit)
        {
            case TextUnit.Character:
                return TextNavigation.NextCharacter(document, position) ?? NextStoryStart(position);
            case TextUnit.Format:
            case TextUnit.Word:
                return TextNavigation.NextWord(document, position) ?? NextStoryStart(position);
            case TextUnit.Line:
                if (View.LineBoundsOf(position) is { } line && Compare(line.End, position) > 0)
                {
                    return line.End;
                }

                return TextNavigation.StartOfNextParagraph(document, position) ?? NextStoryStart(position);
            case TextUnit.Paragraph:
                return TextNavigation.StartOfNextParagraph(document, position) ?? NextStoryStart(position);
            case TextUnit.Page:
                if (View.PageBoundsOf(position) is { } page && Compare(page.End, position) > 0)
                {
                    return page.End;
                }

                return TextNavigation.NextCharacter(document, position) ?? NextStoryStart(position);
            default:
                TextPosition end = _peer.DocumentEnd();
                return Compare(end, position) > 0 ? end : null;
        }
    }

    private TextPosition? StepBackward(TextPosition position, TextUnit unit)
    {
        Document document = Document;
        switch (unit)
        {
            case TextUnit.Character:
                return TextNavigation.PreviousCharacter(document, position) ?? PreviousStoryEnd(position);
            case TextUnit.Format:
            case TextUnit.Word:
                return TextNavigation.PreviousWord(document, position) ?? PreviousStoryEnd(position);
            case TextUnit.Line:
                if (View.LineBoundsOf(position) is { } line && Compare(line.Start, position) < 0)
                {
                    return line.Start;
                }

                return TextNavigation.EndOfPreviousParagraph(document, position) ?? PreviousStoryEnd(position);
            case TextUnit.Paragraph:
                if (position.Offset > 0)
                {
                    return TextNavigation.ParagraphStart(position);
                }

                return TextNavigation.EndOfPreviousParagraph(document, position) is { } previous ? TextNavigation.ParagraphStart(previous) : PreviousStoryEnd(position);
            case TextUnit.Page:
                if (View.PageBoundsOf(position) is { } page && Compare(page.Start, position) < 0)
                {
                    return page.Start;
                }

                return TextNavigation.PreviousCharacter(document, position) ?? PreviousStoryEnd(position);
            default:
                TextPosition start = _peer.DocumentStart();
                return Compare(start, position) < 0 ? start : null;
        }
    }

    private int Step(ref TextPosition position, TextUnit unit, int count)
    {
        int moved = 0;
        while (count > 0)
        {
            if (StepForward(position, unit) is not { } next)
            {
                break;
            }

            position = next;
            moved++;
            count--;
        }

        while (count < 0)
        {
            if (StepBackward(position, unit) is not { } previous)
            {
                break;
            }

            position = previous;
            moved--;
            count++;
        }

        return moved;
    }

    private (TextPosition Start, TextPosition End) EnclosingUnit(TextPosition position, TextUnit unit)
    {
        Document document = Document;
        switch (unit)
        {
            case TextUnit.Character:
                return (position, TextNavigation.NextCharacter(document, position) ?? position);
            case TextUnit.Format:
            case TextUnit.Word:
            {
                TextRange word = TextNavigation.WordAt(document, position);
                return (word.Start, word.End);
            }

            case TextUnit.Line:
                if (View.LineBoundsOf(position) is { } line)
                {
                    return line;
                }

                goto case TextUnit.Paragraph;
            case TextUnit.Paragraph:
            {
                TextRange paragraph = TextNavigation.ParagraphAt(document, position);
                return (paragraph.Start, paragraph.End);
            }

            case TextUnit.Page:
                return View.PageBoundsOf(position) ?? (position, position);
            default:
                return (_peer.DocumentStart(), _peer.DocumentEnd());
        }
    }

    // ---------------------------------------------------------------- ITextRangeProvider

    public ITextRangeProvider Clone() => new TextRangeProvider(_peer, Start, End);

    public bool Compare(ITextRangeProvider range) => range is TextRangeProvider other && other.Start == Start && other.End == End;

    public int CompareEndpoints(TextPatternRangeEndpoint endpoint, ITextRangeProvider targetRange, TextPatternRangeEndpoint targetEndpoint)
    {
        if (targetRange is not TextRangeProvider other)
        {
            throw new ArgumentException("Range from another provider.", nameof(targetRange));
        }

        TextPosition mine = endpoint == TextPatternRangeEndpoint.Start ? Start : End;
        TextPosition theirs = targetEndpoint == TextPatternRangeEndpoint.Start ? other.Start : other.End;
        return Math.Sign(Compare(mine, theirs));
    }

    public void ExpandToEnclosingUnit(TextUnit unit)
    {
        (TextPosition start, TextPosition end) = EnclosingUnit(Clamp(Start), unit);
        Start = start;
        End = end;
        Normalize();
    }

    public ITextRangeProvider? FindAttribute(int attribute, object value, bool backward) => null;

    public ITextRangeProvider? FindText(string text, bool backward, bool ignoreCase)
    {
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        StringComparison comparison = ignoreCase ? StringComparison.CurrentCultureIgnoreCase : StringComparison.Ordinal;
        Document document = Document;
        TextRangeProvider? found = null;
        for (int s = Start.Story.SectionIndex; s <= End.Story.SectionIndex && s < document.Sections.Count; s++)
        {
            StoryId story = StoryId.Body(s);
            ImmutableList<Block> blocks = document.Sections[s].Body;
            for (int b = 0; b < blocks.Count; b++)
            {
                if (blocks[b] is not Paragraph paragraph)
                {
                    continue;
                }

                var blockStart = new TextPosition(story, BlockPath.Of(b), 0);
                var blockEnd = new TextPosition(story, BlockPath.Of(b), paragraph.Length);
                if (Compare(blockEnd, Start) < 0 || Compare(blockStart, End) > 0)
                {
                    continue;
                }

                int from = Compare(blockStart, Start) < 0 ? Start.Offset : 0;
                int to = Compare(blockEnd, End) > 0 ? End.Offset : paragraph.Length;
                string haystack = paragraph.FlatText;
                int index = backward ? haystack.LastIndexOf(text, Math.Min(to, haystack.Length), comparison) : haystack.IndexOf(text, from, comparison);
                if (index >= from && index + text.Length <= to)
                {
                    found = new TextRangeProvider(_peer, blockStart.WithOffset(index), blockStart.WithOffset(index + text.Length));
                    if (!backward)
                    {
                        return found;
                    }
                }
            }
        }

        return found;
    }

    public object? GetAttributeValue(int attribute)
    {
        if (View.Session is not { } session || Document.TryGetParagraph(Start) is not { } paragraph)
        {
            return AutomationElementIdentifiers.NotSupported;
        }

        List<ResolvedRunProperties> runs = ResolvedRuns(session, paragraph);
        if (runs.Count == 0)
        {
            runs.Add(session.Resolver.ResolveParagraphMark(paragraph));
        }

        ResolvedParagraphProperties pp = session.Resolver.ResolveParagraph(paragraph);
        if (attribute == TextPatternIdentifiers.FontNameAttribute.Id)
        {
            return Uniform(runs, r => r.FontFamily);
        }

        if (attribute == TextPatternIdentifiers.FontSizeAttribute.Id)
        {
            return Uniform(runs, r => r.FontSize.ToPoints());
        }

        if (attribute == TextPatternIdentifiers.FontWeightAttribute.Id)
        {
            return Uniform(runs, r => r.Bold ? 700 : 400);
        }

        if (attribute == TextPatternIdentifiers.IsItalicAttribute.Id)
        {
            return Uniform(runs, r => r.Italic);
        }

        if (attribute == TextPatternIdentifiers.UnderlineStyleAttribute.Id)
        {
            return Uniform(runs, r => r.Underline == UnderlineStyle.None ? TextDecorationLineStyle.None : r.Underline == UnderlineStyle.Double ? TextDecorationLineStyle.Double : TextDecorationLineStyle.Single);
        }

        if (attribute == TextPatternIdentifiers.StrikethroughStyleAttribute.Id)
        {
            return Uniform(runs, r => r.Strikethrough || r.DoubleStrikethrough ? TextDecorationLineStyle.Single : TextDecorationLineStyle.None);
        }

        if (attribute == TextPatternIdentifiers.ForegroundColorAttribute.Id)
        {
            return Uniform(runs, r => r.Color.IsAuto ? 0 : r.Color.R | (r.Color.G << 8) | (r.Color.B << 16));
        }

        if (attribute == TextPatternIdentifiers.IsSuperscriptAttribute.Id)
        {
            return Uniform(runs, r => r.VerticalAlignment == VerticalTextAlignment.Superscript);
        }

        if (attribute == TextPatternIdentifiers.IsSubscriptAttribute.Id)
        {
            return Uniform(runs, r => r.VerticalAlignment == VerticalTextAlignment.Subscript);
        }

        if (attribute == TextPatternIdentifiers.IsHiddenAttribute.Id)
        {
            return Uniform(runs, r => r.Hidden);
        }

        if (attribute == TextPatternIdentifiers.IsReadOnlyAttribute.Id)
        {
            return View.IsReadOnly;
        }

        if (attribute == TextPatternIdentifiers.HorizontalTextAlignmentAttribute.Id)
        {
            return pp.Alignment switch
            {
                Alignment.Center => HorizontalTextAlignment.Centered,
                Alignment.Right => HorizontalTextAlignment.Right,
                Alignment.Justify => HorizontalTextAlignment.Justified,
                _ => HorizontalTextAlignment.Left,
            };
        }

        if (attribute == TextPatternIdentifiers.IndentationLeadingAttribute.Id)
        {
            return pp.LeftIndent.ToPoints();
        }

        if (attribute == TextPatternIdentifiers.IndentationTrailingAttribute.Id)
        {
            return pp.RightIndent.ToPoints();
        }

        if (attribute == TextPatternIdentifiers.IndentationFirstLineAttribute.Id)
        {
            return pp.FirstLineIndent.ToPoints();
        }

        if (attribute == TextPatternIdentifiers.BulletStyleAttribute.Id)
        {
            return pp.IsListItem ? BulletStyle.FilledRoundBullet : BulletStyle.None;
        }

        return AutomationElementIdentifiers.NotSupported;
    }

    private static object Uniform<T>(List<ResolvedRunProperties> runs, Func<ResolvedRunProperties, T> select)
        where T : notnull
    {
        T first = select(runs[0]);
        for (int i = 1; i < runs.Count; i++)
        {
            if (!EqualityComparer<T>.Default.Equals(select(runs[i]), first))
            {
                return TextPatternIdentifiers.MixedAttributeValue;
            }
        }

        return first;
    }

    private List<ResolvedRunProperties> ResolvedRuns(EditingSession session, Paragraph startParagraph)
    {
        var runs = new List<ResolvedRunProperties>();
        if (Start.Story != End.Story)
        {
            if (startParagraph.TryGetInlineAt(Math.Min(Start.Offset, Math.Max(0, startParagraph.Length - 1)), out InlineSpan single))
            {
                runs.Add(session.Resolver.ResolveRun(startParagraph, single.Inline));
            }

            return runs;
        }

        TextRange range = Start == End ? new TextRange(Start, Start) : new TextRange(Start, End);
        if (range.IsEmpty)
        {
            runs.Add(session.Resolver.ResolveRun(startParagraph.StyleId, startParagraph.GetTypingFormatAt(Start.Offset).StyleId, startParagraph.GetTypingFormatAt(Start.Offset).Properties));
            return runs;
        }

        foreach (ParagraphSpan span in TextRanges.Paragraphs(Document, range))
        {
            foreach (InlineSpan inline in span.Paragraph.Spans())
            {
                if (inline.End > span.Start && inline.Start < span.End)
                {
                    runs.Add(session.Resolver.ResolveRun(span.Paragraph, inline.Inline));
                }
            }
        }

        return runs;
    }

    public double[] GetBoundingRectangles()
    {
        var result = new List<double>();
        Document document = Document;
        for (int s = Start.Story.SectionIndex; s <= End.Story.SectionIndex && s < document.Sections.Count; s++)
        {
            StoryId story = StoryId.Body(s);
            TextPosition from = s == Start.Story.SectionIndex && Start.Story == story ? Start : TextNavigation.StoryStart(document, story);
            TextPosition to = s == End.Story.SectionIndex && End.Story == story ? End : TextNavigation.StoryEnd(document, story);
            foreach (Rect rect in View.ViewRectsFor(from, to))
            {
                Point topLeft = View.PointToScreen(rect.TopLeft);
                Point bottomRight = View.PointToScreen(rect.BottomRight);
                result.Add(topLeft.X);
                result.Add(topLeft.Y);
                result.Add(bottomRight.X - topLeft.X);
                result.Add(bottomRight.Y - topLeft.Y);
            }
        }

        return result.ToArray();
    }

    public IRawElementProviderSimple[] GetChildren() => [];

    public IRawElementProviderSimple GetEnclosingElement() => _peer.Provider();

    public string GetText(int maxLength)
    {
        var text = new StringBuilder();
        Document document = Document;
        for (int s = Start.Story.SectionIndex; s <= End.Story.SectionIndex && s < document.Sections.Count; s++)
        {
            StoryId story = StoryId.Body(s);
            ImmutableList<Block> blocks = document.Sections[s].Body;
            bool firstSection = s == Start.Story.SectionIndex;
            bool lastSection = s == End.Story.SectionIndex;
            int firstBlock = firstSection && Start.Story == story ? Math.Min(Start.Block.TopIndex, blocks.Count - 1) : 0;
            int lastBlock = lastSection && End.Story == story ? Math.Min(End.Block.TopIndex, blocks.Count - 1) : blocks.Count - 1;
            for (int b = firstBlock; b <= lastBlock && b >= 0; b++)
            {
                if (blocks[b] is not Paragraph paragraph)
                {
                    continue;
                }

                int from = b == firstBlock && firstSection && Start.Story == story ? Math.Min(Start.Offset, paragraph.Length) : 0;
                int to = b == lastBlock && lastSection && End.Story == story ? Math.Min(End.Offset, paragraph.Length) : paragraph.Length;
                if (to > from)
                {
                    text.Append(paragraph.FlatText, from, to - from);
                }

                bool last = b == lastBlock && lastSection;
                if (!last)
                {
                    text.Append('\n');
                }

                if (maxLength >= 0 && text.Length >= maxLength)
                {
                    return text.ToString(0, maxLength);
                }
            }
        }

        return text.ToString();
    }

    public int Move(TextUnit unit, int count)
    {
        if (count == 0)
        {
            return 0;
        }

        ExpandToEnclosingUnit(unit);
        TextPosition position = Start;
        int moved = Step(ref position, unit, count);
        Start = position;
        End = position;
        ExpandToEnclosingUnit(unit);
        return moved;
    }

    public int MoveEndpointByUnit(TextPatternRangeEndpoint endpoint, TextUnit unit, int count)
    {
        TextPosition position = endpoint == TextPatternRangeEndpoint.Start ? Start : End;
        int moved = Step(ref position, unit, count);
        if (endpoint == TextPatternRangeEndpoint.Start)
        {
            Start = position;
            if (Compare(Start, End) > 0)
            {
                End = Start;
            }
        }
        else
        {
            End = position;
            if (Compare(End, Start) < 0)
            {
                Start = End;
            }
        }

        return moved;
    }

    public void MoveEndpointByRange(TextPatternRangeEndpoint endpoint, ITextRangeProvider targetRange, TextPatternRangeEndpoint targetEndpoint)
    {
        if (targetRange is not TextRangeProvider other)
        {
            throw new ArgumentException("Range from another provider.", nameof(targetRange));
        }

        TextPosition target = targetEndpoint == TextPatternRangeEndpoint.Start ? other.Start : other.End;
        if (endpoint == TextPatternRangeEndpoint.Start)
        {
            Start = target;
            if (Compare(Start, End) > 0)
            {
                End = Start;
            }
        }
        else
        {
            End = target;
            if (Compare(End, Start) < 0)
            {
                Start = End;
            }
        }
    }

    public void Select()
    {
        if (View.Session is not { } session)
        {
            return;
        }

        if (Start.Story == End.Story && session.Document.IsValid(Start) && session.Document.IsValid(End))
        {
            session.SetSelection(new Selection(Start, End));
        }
        else if (session.Document.IsValid(Start))
        {
            session.SetSelection(Selection.Caret(Start));
        }
    }

    public void AddToSelection() => throw new InvalidOperationException("The document supports a single selection.");

    public void RemoveFromSelection() => throw new InvalidOperationException("The document supports a single selection.");

    public void ScrollIntoView(bool alignToTop) => View.BringIntoView(alignToTop ? Start : End);
}
