using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using Quill.Core.Model;
using Quill.Core.Units;
using A = DocumentFormat.OpenXml.Drawing;
using DW = DocumentFormat.OpenXml.Drawing.Wordprocessing;
using W = DocumentFormat.OpenXml.Wordprocessing;
using static Quill.Docx.OoxmlValues;

namespace Quill.Docx;

/// <summary>
/// Reads a .docx into the document model. Unsupported block content is preserved as <see cref="OpaqueBlock"/>;
/// unsupported inline content is dropped and reported through <see cref="LoadResult.Warnings"/>.
/// </summary>
public sealed class DocxReader
{
    private readonly List<LoadWarning> _warnings = [];
    private readonly HashSet<string> _warned = new(StringComparer.Ordinal);
    private ThemeInfo _theme = ThemeInfo.Default;
    private MainDocumentPart? _main;
    private OpenXmlPart? _part;
    private readonly ImmutableDictionary<string, ImageData>.Builder _images = ImmutableDictionary.CreateBuilder<string, ImageData>(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _imageIdsByUri = new(StringComparer.Ordinal);

    private DocxReader()
    {
    }

    /// <summary>Reads a document from a stream. The stream is copied, so the caller may close it immediately.</summary>
    public static LoadResult Read(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var memory = new MemoryStream();
        stream.CopyTo(memory);
        memory.Position = 0;
        return new DocxReader().ReadCore(memory);
    }

    /// <summary>Reads a file without holding a lock on it afterwards (Word may have it open).</summary>
    public static LoadResult ReadFile(string path)
    {
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        return Read(file);
    }

    private LoadResult ReadCore(Stream stream)
    {
        using WordprocessingDocument package = WordprocessingDocument.Open(stream, false);
        _main = package.MainDocumentPart ?? throw new InvalidDataException("The file has no main document part.");
        _part = _main;
        _theme = ThemeInfo.From(_main.ThemePart);
        StyleSheet styles = ReadStyles(_main.StyleDefinitionsPart);
        DocumentSettings settings = ReadSettings(_main.DocumentSettingsPart);
        ListStore lists = ReadNumbering(_main.NumberingDefinitionsPart);
        W.Body body = _main.Document?.Body ?? new W.Body();
        ImmutableList<Section> sections = ReadSections(body);
        DocumentMetadata metadata = new()
        {
            Title = package.PackageProperties.Title,
            Author = package.PackageProperties.Creator,
            Subject = package.PackageProperties.Subject,
            Description = package.PackageProperties.Description,
            Created = package.PackageProperties.Created,
            Modified = package.PackageProperties.Modified,
        };
        return new LoadResult(new Document(sections, styles, settings, metadata, lists, new ImageStore(_images.ToImmutable())), _warnings);
    }

    // ------------------------------------------------------------------ sections and stories

    private ImmutableList<Section> ReadSections(W.Body body)
    {
        var sections = ImmutableList.CreateBuilder<Section>();
        var blocks = ImmutableList.CreateBuilder<Block>();
        foreach (OpenXmlElement element in body.ChildElements)
        {
            if (element is W.Paragraph paragraph)
            {
                blocks.Add(ReadParagraph(paragraph));
                if (paragraph.ParagraphProperties?.SectionProperties is { } sectPr)
                {
                    sections.Add(BuildSection(sectPr, EnsureParagraph(blocks.ToImmutable())));
                    blocks.Clear();
                }
            }
            else if (element is W.SectionProperties finalSectPr)
            {
                sections.Add(BuildSection(finalSectPr, EnsureParagraph(blocks.ToImmutable())));
                blocks.Clear();
            }
            else
            {
                ReadBlock(element, blocks);
            }
        }

        if (blocks.Count > 0 || sections.Count == 0)
        {
            sections.Add(new Section(SectionProperties.Letter, EnsureParagraph(blocks.ToImmutable())));
        }

        return sections.ToImmutable();
    }

    /// <summary>Reads a header or footer; pictures inside it are related to <paramref name="part"/>, not to the main part.</summary>
    private ImmutableList<Block> ReadStory(OpenXmlElement? container, OpenXmlPart part)
    {
        OpenXmlPart? previous = _part;
        _part = part;
        try
        {
            var blocks = ImmutableList.CreateBuilder<Block>();
            if (container is not null)
            {
                foreach (OpenXmlElement element in container.ChildElements)
                {
                    ReadBlock(element, blocks);
                }
            }

            return EnsureParagraph(blocks.ToImmutable());
        }
        finally
        {
            _part = previous;
        }
    }


    private void ReadBlock(OpenXmlElement element, ImmutableList<Block>.Builder blocks)
    {
        switch (element)
        {
            case W.Paragraph paragraph:
                blocks.Add(ReadParagraph(paragraph));
                break;
            case W.Table table:
                blocks.Add(new OpaqueBlock("tbl", table.OuterXml));
                Warn("Tables are shown as placeholders and cannot be edited in this version.");
                break;
            case W.SdtBlock sdt:
                foreach (OpenXmlElement child in sdt.SdtContentBlock?.ChildElements ?? [])
                {
                    ReadBlock(child, blocks);
                }

                break;
            case W.CustomXmlBlock customXml:
                foreach (OpenXmlElement child in customXml.ChildElements)
                {
                    ReadBlock(child, blocks);
                }

                break;
            case W.BookmarkStart or W.BookmarkEnd or W.ProofError or W.CommentRangeStart or W.CommentRangeEnd or W.SectionProperties:
                break;
            default:
                blocks.Add(new OpaqueBlock(element.LocalName, element.OuterXml));
                Warn($"Unsupported content ({element.LocalName}) is preserved but not shown.");
                break;
        }
    }

    private static ImmutableList<Block> EnsureParagraph(ImmutableList<Block> blocks) =>
        blocks.Any(b => b is Paragraph) ? blocks : blocks.Add(Paragraph.Empty());

    private Section BuildSection(W.SectionProperties sectPr, ImmutableList<Block> body)
    {
        var props = new SectionProperties();
        if (sectPr.GetFirstChild<W.PageSize>() is { } size)
        {
            props = props with
            {
                PageWidth = size.Width?.Value is { } w ? new Twips((int)w) : props.PageWidth,
                PageHeight = size.Height?.Value is { } h ? new Twips((int)h) : props.PageHeight,
                Orientation = size.Orient?.InnerText == "landscape" ? Orientation.Landscape : Orientation.Portrait,
            };
        }

        if (sectPr.GetFirstChild<W.PageMargin>() is { } margin)
        {
            props = props with
            {
                MarginTop = margin.Top?.Value is { } top ? new Twips(top) : props.MarginTop,
                MarginBottom = margin.Bottom?.Value is { } bottom ? new Twips(bottom) : props.MarginBottom,
                MarginLeft = margin.Left?.Value is { } left ? new Twips((int)left) : props.MarginLeft,
                MarginRight = margin.Right?.Value is { } right ? new Twips((int)right) : props.MarginRight,
                HeaderDistance = margin.Header?.Value is { } header ? new Twips((int)header) : props.HeaderDistance,
                FooterDistance = margin.Footer?.Value is { } footer ? new Twips((int)footer) : props.FooterDistance,
                Gutter = margin.Gutter?.Value is { } gutter ? new Twips((int)gutter) : props.Gutter,
            };
        }

        props = props with
        {
            Start = SectionStartFrom(sectPr.GetFirstChild<W.SectionType>()?.Val?.InnerText),
            TitlePage = OnOff(sectPr.GetFirstChild<W.TitlePage>()) ?? false,
        };

        if (sectPr.GetFirstChild<W.PageNumberType>() is { } numbering)
        {
            props = props with
            {
                PageNumberStart = numbering.Start?.Value,
                PageNumberFormat = PageNumberFormatFrom(numbering.Format?.InnerText),
            };
        }

        if (sectPr.GetFirstChild<W.Columns>()?.ColumnCount?.Value is { } columns && columns > 1)
        {
            props = props with { ColumnCount = columns };
            Warn("Multiple columns are laid out as a single column in this version.");
        }

        HeaderFooterSet headers = HeaderFooterSet.Empty;
        foreach (W.HeaderReference reference in sectPr.Elements<W.HeaderReference>())
        {
            if (reference.Id?.Value is { } id && _main!.GetPartById(id) is HeaderPart part)
            {
                headers = headers.With(VariantFrom(reference.Type?.InnerText), ReadStory(part.Header, part));
            }
        }

        HeaderFooterSet footers = HeaderFooterSet.Empty;
        foreach (W.FooterReference reference in sectPr.Elements<W.FooterReference>())
        {
            if (reference.Id?.Value is { } id && _main!.GetPartById(id) is FooterPart part)
            {
                footers = footers.With(VariantFrom(reference.Type?.InnerText), ReadStory(part.Footer, part));
            }
        }

        return new Section(props, body, headers, footers);
    }

    // ------------------------------------------------------------------ paragraphs and runs

    private Paragraph ReadParagraph(W.Paragraph paragraph)
    {
        W.ParagraphProperties? pPr = paragraph.ParagraphProperties;
        var builder = new InlineBuilder(this);
        ReadInlines(paragraph, builder);
        return new Paragraph(
            builder.ToImmutable(),
            pPr?.ParagraphStyleId?.Val?.Value,
            ReadParagraphProperties(pPr),
            ReadRunProperties(pPr?.ParagraphMarkRunProperties));
    }

    private void ReadInlines(OpenXmlElement container, InlineBuilder builder)
    {
        foreach (OpenXmlElement element in container.ChildElements)
        {
            switch (element)
            {
                case W.Run run:
                    ReadRun(run, builder);
                    break;
                case W.Hyperlink hyperlink:
                    ReadHyperlink(hyperlink, builder);
                    break;
                case W.SdtContentRun or W.InsertedRun or W.CustomXmlRun or W.SimpleFieldRuby:
                case OpenXmlElement when element.LocalName == "smartTag":
                    ReadInlines(element, builder);
                    break;
                case W.SdtRun sdt:
                    ReadInlines(sdt.SdtContentRun ?? new W.SdtContentRun(), builder);
                    break;
                case W.SimpleField field:
                    ReadSimpleField(field, builder);
                    break;
                case W.DeletedRun or W.ParagraphProperties or W.BookmarkStart or W.BookmarkEnd or W.ProofError
                    or W.CommentRangeStart or W.CommentRangeEnd or W.PermStart or W.PermEnd or W.MoveFromRangeStart
                    or W.MoveFromRangeEnd or W.MoveToRangeStart or W.MoveToRangeEnd or W.SdtProperties or W.SdtEndCharProperties
                    or W.RunProperties:
                    break;
                case DocumentFormat.OpenXml.Math.OfficeMath or DocumentFormat.OpenXml.Math.Paragraph:
                    Warn("Equations are not supported and were dropped.");
                    break;
                default:
                    Warn($"Unsupported inline content ({element.LocalName}) was dropped.");
                    break;
            }
        }
    }

    /// <summary>Runs inside w:hyperlink get the target as their Link; the relationship holds external URLs, the anchor attribute internal ones.</summary>
    private void ReadHyperlink(W.Hyperlink hyperlink, InlineBuilder builder)
    {
        string? target = null;
        if (hyperlink.Id?.Value is { } id && _part is not null)
        {
            target = _part.HyperlinkRelationships.FirstOrDefault(r => string.Equals(r.Id, id, StringComparison.Ordinal))?.Uri.OriginalString;
        }

        if (target is null && hyperlink.Anchor?.Value is { Length: > 0 } anchor)
        {
            target = "#" + anchor;
        }

        string? previous = builder.CurrentLink;
        builder.CurrentLink = target ?? previous;
        try
        {
            ReadInlines(hyperlink, builder);
        }
        finally
        {
            builder.CurrentLink = previous;
        }
    }

    private static readonly Regex HyperlinkInstruction = new("^HYPERLINK\\s+(?<local>\\\\l\\s+)?\"(?<target>[^\"]+)\"", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>The target of a HYPERLINK field instruction, or null when the instruction is something else.</summary>
    internal static string? HyperlinkTarget(string instruction)
    {
        Match match = HyperlinkInstruction.Match(instruction.Trim());
        if (!match.Success)
        {
            return null;
        }

        string target = match.Groups["target"].Value;
        return match.Groups["local"].Success ? "#" + target : target;
    }

    private void ReadSimpleField(W.SimpleField field, InlineBuilder builder)
    {
        string instruction = field.Instruction?.Value ?? string.Empty;
        var result = new StringBuilder();
        foreach (W.Text text in field.Descendants<W.Text>())
        {
            result.Append(text.Text);
        }

        W.Run? firstRun = field.Descendants<W.Run>().FirstOrDefault();
        if (HyperlinkTarget(instruction) is { } link)
        {
            builder.AddText(result.ToString(), ReadRunProperties(firstRun?.RunProperties) with { Link = link }, firstRun?.RunProperties?.RunStyle?.Val?.Value);
            return;
        }

        builder.AddField(new Field(FieldKindFrom(instruction), instruction.Trim(), result.ToString(), ReadRunProperties(firstRun?.RunProperties), firstRun?.RunProperties?.RunStyle?.Val?.Value));
    }

    private void ReadRun(W.Run run, InlineBuilder builder)
    {
        RunProperties properties = ReadRunProperties(run.RunProperties);
        string? styleId = run.RunProperties?.RunStyle?.Val?.Value;
        var text = new StringBuilder();

        void Flush()
        {
            if (text.Length > 0)
            {
                builder.AddText(text.ToString(), properties, styleId);
                text.Clear();
            }
        }

        foreach (OpenXmlElement element in run.ChildElements)
        {
            switch (element)
            {
                case W.Text t:
                    text.Append(t.Text);
                    break;
                case W.TabChar or W.PositionalTab:
                    text.Append('\t');
                    break;
                case W.Break br:
                    Flush();
                    BreakKind kind = br.Type?.InnerText switch
                    {
                        "page" => BreakKind.Page,
                        "column" => BreakKind.Column,
                        _ => BreakKind.Line,
                    };
                    builder.AddInline(new Break(kind, properties, styleId));
                    break;
                case W.CarriageReturn:
                    Flush();
                    builder.AddInline(new Break(BreakKind.Line, properties, styleId));
                    break;
                case W.NoBreakHyphen:
                    text.Append('‑');
                    break;
                case W.SoftHyphen:
                    text.Append('­');
                    break;
                case W.SymbolChar symbol:
                    if (symbol.Char?.Value is { } hex && int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int code))
                    {
                        text.Append((char)(code & 0xFFFF));
                    }

                    break;
                case W.FieldChar fieldChar:
                    Flush();
                    builder.FieldChar(fieldChar.FieldCharType?.InnerText, properties, styleId);
                    break;
                case W.FieldCode fieldCode:
                    builder.FieldInstruction(fieldCode.Text);
                    break;
                case W.RunProperties or W.LastRenderedPageBreak or W.AnnotationReferenceMark or W.ContinuationSeparatorMark or W.SeparatorMark:
                    break;
                case W.Drawing drawing:
                    Flush();
                    ReadDrawing(drawing, builder, properties, styleId);
                    break;
                case W.Picture or W.EmbeddedObject:
                    Warn("Legacy (VML) pictures and embedded objects are not supported and were dropped.");
                    break;

                case W.FootnoteReference or W.EndnoteReference:
                    Warn("Footnotes and endnotes are not supported and were dropped.");
                    break;
                case W.CommentReference:
                    break;
                default:
                    Warn($"Unsupported run content ({element.LocalName}) was dropped.");
                    break;
            }
        }

        Flush();
    }

    /// <summary>Reads an inline (or, flattened, a floating) picture: the blip's relationship points at an image part.</summary>
    private void ReadDrawing(W.Drawing drawing, InlineBuilder builder, RunProperties properties, string? styleId)
    {
        DW.Extent? extent = drawing.Descendants<DW.Extent>().FirstOrDefault();
        A.Blip? blip = drawing.Descendants<A.Blip>().FirstOrDefault();
        string? relationshipId = blip?.Embed?.Value;
        if (relationshipId is null || extent?.Cx?.Value is not { } cx || extent.Cy?.Value is not { } cy)
        {
            Warn("Drawings that are not pictures (shapes, charts, diagrams) are not supported and were dropped.");
            return;
        }

        if (_part is null || !_part.TryGetPartById(relationshipId, out OpenXmlPart? referenced) || referenced is not ImagePart imagePart)
        {
            Warn("A picture's data was missing from the file and the picture was dropped.");
            return;
        }

        string key = imagePart.Uri.ToString();
        if (!_imageIdsByUri.TryGetValue(key, out string? imageId))
        {
            using Stream stream = imagePart.GetStream(FileMode.Open, FileAccess.Read);
            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            imageId = ImageStore.MakeId(_images.Count + 1);
            _images[imageId] = new ImageData(memory.ToArray(), imagePart.ContentType);
            _imageIdsByUri[key] = imageId;
        }

        if (drawing.GetFirstChild<DW.Anchor>() is not null)
        {
            Warn("Floating pictures are placed in the text in this version.");
        }

        builder.AddInline(new InlineImage(imageId, Twips.FromEmu(cx), Twips.FromEmu(cy), properties, styleId));
    }

    // ------------------------------------------------------------------ properties

    private RunProperties ReadRunProperties(OpenXmlElement? rPr)
    {
        if (rPr is null)
        {
            return RunProperties.Empty;
        }

        return new RunProperties
        {
            FontFamily = ResolveFont(rPr.GetFirstChild<W.RunFonts>()),
            FontSize = HalfPointsFrom(rPr.GetFirstChild<W.FontSize>()?.Val?.Value),
            Bold = OnOff(rPr.GetFirstChild<W.Bold>()),
            Italic = OnOff(rPr.GetFirstChild<W.Italic>()),
            Underline = UnderlineFrom(rPr.GetFirstChild<W.Underline>()),
            Strikethrough = OnOff(rPr.GetFirstChild<W.Strike>()),
            DoubleStrikethrough = OnOff(rPr.GetFirstChild<W.DoubleStrike>()),
            Color = ReadColor(rPr.GetFirstChild<W.Color>()),
            Highlight = HighlightFrom(rPr.GetFirstChild<W.Highlight>()),
            VerticalAlignment = VerticalFrom(rPr.GetFirstChild<W.VerticalTextAlignment>()),
            SmallCaps = OnOff(rPr.GetFirstChild<W.SmallCaps>()),
            AllCaps = OnOff(rPr.GetFirstChild<W.Caps>()),
            Hidden = OnOff(rPr.GetFirstChild<W.Vanish>()),
            Language = rPr.GetFirstChild<W.Languages>()?.Val?.Value,
        };
    }

    private string? ResolveFont(W.RunFonts? fonts)
    {
        if (fonts is null)
        {
            return null;
        }

        string? theme = fonts.AsciiTheme?.InnerText ?? fonts.HighAnsiTheme?.InnerText;
        if (theme is not null)
        {
            return _theme.Font(theme);
        }

        return fonts.Ascii?.Value ?? fonts.HighAnsi?.Value;
    }

    private DocColor? ReadColor(W.Color? color)
    {
        if (color is null)
        {
            return null;
        }

        string? value = color.Val?.Value;
        if (value is not null && !value.Equals("auto", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                return DocColor.Parse(value);
            }
            catch (FormatException)
            {
                // fall through to theme / auto
            }
        }

        if (color.ThemeColor?.InnerText is { } themeColor && _theme.Color(themeColor) is { } resolved)
        {
            return resolved;
        }

        return DocColor.Auto;
    }

    private static ParagraphProperties ReadParagraphProperties(OpenXmlElement? pPr)
    {
        if (pPr is null)
        {
            return ParagraphProperties.Empty;
        }

        var props = new ParagraphProperties
        {
            Alignment = AlignmentFrom(pPr.GetFirstChild<W.Justification>()),
            KeepWithNext = OnOff(pPr.GetFirstChild<W.KeepNext>()),
            KeepLinesTogether = OnOff(pPr.GetFirstChild<W.KeepLines>()),
            PageBreakBefore = OnOff(pPr.GetFirstChild<W.PageBreakBefore>()),
            WidowControl = OnOff(pPr.GetFirstChild<W.WidowControl>()),
            ContextualSpacing = OnOff(pPr.GetFirstChild<W.ContextualSpacing>()),
            OutlineLevel = pPr.GetFirstChild<W.OutlineLevel>()?.Val?.Value,
            List = ReadListFormat(pPr.GetFirstChild<W.NumberingProperties>()),
        };

        if (pPr.GetFirstChild<W.Indentation>() is { } ind)
        {
            Twips? firstLine = TwipsFrom(ind.FirstLine?.Value);
            Twips? hanging = TwipsFrom(ind.Hanging?.Value);
            props = props with
            {
                LeftIndent = TwipsFrom(ind.Left?.Value ?? ind.Start?.Value),
                RightIndent = TwipsFrom(ind.Right?.Value ?? ind.End?.Value),
                FirstLineIndent = hanging is { } h ? -h : firstLine,
            };
        }

        if (pPr.GetFirstChild<W.SpacingBetweenLines>() is { } spacing)
        {
            Twips auto = Twips.FromPoints(14);
            props = props with
            {
                SpaceBefore = spacing.BeforeAutoSpacing?.Value == true ? auto : TwipsFrom(spacing.Before?.Value),
                SpaceAfter = spacing.AfterAutoSpacing?.Value == true ? auto : TwipsFrom(spacing.After?.Value),
            };
            if (int.TryParse(spacing.Line?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int line))
            {
                LineSpacingRule rule = spacing.LineRule?.InnerText switch
                {
                    "exact" => LineSpacingRule.Exact,
                    "atLeast" => LineSpacingRule.AtLeast,
                    _ => LineSpacingRule.Auto,
                };
                props = props with { LineSpacing = new LineSpacing(rule, line) };
            }
        }

        if (pPr.GetFirstChild<W.Tabs>() is { } tabs)
        {
            var stops = new List<TabStop>();
            foreach (W.TabStop tab in tabs.Elements<W.TabStop>())
            {
                if (tab.Position?.Value is not { } position)
                {
                    continue;
                }

                string? val = tab.Val?.InnerText;
                stops.Add(new TabStop(new Twips(position), TabAlignmentFrom(val), TabLeaderFrom(tab.Leader?.InnerText), IsCleared: val == "clear"));
            }

            props = props with { Tabs = TabStops.Create(stops) };
        }

        return props;
    }

    private static ListFormat? ReadListFormat(W.NumberingProperties? numPr)
    {
        if (numPr is null)
        {
            return null;
        }

        int numberingId = numPr.NumberingId?.Val?.Value ?? 0;
        int level = numPr.NumberingLevelReference?.Val?.Value ?? 0;
        return numberingId <= 0 ? ListFormat.None : new ListFormat(numberingId, Math.Clamp(level, 0, ListDefinition.LevelCount - 1));
    }

    // ------------------------------------------------------------------ numbering

    private ListStore ReadNumbering(NumberingDefinitionsPart? part)
    {
        W.Numbering? numbering = part?.Numbering;
        if (numbering is null)
        {
            return ListStore.Empty;
        }

        var definitions = ImmutableDictionary.CreateBuilder<int, ListDefinition>();
        foreach (W.AbstractNum abstractNum in numbering.Elements<W.AbstractNum>())
        {
            if (abstractNum.AbstractNumberId?.Value is not { } id)
            {
                continue;
            }

            var levels = new ListLevel[ListDefinition.LevelCount];
            for (int i = 0; i < levels.Length; i++)
            {
                levels[i] = new ListLevel { LeftIndent = Twips.FromInches(0.5 * (i + 1)), Hanging = Twips.FromInches(0.25) };
            }

            foreach (W.Level level in abstractNum.Elements<W.Level>())
            {
                int index = level.LevelIndex?.Value ?? 0;
                if (index < 0 || index >= levels.Length)
                {
                    continue;
                }

                levels[index] = ReadLevel(level, levels[index]);
            }

            definitions[id] = new ListDefinition(id, [.. levels]);
        }

        var instances = ImmutableDictionary.CreateBuilder<int, ListInstance>();
        foreach (W.NumberingInstance num in numbering.Elements<W.NumberingInstance>())
        {
            if (num.NumberID?.Value is not { } id || num.AbstractNumId?.Val?.Value is not { } definitionId)
            {
                continue;
            }

            ImmutableDictionary<int, int>? overrides = null;
            foreach (W.LevelOverride levelOverride in num.Elements<W.LevelOverride>())
            {
                if (levelOverride.LevelIndex?.Value is { } index && levelOverride.StartOverrideNumberingValue?.Val?.Value is { } start)
                {
                    overrides = (overrides ?? ImmutableDictionary<int, int>.Empty).SetItem(index, start);
                }
            }

            instances[id] = new ListInstance(id, definitionId, overrides);
        }

        return new ListStore(definitions.ToImmutable(), instances.ToImmutable());
    }

    private ListLevel ReadLevel(W.Level level, ListLevel defaults)
    {
        NumberFormat format = level.NumberingFormat?.Val?.InnerText switch
        {
            "bullet" => NumberFormat.Bullet,
            "lowerLetter" => NumberFormat.LowerLetter,
            "upperLetter" => NumberFormat.UpperLetter,
            "lowerRoman" => NumberFormat.LowerRoman,
            "upperRoman" => NumberFormat.UpperRoman,
            "none" => NumberFormat.None,
            null => defaults.Format,
            _ => NumberFormat.Decimal,
        };
        string text = level.LevelText?.Val?.Value ?? (format == NumberFormat.Bullet ? "\u2022" : defaults.Text);
        string? font = level.NumberingSymbolRunProperties?.GetFirstChild<W.RunFonts>() is { } fonts ? fonts.Ascii?.Value ?? fonts.HighAnsi?.Value : null;
        if (format == NumberFormat.Bullet)
        {
            (text, font) = MapSymbolBullet(text, font);
        }

        Twips leftIndent = defaults.LeftIndent;
        Twips hanging = defaults.Hanging;
        if (level.PreviousParagraphProperties?.GetFirstChild<W.Indentation>() is { } ind)
        {
            leftIndent = TwipsFrom(ind.Left?.Value ?? ind.Start?.Value) ?? leftIndent;
            hanging = TwipsFrom(ind.Hanging?.Value) ?? (TwipsFrom(ind.FirstLine?.Value) is { } firstLine ? -firstLine : hanging);
        }

        return new ListLevel
        {
            Format = format,
            Text = text,
            Start = level.StartNumberingValue?.Val?.Value ?? 1,
            LeftIndent = leftIndent,
            Hanging = hanging,
            MarkerFont = font,
            Alignment = level.LevelJustification?.Val?.InnerText switch
            {
                "center" => Alignment.Center,
                "right" => Alignment.Right,
                _ => Alignment.Left,
            },
        };
    }

    /// <summary>Word stores its default bullets as private-use characters in Symbol/Wingdings; map the common ones to Unicode.</summary>
    private static (string Text, string? Font) MapSymbolBullet(string text, string? font)
    {
        if (text.Length == 1 && text[0] >= '\uF000' && text[0] <= '\uF0FF')
        {
            string mapped = (text[0] & 0xFF) switch
            {
                0xB7 => "\u2022", // Symbol bullet
                0xA7 => "\u25AA", // Wingdings small square
                0xA8 => "\u25AB",
                0xD8 => "\u27A2", // Wingdings arrow
                0xFC => "\u2713", // Wingdings check
                0x76 => "\u2756", // Wingdings diamond
                0x6E => "\u25A0",
                0x6C => "\u25CF",
                0xB2 => "\u2751",
                0xA1 => "\u25CB",
                _ => "\u2022",
            };
            return (mapped, null);
        }

        if (string.Equals(font, "Symbol", StringComparison.OrdinalIgnoreCase) || (font is not null && font.StartsWith("Wingdings", StringComparison.OrdinalIgnoreCase)))
        {
            return (text, null);
        }

        return (text, font);
    }

    // ------------------------------------------------------------------ styles and settings

    private StyleSheet ReadStyles(StyleDefinitionsPart? part)
    {
        W.Styles? styles = part?.Styles;
        if (styles is null)
        {
            return StyleSheet.Empty;
        }

        var defaults = new DocumentDefaults
        {
            Run = ReadRunProperties(styles.DocDefaults?.RunPropertiesDefault?.RunPropertiesBaseStyle),
            Paragraph = ReadParagraphProperties(styles.DocDefaults?.ParagraphPropertiesDefault?.ParagraphPropertiesBaseStyle),
        };

        var map = ImmutableDictionary.CreateBuilder<string, Style>(StringComparer.Ordinal);
        string? defaultParagraph = null;
        string? defaultCharacter = null;
        foreach (W.Style style in styles.Elements<W.Style>())
        {
            string? id = style.StyleId?.Value;
            if (string.IsNullOrEmpty(id))
            {
                continue;
            }

            StyleType type = StyleTypeFrom(style.Type?.InnerText);
            bool isDefault = style.Default?.Value == true;
            map[id] = new Style
            {
                Id = id,
                Name = style.StyleName?.Val?.Value ?? id,
                Type = type,
                BasedOn = style.BasedOn?.Val?.Value,
                Next = style.NextParagraphStyle?.Val?.Value,
                IsDefault = isDefault,
                QuickFormat = style.PrimaryStyle is not null,
                Priority = style.UIPriority?.Val?.Value ?? 99,
                ParagraphProperties = ReadParagraphProperties(style.StyleParagraphProperties),
                RunProperties = ReadRunProperties(style.StyleRunProperties),
            };

            if (isDefault && type == StyleType.Paragraph)
            {
                defaultParagraph ??= id;
            }
            else if (isDefault && type == StyleType.Character)
            {
                defaultCharacter ??= id;
            }
        }

        return new StyleSheet(
            map.ToImmutable(),
            defaults,
            defaultParagraph ?? StyleSheet.NormalStyleId,
            defaultCharacter ?? StyleSheet.DefaultParagraphFontStyleId);
    }

    private static DocumentSettings ReadSettings(DocumentSettingsPart? part)
    {
        W.Settings? settings = part?.Settings;
        if (settings is null)
        {
            return DocumentSettings.Default;
        }

        var result = DocumentSettings.Default;
        if (settings.GetFirstChild<W.DefaultTabStop>()?.Val?.Value is { } tab && tab > 0)
        {
            result = result with { DefaultTabStop = new Twips(tab) };
        }

        if (OnOff(settings.GetFirstChild<W.EvenAndOddHeaders>()) == true)
        {
            result = result with { EvenAndOddHeaders = true };
        }

        return result;
    }

    private void Warn(string message)
    {
        if (_warned.Add(message))
        {
            _warnings.Add(new LoadWarning(message));
        }
    }

    /// <summary>Accumulates inlines while tracking complex-field state (begin / instruction / separate / result / end).</summary>
    private sealed class InlineBuilder(DocxReader reader)
    {
        private readonly ImmutableArray<Inline>.Builder _inlines = ImmutableArray.CreateBuilder<Inline>();
        private readonly StringBuilder _instruction = new();
        private readonly StringBuilder _result = new();
        private int _depth;
        private bool _inResult;
        private RunProperties _fieldProperties = RunProperties.Empty;
        private string? _fieldStyle;

        /// <summary>Target of the w:hyperlink being read, applied to every run inside it.</summary>
        public string? CurrentLink { get; set; }

        public void AddText(string text, RunProperties properties, string? styleId)
        {
            if (_depth > 1)
            {
                return; // nested field: ignore entirely
            }

            if (_depth == 1)
            {
                if (_inResult)
                {
                    _result.Append(text);
                }

                return;
            }

            _inlines.Add(new Run(text, CurrentLink is null ? properties : properties with { Link = CurrentLink }, styleId));
        }

        public void AddInline(Inline inline)
        {
            if (_depth == 0)
            {
                _inlines.Add(inline);
            }
        }

        public void AddField(Field field)
        {
            if (_depth == 0)
            {
                _inlines.Add(field);
            }
        }

        public void FieldChar(string? type, RunProperties properties, string? styleId)
        {
            switch (type)
            {
                case "begin":
                    _depth++;
                    if (_depth == 1)
                    {
                        _instruction.Clear();
                        _result.Clear();
                        _inResult = false;
                        _fieldProperties = properties;
                        _fieldStyle = styleId;
                    }

                    break;
                case "separate":
                    if (_depth == 1)
                    {
                        _inResult = true;
                    }

                    break;
                case "end":
                    if (_depth == 1)
                    {
                        string instruction = _instruction.ToString().Trim();
                        if (HyperlinkTarget(instruction) is { } link)
                        {
                            _inlines.Add(new Run(_result.ToString(), _fieldProperties with { Link = link }, _fieldStyle));
                        }
                        else
                        {
                            _inlines.Add(new Field(FieldKindFrom(instruction), instruction, _result.ToString(), _fieldProperties, _fieldStyle));
                        }

                    }

                    _depth = Math.Max(0, _depth - 1);
                    break;
            }
        }

        public void FieldInstruction(string text)
        {
            if (_depth == 1 && !_inResult)
            {
                _instruction.Append(text);
            }
        }

        public ImmutableArray<Inline> ToImmutable()
        {
            if (_depth > 0)
            {
                reader.Warn("A field was not terminated and was dropped.");
            }

            // Merge adjacent runs with equal formatting (Word splits runs for revision tracking).
            var merged = ImmutableArray.CreateBuilder<Inline>(_inlines.Count);
            foreach (Inline inline in _inlines)
            {
                if (inline is Run { Text.Length: 0 })
                {
                    continue;
                }

                if (merged.Count > 0 && merged[^1] is Run previous && inline is Run current && previous.HasSameFormatting(current))
                {
                    merged[^1] = previous.WithText(previous.Text + current.Text);
                }
                else
                {
                    merged.Add(inline);
                }
            }

            return merged.ToImmutable();
        }
    }
}
