using System.Windows;
using System.Windows.Media.TextFormatting;
using Quill.Core.Model;
using Quill.Core.Styles;
using Quill.Core.Units;

namespace Quill.Layout.Wpf;

/// <summary>
/// Adapts resolved paragraph formatting to WPF's TextFormatter. Indents are handled by the caller (line width
/// and origin), so tab positions are re-based onto the line's own origin here.
/// </summary>
internal sealed class QuillTextParagraphProperties : TextParagraphProperties
{
    private readonly TextAlignment _alignment;
    private readonly System.Windows.FlowDirection _flowDirection;
    private readonly double _lineHeight;
    private readonly bool _firstLine;
    private readonly TextRunProperties _defaultRunProperties;
    private readonly IList<TextTabProperties> _tabs;
    private readonly double _defaultIncrementalTab;

    public QuillTextParagraphProperties(ParagraphLayoutInput input, bool firstLine, TextRunProperties defaultRunProperties)
    {
        ArgumentNullException.ThrowIfNull(input);
        ResolvedParagraphProperties props = input.Properties;
        _alignment = props.Alignment switch
        {
            Alignment.Center => TextAlignment.Center,
            Alignment.Right => TextAlignment.Right,
            Alignment.Justify => TextAlignment.Justify,
            _ => TextAlignment.Left,
        };
        _flowDirection = input.FlowDirection == Layout.FlowDirection.RightToLeft ? System.Windows.FlowDirection.RightToLeft : System.Windows.FlowDirection.LeftToRight;
        _lineHeight = props.LineSpacing.Rule == LineSpacingRule.Exact ? Math.Max(1, props.LineSpacing.Height.ToDips()) : 0;
        _firstLine = firstLine;
        _defaultRunProperties = defaultRunProperties;
        _defaultIncrementalTab = Math.Max(1, input.DefaultTabStop);

        double lineStart = input.LineStart(firstLine);
        var tabs = new List<TextTabProperties>(props.Tabs.Count);
        foreach (TabStop stop in props.Tabs)
        {
            double location = stop.Position.ToDips() - lineStart;
            if (location <= 0 || stop.Alignment == TabAlignment.Bar)
            {
                continue;
            }

            TextTabAlignment alignment = stop.Alignment switch
            {
                TabAlignment.Center => TextTabAlignment.Center,
                TabAlignment.Right => TextTabAlignment.Right,
                TabAlignment.Decimal => TextTabAlignment.Character,
                _ => TextTabAlignment.Left,
            };
            int leader = stop.Leader switch
            {
                TabLeader.Dot => '.',
                TabLeader.Hyphen => '-',
                TabLeader.Underscore => '_',
                TabLeader.MiddleDot => '·',
                TabLeader.Heavy => '_',
                _ => 0,
            };
            tabs.Add(new TextTabProperties(alignment, location, leader, '.'));
        }

        _tabs = tabs;
    }

    public override System.Windows.FlowDirection FlowDirection => _flowDirection;

    public override TextAlignment TextAlignment => _alignment;

    public override double LineHeight => _lineHeight;

    public override bool FirstLineInParagraph => _firstLine;

    public override TextRunProperties DefaultTextRunProperties => _defaultRunProperties;

    public override TextWrapping TextWrapping => TextWrapping.Wrap;

    public override TextMarkerProperties? TextMarkerProperties => null;

    public override double Indent => 0;

    public override double ParagraphIndent => 0;

    public override IList<TextTabProperties> Tabs => _tabs;

    public override double DefaultIncrementalTab => _defaultIncrementalTab;

    public override bool AlwaysCollapsible => false;
}
