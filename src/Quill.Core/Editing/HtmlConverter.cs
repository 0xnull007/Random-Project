using System.Collections.Immutable;
using System.Globalization;
using System.Net;
using System.Text;
using Quill.Core.Model;
using Quill.Core.Styles;
using Quill.Core.Units;

namespace Quill.Core.Editing;

/// <summary>
/// Converts between document fragments and the HTML other applications put on the clipboard (browsers, mail).
/// Import is tolerant: unknown tags are ignored, inline formatting and links are kept, lists become indented
/// paragraphs with a marker, tables become one paragraph per row with tabs between cells.
/// </summary>
public static class HtmlConverter
{
    private static readonly HashSet<string> BlockTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "p", "div", "h1", "h2", "h3", "h4", "h5", "h6", "li", "blockquote", "pre", "section", "article", "header", "footer", "dd", "dt", "figcaption", "hr",
    };

    private static readonly HashSet<string> SkippedTags = new(StringComparer.OrdinalIgnoreCase) { "script", "style", "head", "title", "template", "noscript" };

    // ------------------------------------------------------------------ import

    /// <summary>Parses HTML (or a CF_HTML clipboard payload) into paragraphs.</summary>
    public static DocumentFragment FromHtml(string html)
    {
        ArgumentNullException.ThrowIfNull(html);
        string body = ExtractFragment(html);
        var builder = new ImportBuilder();
        new Parser(body, builder).Run();
        return builder.ToFragment();
    }

    /// <summary>The fragment between the StartFragment/EndFragment markers of a CF_HTML payload, or the whole text.</summary>
    internal static string ExtractFragment(string html)
    {
        int start = html.IndexOf("<!--StartFragment-->", StringComparison.OrdinalIgnoreCase);
        int end = html.IndexOf("<!--EndFragment-->", StringComparison.OrdinalIgnoreCase);
        if (start >= 0 && end > start)
        {
            return html[(start + "<!--StartFragment-->".Length)..end];
        }

        int bodyStart = html.IndexOf("<body", StringComparison.OrdinalIgnoreCase);
        if (bodyStart >= 0)
        {
            int close = html.IndexOf('>', bodyStart);
            int bodyEnd = html.LastIndexOf("</body>", StringComparison.OrdinalIgnoreCase);
            if (close >= 0 && bodyEnd > close)
            {
                return html[(close + 1)..bodyEnd];
            }
        }

        return html;
    }

    private sealed class Parser(string html, ImportBuilder builder)
    {
        private int _position;

        public void Run()
        {
            while (_position < html.Length)
            {
                char c = html[_position];
                if (c == '<')
                {
                    if (html.AsSpan(_position).StartsWith("<!--", StringComparison.Ordinal))
                    {
                        int end = html.IndexOf("-->", _position, StringComparison.Ordinal);
                        _position = end < 0 ? html.Length : end + 3;
                        continue;
                    }

                    int close = html.IndexOf('>', _position);
                    if (close < 0)
                    {
                        break;
                    }

                    string raw = html[(_position + 1)..close].Trim();
                    _position = close + 1;
                    if (raw.Length == 0 || raw[0] == '!' || raw[0] == '?')
                    {
                        continue;
                    }

                    bool closing = raw[0] == '/';
                    bool selfClosing = raw.EndsWith('/');
                    string inner = raw.Trim('/').Trim();
                    int nameEnd = 0;
                    while (nameEnd < inner.Length && !char.IsWhiteSpace(inner[nameEnd]))
                    {
                        nameEnd++;
                    }

                    string name = inner[..nameEnd].ToLowerInvariant();
                    string attributes = inner[nameEnd..];
                    if (closing)
                    {
                        builder.Close(name);
                    }
                    else
                    {
                        if (SkippedTags.Contains(name) && !selfClosing)
                        {
                            int skipTo = html.IndexOf("</" + name, _position, StringComparison.OrdinalIgnoreCase);
                            _position = skipTo < 0 ? html.Length : html.IndexOf('>', skipTo) + 1;
                            continue;
                        }

                        builder.Open(name, attributes);
                        if (selfClosing || name is "br" or "hr" or "img" or "meta" or "link" or "input")
                        {
                            builder.Close(name);
                        }
                    }
                }
                else
                {
                    int next = html.IndexOf('<', _position);
                    if (next < 0)
                    {
                        next = html.Length;
                    }

                    builder.Text(WebUtility.HtmlDecode(html[_position..next]));
                    _position = next;
                }
            }
        }
    }

    private sealed class ImportBuilder
    {
        private readonly List<Block> _blocks = [];
        private readonly Stack<TableBuild> _tables = new();

        private readonly List<Inline> _inlines = [];
        private readonly Stack<(string Tag, RunProperties Format, string? Link)> _stack = new();
        private readonly Stack<(bool Ordered, int Count)> _lists = new();
        private ParagraphProperties _paragraphProperties = ParagraphProperties.Empty;
        private string? _paragraphStyle;
        private bool _inParagraph;
        private bool _pre;
        private bool _lastWasSpace = true;

        private RunProperties CurrentFormat => _stack.Count > 0 ? _stack.Peek().Format : RunProperties.Empty;

        private string? CurrentLink => _stack.Count > 0 ? _stack.Peek().Link : null;

        public void Open(string tag, string attributes)
        {
            string? style = Attribute(attributes, "style");
            if (BlockTags.Contains(tag))
            {
                EndParagraph();
                _inParagraph = true;
                _paragraphProperties = BlockProperties(tag, style);
                _paragraphStyle = tag switch
                {
                    "h1" => DefaultStyleSheet.Heading1Id,
                    "h2" => DefaultStyleSheet.Heading2Id,
                    "h3" => DefaultStyleSheet.Heading3Id,
                    _ => null,
                };
                if (tag == "pre")
                {
                    _pre = true;
                }

                if (tag == "li")
                {
                    StartListItem();
                }

                if (tag == "hr")
                {
                    _inlines.Add(new Run(new string('─', 30)));
                }
            }
            else if (tag is "ul" or "ol")
            {
                EndParagraph();
                _lists.Push((tag == "ol", 0));
            }
            else if (tag == "table")
            {
                EndParagraph();
                string? border = Attribute(attributes, "border");
                _tables.Push(new TableBuild { Borders = border is null || border.Trim() != "0" });
            }
            else if (tag == "tr" && _tables.Count > 0)
            {
                EndParagraph();
                _tables.Peek().StartRow();
            }
            else if (tag == "tr")
            {
                EndParagraph();
                _inParagraph = true;
            }
            else if (tag is "td" or "th")
            {
                if (_tables.Count > 0)
                {
                    EndParagraph();
                    TableBuild table = _tables.Peek();
                    int span = int.TryParse(Attribute(attributes, "colspan"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int colspan) ? Math.Max(1, colspan) : 1;
                    DocColor? shading = Attribute(attributes, "bgcolor") is { } bg ? ParseColor(bg) : StyleBackground(style);
                    if (style is not null && style.Contains("border", StringComparison.OrdinalIgnoreCase) && !style.Contains("border:none", StringComparison.OrdinalIgnoreCase) && !style.Contains("border: none", StringComparison.OrdinalIgnoreCase))
                    {
                        table.Borders = true;
                    }

                    table.StartCell(span, shading, CellParagraphProperties(style));

                }
                else if (_inlines.Count > 0)
                {
                    _inlines.Add(new Run("\t", CurrentFormat));
                }

                _lastWasSpace = true;
            }

            else if (tag == "br")
            {
                EnsureParagraph();
                _inlines.Add(new Break(BreakKind.Line));
                _lastWasSpace = true;
            }

            RunProperties format = CurrentFormat;
            string? link = CurrentLink;
            switch (tag)
            {
                case "b" or "strong":
                    format = format with { Bold = true };
                    break;
                case "i" or "em" or "cite" or "var":
                    format = format with { Italic = true };
                    break;
                case "u" or "ins":
                    format = format with { Underline = UnderlineStyle.Single };
                    break;
                case "s" or "strike" or "del":
                    format = format with { Strikethrough = true };
                    break;
                case "sup":
                    format = format with { VerticalAlignment = VerticalTextAlignment.Superscript };
                    break;
                case "sub":
                    format = format with { VerticalAlignment = VerticalTextAlignment.Subscript };
                    break;
                case "code" or "kbd" or "samp" or "tt" or "pre":
                    format = format with { FontFamily = "Consolas" };
                    break;
                case "mark":
                    format = format with { Highlight = HighlightColor.Yellow };
                    break;
                case "a":
                    if (Attribute(attributes, "href") is { Length: > 0 } href && !href.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase))
                    {
                        link = href;
                    }

                    break;
                case "font":
                    if (Attribute(attributes, "color") is { } fontColor && ParseColor(fontColor) is { } fc)
                    {
                        format = format with { Color = fc };
                    }

                    if (Attribute(attributes, "face") is { Length: > 0 } face)
                    {
                        format = format with { FontFamily = face.Split(',')[0].Trim('\'', '"', ' ') };
                    }

                    break;
                case "th":
                    format = format with { Bold = true };
                    break;
            }

            if (tag is "h1" or "h2" or "h3" or "h4" or "h5" or "h6")
            {
                format = tag is "h4" or "h5" or "h6" ? format with { Bold = true } : format;
            }

            format = ApplyInlineStyle(format, style);
            _stack.Push((tag, format, link));
        }

        public void Close(string tag)
        {
            // Pop to the matching tag (tolerating unbalanced markup).
            var popped = new Stack<(string, RunProperties, string?)>();
            bool found = false;
            while (_stack.Count > 0)
            {
                var top = _stack.Pop();
                if (string.Equals(top.Tag, tag, StringComparison.OrdinalIgnoreCase))
                {
                    found = true;
                    break;
                }

                popped.Push(top);
            }

            if (!found)
            {
                while (popped.Count > 0)
                {
                    _stack.Push(popped.Pop());
                }

                return;
            }

            if (BlockTags.Contains(tag))
            {
                EndParagraph();
                if (tag == "pre")
                {
                    _pre = false;
                }
            }
            else if (tag is "ul" or "ol")
            {
                EndParagraph();
                if (_lists.Count > 0)
                {
                    _lists.Pop();
                }
            }
            else if (tag is "td" or "th" && _tables.Count > 0)
            {
                EndParagraph();
                _tables.Peek().EndCell();
            }
            else if (tag == "tr")
            {
                EndParagraph();
                if (_tables.Count > 0)
                {
                    _tables.Peek().EndRow();
                }
            }
            else if (tag == "table" && _tables.Count > 0)
            {
                EndParagraph();
                TableBuild finished = _tables.Pop();
                if (finished.ToTable() is { } table)
                {
                    Target.Add(table);
                }
            }
        }

        /// <summary>Where finished blocks go: the open table cell, or the fragment itself.</summary>
        private List<Block> Target => _tables.Count > 0 && _tables.Peek().CurrentCell is { } cell ? cell : _blocks;


        public void Text(string text)
        {
            if (text.Length == 0)
            {
                return;
            }

            if (_pre)
            {
                EnsureParagraph();
                string[] lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
                for (int i = 0; i < lines.Length; i++)
                {
                    if (i > 0)
                    {
                        _inlines.Add(new Break(BreakKind.Line));
                    }

                    if (lines[i].Length > 0)
                    {
                        _inlines.Add(new Run(lines[i], CurrentFormat));
                    }
                }

                return;
            }

            var collapsed = new StringBuilder(text.Length);
            foreach (char c in text)
            {
                if (char.IsWhiteSpace(c) && c != ' ')
                {
                    if (!_lastWasSpace)
                    {
                        collapsed.Append(' ');
                        _lastWasSpace = true;
                    }
                }
                else
                {
                    collapsed.Append(c);
                    _lastWasSpace = false;
                }
            }

            string result = collapsed.ToString();
            if (result.Length == 0 || (!_inParagraph && result.Trim().Length == 0))
            {
                return;
            }

            EnsureParagraph();
            RunProperties format = CurrentLink is { } link ? CurrentFormat with { Link = link, Color = CurrentFormat.Color ?? DocColor.Parse("0563C1"), Underline = CurrentFormat.Underline ?? UnderlineStyle.Single } : CurrentFormat;
            _inlines.Add(new Run(result, format));
        }

        public DocumentFragment ToFragment()
        {
            EndParagraph();
            while (_tables.Count > 0)
            {
                TableBuild unclosed = _tables.Pop();
                if (unclosed.ToTable() is { } table)
                {
                    Target.Add(table);
                }
            }

            return new DocumentFragment(_blocks.ToImmutableArray());
        }


        private void EnsureParagraph()
        {
            if (!_inParagraph)
            {
                _inParagraph = true;
                _paragraphProperties = _tables.Count > 0 && _tables.Peek().CurrentCell is not null ? _tables.Peek().CellDefaults : ParagraphProperties.Empty;
                _paragraphStyle = null;
            }
        }


        private void EndParagraph()
        {
            if (!_inParagraph)
            {
                return;
            }

            // Trim the trailing space HTML leaves before a closing tag.
            if (_inlines.Count > 0 && _inlines[^1] is Run last && last.Text.EndsWith(' ') && last.Text.Trim().Length == 0)
            {
                _inlines.RemoveAt(_inlines.Count - 1);
            }
            else if (_inlines.Count > 0 && _inlines[^1] is Run trailing && trailing.Text.EndsWith(' '))
            {
                _inlines[^1] = trailing.WithText(trailing.Text.TrimEnd(' '));
            }

            Target.Add(new Paragraph([.. _inlines], _paragraphStyle, _paragraphProperties));
            _inlines.Clear();
            _inParagraph = false;
            _lastWasSpace = true;
        }

        private void StartListItem()
        {
            int depth = Math.Max(1, _lists.Count);
            string marker;
            if (_lists.Count > 0)
            {
                (bool ordered, int count) = _lists.Pop();
                count++;
                _lists.Push((ordered, count));
                marker = ordered ? NumberText.Format(count, NumberFormat.Decimal) + "." : (depth % 3) switch { 1 => "\u2022", 2 => "o", _ => "\u25AA" };
            }
            else
            {
                marker = "•";
            }

            _paragraphProperties = _paragraphProperties with
            {
                LeftIndent = Twips.FromInches(0.5 * depth),
                FirstLineIndent = -Twips.FromInches(0.25),
            };
            _inlines.Add(new Run(marker + "\t"));
            _lastWasSpace = true;
        }

        internal static ParagraphProperties BlockProperties(string tag, string? style)
        {
            var properties = ParagraphProperties.Empty;
            if (tag == "blockquote")
            {
                properties = properties with { LeftIndent = Twips.FromInches(0.5) };
            }

            if (StyleValue(style, "text-align") is { } align)
            {
                properties = properties with
                {
                    Alignment = align.ToLowerInvariant() switch
                    {
                        "center" => Alignment.Center,
                        "right" or "end" => Alignment.Right,
                        "justify" => Alignment.Justify,
                        _ => Alignment.Left,
                    },
                };
            }

            if (StyleValue(style, "margin-left") is { } marginLeft && ParseLength(marginLeft) is { } left && left.Value > 0)
            {
                properties = properties with { LeftIndent = left };
            }

            if (StyleValue(style, "text-indent") is { } textIndent && ParseLength(textIndent) is { } first)
            {
                properties = properties with { FirstLineIndent = first };
            }

            return properties;
        }

        private static RunProperties ApplyInlineStyle(RunProperties format, string? style)
        {
            if (string.IsNullOrEmpty(style))
            {
                return format;
            }

            if (StyleValue(style, "font-weight") is { } weight && (weight.StartsWith("bold", StringComparison.OrdinalIgnoreCase) || (int.TryParse(weight, out int w) && w >= 600)))
            {
                format = format with { Bold = true };
            }
            else if (StyleValue(style, "font-weight") is "normal" or "400")
            {
                format = format with { Bold = false };
            }

            if (StyleValue(style, "font-style") is { } fontStyle)
            {
                format = format with { Italic = fontStyle.Contains("italic", StringComparison.OrdinalIgnoreCase) || fontStyle.Contains("oblique", StringComparison.OrdinalIgnoreCase) };
            }

            if (StyleValue(style, "text-decoration") is { } decoration || StyleValue(style, "text-decoration-line") is { } decorationLine)
            {
                string value = (StyleValue(style, "text-decoration") ?? StyleValue(style, "text-decoration-line") ?? string.Empty).ToLowerInvariant();
                if (value.Contains("underline", StringComparison.Ordinal))
                {
                    format = format with { Underline = UnderlineStyle.Single };
                }

                if (value.Contains("line-through", StringComparison.Ordinal))
                {
                    format = format with { Strikethrough = true };
                }

                if (value.Contains("none", StringComparison.Ordinal))
                {
                    format = format with { Underline = UnderlineStyle.None };
                }
            }

            if (StyleValue(style, "color") is { } color && ParseColor(color) is { } c)
            {
                format = format with { Color = c };
            }

            if (StyleValue(style, "background-color") is { } background && ParseColor(background) is { } b)
            {
                format = format with { Highlight = NearestHighlight(b) };
            }

            if (StyleValue(style, "font-family") is { } family)
            {
                string name = family.Split(',')[0].Trim('\'', '"', ' ');
                if (name.Length > 0 && !name.Equals("inherit", StringComparison.OrdinalIgnoreCase))
                {
                    format = format with { FontFamily = name };
                }
            }

            if (StyleValue(style, "font-size") is { } size && ParseFontSize(size) is { } points)
            {
                format = format with { FontSize = HalfPoints.FromPoints(points) };
            }

            if (StyleValue(style, "vertical-align") is { } vertical)
            {
                if (vertical.Contains("super", StringComparison.OrdinalIgnoreCase))
                {
                    format = format with { VerticalAlignment = VerticalTextAlignment.Superscript };
                }
                else if (vertical.Contains("sub", StringComparison.OrdinalIgnoreCase))
                {
                    format = format with { VerticalAlignment = VerticalTextAlignment.Subscript };
                }
            }

            return format;
        }
    }

    // ------------------------------------------------------------------ helpers

    /// <summary>Collects the rows and cells of an HTML table while its content is parsed.</summary>
    private sealed class TableBuild
    {
        private readonly List<List<(List<Block> Blocks, int Span, DocColor? Shading)>> _rows = [];
        private List<(List<Block> Blocks, int Span, DocColor? Shading)>? _row;

        public bool Borders { get; set; }

        public List<Block>? CurrentCell { get; private set; }

        /// <summary>Paragraph formatting for text typed straight into the open cell (no space after, single spacing).</summary>
        public ParagraphProperties CellDefaults { get; private set; } = ParagraphProperties.Empty;

        public void StartRow()
        {
            _row = [];
            _rows.Add(_row);
            CurrentCell = null;
        }

        public void StartCell(int span, DocColor? shading, ParagraphProperties defaults)
        {
            if (_row is null)
            {
                StartRow();
            }

            CurrentCell = [];
            CellDefaults = defaults;
            _row!.Add((CurrentCell, span, shading));
        }


        public void EndCell() => CurrentCell = null;

        public void EndRow()
        {
            CurrentCell = null;
            _row = null;
        }

        public Table? ToTable()
        {
            var rows = ImmutableArray.CreateBuilder<TableRow>();
            foreach (var row in _rows)
            {
                if (row.Count == 0)
                {
                    continue;
                }

                rows.Add(new TableRow(row.Select(c => new TableCell(
                    c.Blocks.Count == 0 ? ImmutableList.Create<Block>(Paragraph.Empty()) : ImmutableList.CreateRange(c.Blocks),
                    c.Span,
                    VerticalMerge.None,
                    c.Shading)).ToImmutableArray()));
            }

            if (rows.Count == 0)
            {
                return null;
            }

            int columns = Math.Max(1, rows.Max(r => r.GridWidth));
            var width = new Twips(Twips.FromInches(6.5).Value / columns);
            return new Table(rows.ToImmutable(), Enumerable.Repeat(width, columns).ToImmutableArray(), Borders, Twips.Zero);
        }
    }

    private static ParagraphProperties CellParagraphProperties(string? style)
    {
        ParagraphProperties properties = ImportBuilder.BlockProperties("td", style);
        return properties with { SpaceBefore = properties.SpaceBefore ?? Twips.Zero, SpaceAfter = properties.SpaceAfter ?? Twips.Zero, LineSpacing = properties.LineSpacing ?? LineSpacing.Single };
    }

    private static DocColor? StyleBackground(string? style)
    {
        if (style is null)
        {
            return null;
        }

        foreach (string declaration in style.Split(';'))
        {
            int colon = declaration.IndexOf(':', StringComparison.Ordinal);
            if (colon < 0)
            {
                continue;
            }

            string name = declaration[..colon].Trim();
            if (name.Equals("background-color", StringComparison.OrdinalIgnoreCase) || name.Equals("background", StringComparison.OrdinalIgnoreCase))
            {
                string value = declaration[(colon + 1)..].Trim();
                return ParseColor(value.Split(' ')[0]);
            }
        }

        return null;
    }

    private static string? Attribute(string attributes, string name)

    {
        int index = 0;
        while (index < attributes.Length)
        {
            int found = attributes.IndexOf(name, index, StringComparison.OrdinalIgnoreCase);
            if (found < 0)
            {
                return null;
            }

            bool boundaryBefore = found == 0 || char.IsWhiteSpace(attributes[found - 1]);
            int after = found + name.Length;
            while (after < attributes.Length && char.IsWhiteSpace(attributes[after]))
            {
                after++;
            }

            if (!boundaryBefore || after >= attributes.Length || attributes[after] != '=')
            {
                index = found + 1;
                continue;
            }

            after++;
            while (after < attributes.Length && char.IsWhiteSpace(attributes[after]))
            {
                after++;
            }

            if (after >= attributes.Length)
            {
                return string.Empty;
            }

            char quote = attributes[after];
            if (quote is '"' or '\'')
            {
                int end = attributes.IndexOf(quote, after + 1);
                return WebUtility.HtmlDecode(end < 0 ? attributes[(after + 1)..] : attributes[(after + 1)..end]);
            }

            int space = after;
            while (space < attributes.Length && !char.IsWhiteSpace(attributes[space]))
            {
                space++;
            }

            return WebUtility.HtmlDecode(attributes[after..space]);
        }

        return null;
    }

    internal static string? StyleValue(string? style, string property)
    {
        if (string.IsNullOrEmpty(style))
        {
            return null;
        }

        foreach (string declaration in style.Split(';'))
        {
            int colon = declaration.IndexOf(':', StringComparison.Ordinal);
            if (colon > 0 && declaration[..colon].Trim().Equals(property, StringComparison.OrdinalIgnoreCase))
            {
                string value = declaration[(colon + 1)..].Trim();
                int important = value.IndexOf("!important", StringComparison.OrdinalIgnoreCase);
                return (important >= 0 ? value[..important] : value).Trim();
            }
        }

        return null;
    }

    private static readonly Dictionary<string, string> NamedColors = new(StringComparer.OrdinalIgnoreCase)
    {
        ["black"] = "000000", ["white"] = "FFFFFF", ["red"] = "FF0000", ["green"] = "008000", ["blue"] = "0000FF", ["yellow"] = "FFFF00",
        ["gray"] = "808080", ["grey"] = "808080", ["silver"] = "C0C0C0", ["maroon"] = "800000", ["navy"] = "000080", ["teal"] = "008080",
        ["purple"] = "800080", ["orange"] = "FFA500", ["lime"] = "00FF00", ["aqua"] = "00FFFF", ["cyan"] = "00FFFF", ["fuchsia"] = "FF00FF",
        ["magenta"] = "FF00FF", ["olive"] = "808000", ["brown"] = "A52A2A", ["pink"] = "FFC0CB", ["darkblue"] = "00008B", ["darkred"] = "8B0000",
        ["darkgreen"] = "006400", ["lightgray"] = "D3D3D3", ["lightgrey"] = "D3D3D3", ["gold"] = "FFD700", ["violet"] = "EE82EE",
    };

    internal static DocColor? ParseColor(string value)
    {
        string v = value.Trim();
        if (v.StartsWith('#'))
        {
            string hex = v[1..];
            if (hex.Length == 3)
            {
                hex = string.Concat(hex[0], hex[0], hex[1], hex[1], hex[2], hex[2]);
            }

            if (hex.Length >= 6 && int.TryParse(hex[..6], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out _))
            {
                return DocColor.Parse(hex[..6]);
            }

            return null;
        }

        if (v.StartsWith("rgb", StringComparison.OrdinalIgnoreCase))
        {
            int open = v.IndexOf('(');
            int close = v.IndexOf(')');
            if (open >= 0 && close > open)
            {
                string[] parts = v[(open + 1)..close].Split(',', ' ', '/').Where(p => p.Length > 0).ToArray();
                if (parts.Length >= 3 && byte.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out byte r)
                    && byte.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out byte g)
                    && byte.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out byte b))
                {
                    if (parts.Length >= 4 && double.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out double alpha) && alpha == 0)
                    {
                        return null; // transparent
                    }

                    return DocColor.FromRgb(r, g, b);
                }
            }

            return null;
        }

        if (v.Equals("transparent", StringComparison.OrdinalIgnoreCase) || v.Equals("inherit", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return NamedColors.TryGetValue(v, out string? named) ? DocColor.Parse(named) : null;
    }

    private static HighlightColor NearestHighlight(DocColor color)
    {
        (HighlightColor Highlight, int R, int G, int B)[] palette =
        [
            (HighlightColor.Yellow, 255, 255, 0), (HighlightColor.Green, 0, 255, 0), (HighlightColor.Cyan, 0, 255, 255), (HighlightColor.Magenta, 255, 0, 255),
            (HighlightColor.Blue, 0, 0, 255), (HighlightColor.Red, 255, 0, 0), (HighlightColor.DarkBlue, 0, 0, 128), (HighlightColor.DarkCyan, 0, 128, 128),
            (HighlightColor.DarkGreen, 0, 128, 0), (HighlightColor.DarkMagenta, 128, 0, 128), (HighlightColor.DarkRed, 128, 0, 0), (HighlightColor.DarkYellow, 128, 128, 0),
            (HighlightColor.DarkGray, 128, 128, 128), (HighlightColor.LightGray, 192, 192, 192), (HighlightColor.Black, 0, 0, 0),
        ];
        if (color.R > 235 && color.G > 235 && color.B > 235)
        {
            return HighlightColor.None; // white-ish backgrounds are the page, not a highlight
        }

        HighlightColor best = HighlightColor.Yellow;
        int bestDistance = int.MaxValue;
        foreach ((HighlightColor highlight, int r, int g, int b) in palette)
        {
            int distance = (r - color.R) * (r - color.R) + (g - color.G) * (g - color.G) + (b - color.B) * (b - color.B);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = highlight;
            }
        }

        return best;
    }

    internal static double? ParseFontSize(string value)
    {
        string v = value.Trim().ToLowerInvariant();
        double number;
        if (v.EndsWith("pt", StringComparison.Ordinal) && double.TryParse(v[..^2], NumberStyles.Float, CultureInfo.InvariantCulture, out number))
        {
            return number;
        }

        if (v.EndsWith("px", StringComparison.Ordinal) && double.TryParse(v[..^2], NumberStyles.Float, CultureInfo.InvariantCulture, out number))
        {
            return number * 0.75;
        }

        if (v.EndsWith("em", StringComparison.Ordinal) && double.TryParse(v[..^2], NumberStyles.Float, CultureInfo.InvariantCulture, out number))
        {
            return number * 11;
        }

        if (v.EndsWith('%') && double.TryParse(v[..^1], NumberStyles.Float, CultureInfo.InvariantCulture, out number))
        {
            return number / 100 * 11;
        }

        return v switch
        {
            "xx-small" => 7, "x-small" => 7.5, "small" => 10, "medium" => 11, "large" => 13.5, "x-large" => 18, "xx-large" => 24,
            _ => null,
        };
    }

    internal static Twips? ParseLength(string value)
    {
        string v = value.Trim().ToLowerInvariant();
        if (v == "0")
        {
            return Twips.Zero;
        }

        if (v.EndsWith("pt", StringComparison.Ordinal) && double.TryParse(v[..^2], NumberStyles.Float, CultureInfo.InvariantCulture, out double pt))
        {
            return Twips.FromPoints(pt);
        }

        if (v.EndsWith("px", StringComparison.Ordinal) && double.TryParse(v[..^2], NumberStyles.Float, CultureInfo.InvariantCulture, out double px))
        {
            return Twips.FromDips(px);
        }

        if (v.EndsWith("in", StringComparison.Ordinal) && double.TryParse(v[..^2], NumberStyles.Float, CultureInfo.InvariantCulture, out double inches))
        {
            return Twips.FromInches(inches);
        }

        if (v.EndsWith("cm", StringComparison.Ordinal) && double.TryParse(v[..^2], NumberStyles.Float, CultureInfo.InvariantCulture, out double cm))
        {
            return Twips.FromCentimeters(cm);
        }

        if (v.EndsWith("em", StringComparison.Ordinal) && double.TryParse(v[..^2], NumberStyles.Float, CultureInfo.InvariantCulture, out double em))
        {
            return Twips.FromPoints(em * 11);
        }

        return null;
    }

    // ------------------------------------------------------------------ export

    /// <summary>HTML for the clipboard (CF_HTML with its header), from the resolved formatting of each run.</summary>
    public static string ToClipboardHtml(DocumentFragment fragment, StyleResolver resolver, ListStore lists)
    {
        string html = ToHtml(fragment, resolver, lists);
        const string header = "Version:0.9\r\nStartHTML:{0:D10}\r\nEndHTML:{1:D10}\r\nStartFragment:{2:D10}\r\nEndFragment:{3:D10}\r\n";
        string prefix = "<html><body>\r\n<!--StartFragment-->";
        string suffix = "<!--EndFragment-->\r\n</body></html>";
        int headerLength = Encoding.UTF8.GetByteCount(string.Format(CultureInfo.InvariantCulture, header, 0, 0, 0, 0));
        int startHtml = headerLength;
        int startFragment = startHtml + Encoding.UTF8.GetByteCount(prefix);
        int endFragment = startFragment + Encoding.UTF8.GetByteCount(html);
        int endHtml = endFragment + Encoding.UTF8.GetByteCount(suffix);
        return string.Format(CultureInfo.InvariantCulture, header, startHtml, endHtml, startFragment, endFragment) + prefix + html + suffix;
    }

    public static string ToHtml(DocumentFragment fragment, StyleResolver resolver, ListStore lists)
    {
        ArgumentNullException.ThrowIfNull(fragment);
        ArgumentNullException.ThrowIfNull(resolver);
        ArgumentNullException.ThrowIfNull(lists);
        var html = new StringBuilder();
        AppendBlocks(html, ImmutableList.CreateRange(fragment.Blocks), resolver, lists);
        return html.ToString();
    }

    private static void AppendBlocks(StringBuilder html, ImmutableList<Block> blocks, StyleResolver resolver, ListStore lists)
    {
        IReadOnlyDictionary<int, ListMarker> markers = ListNumbering.Compute(blocks, lists, resolver);
        for (int i = 0; i < blocks.Count; i++)
        {
            switch (blocks[i])
            {
                case Paragraph paragraph:
                    AppendParagraph(html, paragraph, markers.TryGetValue(i, out ListMarker marker) ? marker : null, resolver);
                    break;
                case Table table:
                    AppendTable(html, table, resolver, lists);
                    break;
            }
        }
    }

    private static void AppendTable(StringBuilder html, Table table, StyleResolver resolver, ListStore lists)
    {
        html.Append(table.HasBorders ? "<table border=\"1\" cellspacing=\"0\" cellpadding=\"4\" style=\"border-collapse:collapse\">\r\n" : "<table cellspacing=\"0\" cellpadding=\"4\">\r\n");
        int columns = Math.Max(1, table.ColumnCount);
        foreach (TableRow row in table.Rows)
        {
            html.Append("<tr>");
            foreach (TableCell cell in row.Cells)
            {
                if (cell.VerticalMerge == VerticalMerge.Continue)
                {
                    continue;
                }

                html.Append("<td");
                if (cell.GridSpan > 1)
                {
                    html.Append(" colspan=\"").Append(cell.GridSpan).Append('"');
                }

                int gridStart = row.Cells.TakeWhile(c => !ReferenceEquals(c, cell)).Sum(c => c.GridSpan);
                double percent = 100.0 * Math.Min(cell.GridSpan, columns - gridStart) / columns;
                var style = new StringBuilder("width:").Append(percent.ToString("0.#", CultureInfo.InvariantCulture)).Append("%;vertical-align:top;");
                if (cell.Shading is { IsAuto: false } shading)
                {
                    style.Append("background-color:#").Append(shading.ToHex()).Append(';');
                }

                html.Append(" style=\"").Append(style).Append("\">");
                AppendBlocks(html, cell.Blocks, resolver, lists);
                html.Append("</td>");
            }

            html.Append("</tr>\r\n");
        }

        html.Append("</table>\r\n");
    }

    private static void AppendParagraph(StringBuilder html, Paragraph paragraph, ListMarker? marker, StyleResolver resolver)
    {
        ResolvedParagraphProperties pp = resolver.ResolveParagraph(paragraph);
        string tag = paragraph.StyleId switch
        {
            DefaultStyleSheet.Heading1Id => "h1",
            DefaultStyleSheet.Heading2Id => "h2",
            DefaultStyleSheet.Heading3Id => "h3",
            _ => "p",
        };
        var style = new StringBuilder();
        if (pp.Alignment != Alignment.Left)
        {
            style.Append("text-align:").Append(pp.Alignment switch { Alignment.Center => "center", Alignment.Right => "right", _ => "justify" }).Append(';');
        }

        if (pp.LeftIndent.Value > 0)
        {
            style.Append("margin-left:").Append(pp.LeftIndent.ToPoints().ToString("0.#", CultureInfo.InvariantCulture)).Append("pt;");
        }

        html.Append('<').Append(tag);
        if (style.Length > 0)
        {
            html.Append(" style=\"").Append(style).Append('"');
        }

        html.Append('>');
        if (marker is { } listMarker)
        {
            html.Append(WebUtility.HtmlEncode(listMarker.Text)).Append("&nbsp;");
        }

        foreach (Inline inline in paragraph.Inlines)
        {
            switch (inline)
            {
                case Run run:
                    AppendRun(html, run.Text, resolver.ResolveRun(paragraph, run), run.Properties.Link);
                    break;
                case Break { Kind: BreakKind.Line }:
                    html.Append("<br>");
                    break;
                case Field field:
                    AppendRun(html, field.CachedResult, resolver.ResolveRun(paragraph, field), null);
                    break;
            }
        }

        html.Append("</").Append(tag).Append(">\r\n");
    }

    private static void AppendRun(StringBuilder html, string text, ResolvedRunProperties p, string? link)
    {
        if (text.Length == 0)
        {
            return;
        }

        var style = new StringBuilder();
        style.Append("font-family:'").Append(p.FontFamily.Replace("'", string.Empty, StringComparison.Ordinal)).Append("';font-size:").Append(p.FontSize.ToPoints().ToString("0.#", CultureInfo.InvariantCulture)).Append("pt;");
        if (p.Bold)
        {
            style.Append("font-weight:bold;");
        }

        if (p.Italic)
        {
            style.Append("font-style:italic;");
        }

        var decorations = new List<string>();
        if (p.Underline != UnderlineStyle.None)
        {
            decorations.Add("underline");
        }

        if (p.Strikethrough || p.DoubleStrikethrough)
        {
            decorations.Add("line-through");
        }

        if (decorations.Count > 0)
        {
            style.Append("text-decoration:").Append(string.Join(' ', decorations)).Append(';');
        }

        if (!p.Color.IsAuto)
        {
            style.Append("color:#").Append(p.Color.ToHex()).Append(';');
        }

        if (p.Highlight != HighlightColor.None && HighlightHex(p.Highlight) is { } highlight)
        {
            style.Append("background-color:#").Append(highlight).Append(';');
        }

        if (p.VerticalAlignment == VerticalTextAlignment.Superscript)
        {
            style.Append("vertical-align:super;font-size:smaller;");
        }
        else if (p.VerticalAlignment == VerticalTextAlignment.Subscript)
        {
            style.Append("vertical-align:sub;font-size:smaller;");
        }

        string encoded = WebUtility.HtmlEncode(p.AllCaps ? text.ToUpperInvariant() : text).Replace("\t", "&emsp;", StringComparison.Ordinal);
        if (link is { Length: > 0 })
        {
            html.Append("<a href=\"").Append(WebUtility.HtmlEncode(link)).Append("\" style=\"").Append(style).Append("\">").Append(encoded).Append("</a>");
        }
        else
        {
            html.Append("<span style=\"").Append(style).Append("\">").Append(encoded).Append("</span>");
        }
    }

    private static string? HighlightHex(HighlightColor highlight) => highlight switch
    {
        HighlightColor.Yellow => "FFFF00",
        HighlightColor.Green => "00FF00",
        HighlightColor.Cyan => "00FFFF",
        HighlightColor.Magenta => "FF00FF",
        HighlightColor.Blue => "0000FF",
        HighlightColor.Red => "FF0000",
        HighlightColor.DarkBlue => "000080",
        HighlightColor.DarkCyan => "008080",
        HighlightColor.DarkGreen => "008000",
        HighlightColor.DarkMagenta => "800080",
        HighlightColor.DarkRed => "800000",
        HighlightColor.DarkYellow => "808000",
        HighlightColor.DarkGray => "808080",
        HighlightColor.LightGray => "C0C0C0",
        HighlightColor.Black => "000000",
        _ => null,
    };
}
