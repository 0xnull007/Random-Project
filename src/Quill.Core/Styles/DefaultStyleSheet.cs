using System.Collections.Immutable;
using Quill.Core.Model;
using Quill.Core.Units;

namespace Quill.Core.Styles;

/// <summary>The style sheet of a new document, modeled on Word's Normal template (Calibri variant).</summary>
public static class DefaultStyleSheet
{
    public const string Heading1Id = "Heading1";
    public const string Heading2Id = "Heading2";
    public const string Heading3Id = "Heading3";
    public const string TitleId = "Title";
    public const string HeaderId = "Header";
    public const string FooterId = "Footer";

    private const string BodyFont = "Calibri";
    private const string HeadingFont = "Calibri Light";

    public static StyleSheet Create()
    {
        var defaults = new DocumentDefaults
        {
            Run = new RunProperties { FontFamily = BodyFont, FontSize = HalfPoints.FromPoints(11), Language = "en-US" },
            Paragraph = new ParagraphProperties { SpaceAfter = Twips.FromPoints(8), LineSpacing = LineSpacing.Multiple(1.08) },
        };

        var headerFooterTabs = TabStops.Create(
            new TabStop(Twips.FromInches(3.25), TabAlignment.Center),
            new TabStop(Twips.FromInches(6.5), TabAlignment.Right));

        Style[] styles =
        [
            new()
            {
                Id = StyleSheet.NormalStyleId,
                Name = "Normal",
                IsDefault = true,
                QuickFormat = true,
                Priority = 0,
            },
            new()
            {
                Id = StyleSheet.DefaultParagraphFontStyleId,
                Name = "Default Paragraph Font",
                Type = StyleType.Character,
                IsDefault = true,
                Priority = 1,
            },
            new()
            {
                Id = Heading1Id,
                Name = "heading 1",
                BasedOn = StyleSheet.NormalStyleId,
                Next = StyleSheet.NormalStyleId,
                QuickFormat = true,
                Priority = 9,
                ParagraphProperties = new ParagraphProperties
                {
                    KeepWithNext = true,
                    KeepLinesTogether = true,
                    SpaceBefore = Twips.FromPoints(12),
                    SpaceAfter = Twips.Zero,
                    OutlineLevel = 0,
                },
                RunProperties = new RunProperties { FontFamily = HeadingFont, FontSize = HalfPoints.FromPoints(16), Color = DocColor.Parse("2F5496") },
            },
            new()
            {
                Id = Heading2Id,
                Name = "heading 2",
                BasedOn = StyleSheet.NormalStyleId,
                Next = StyleSheet.NormalStyleId,
                QuickFormat = true,
                Priority = 9,
                ParagraphProperties = new ParagraphProperties
                {
                    KeepWithNext = true,
                    KeepLinesTogether = true,
                    SpaceBefore = Twips.FromPoints(2),
                    SpaceAfter = Twips.Zero,
                    OutlineLevel = 1,
                },
                RunProperties = new RunProperties { FontFamily = HeadingFont, FontSize = HalfPoints.FromPoints(13), Color = DocColor.Parse("2F5496") },
            },
            new()
            {
                Id = Heading3Id,
                Name = "heading 3",
                BasedOn = StyleSheet.NormalStyleId,
                Next = StyleSheet.NormalStyleId,
                QuickFormat = true,
                Priority = 9,
                ParagraphProperties = new ParagraphProperties
                {
                    KeepWithNext = true,
                    KeepLinesTogether = true,
                    SpaceBefore = Twips.FromPoints(2),
                    SpaceAfter = Twips.Zero,
                    OutlineLevel = 2,
                },
                RunProperties = new RunProperties { FontFamily = HeadingFont, FontSize = HalfPoints.FromPoints(12), Color = DocColor.Parse("1F3763") },
            },
            new()
            {
                Id = TitleId,
                Name = "Title",
                BasedOn = StyleSheet.NormalStyleId,
                Next = StyleSheet.NormalStyleId,
                QuickFormat = true,
                Priority = 10,
                ParagraphProperties = new ParagraphProperties
                {
                    SpaceAfter = Twips.Zero,
                    LineSpacing = LineSpacing.Single,
                    ContextualSpacing = true,
                },
                RunProperties = new RunProperties { FontFamily = HeadingFont, FontSize = HalfPoints.FromPoints(28) },
            },
            new()
            {
                Id = HeaderId,
                Name = "header",
                BasedOn = StyleSheet.NormalStyleId,
                Priority = 99,
                ParagraphProperties = new ParagraphProperties
                {
                    Tabs = headerFooterTabs,
                    SpaceAfter = Twips.Zero,
                    LineSpacing = LineSpacing.Single,
                },
            },
            new()
            {
                Id = FooterId,
                Name = "footer",
                BasedOn = StyleSheet.NormalStyleId,
                Priority = 99,
                ParagraphProperties = new ParagraphProperties
                {
                    Tabs = headerFooterTabs,
                    SpaceAfter = Twips.Zero,
                    LineSpacing = LineSpacing.Single,
                },
            },
        ];

        return new StyleSheet(
            styles.ToImmutableDictionary(s => s.Id, StringComparer.Ordinal),
            defaults,
            StyleSheet.NormalStyleId,
            StyleSheet.DefaultParagraphFontStyleId);
    }
}
