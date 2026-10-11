using System.Globalization;
using System.Text.RegularExpressions;

namespace Quill.Core.Model;

/// <summary>DATE, TIME and document property fields: instructions, format pictures and their current text.</summary>
public static partial class FieldSupport
{
    /// <summary>The instruction for a DATE or TIME field with a Word date picture ("d MMMM yyyy", "h:mm am/pm").</summary>
    public static string DateInstruction(string picture, bool time) =>
        (time ? "TIME" : "DATE") + (string.IsNullOrWhiteSpace(picture) ? string.Empty : " \\@ \"" + picture.Trim() + "\"");

    public static string DocumentPropertyInstruction(string property) => property.Trim().ToUpperInvariant();

    /// <summary>Word's picture to a .NET custom format: the tokens agree except am/pm, which becomes tt.</summary>
    public static string ToDotNetFormat(string picture) =>
        AmPm().Replace(picture, "tt");

    /// <summary>A .NET format (custom, or a standard letter such as "D") to a Word picture.</summary>
    public static string ToWordPicture(string format, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);
        DateTimeFormatInfo f = culture.DateTimeFormat;
        string expanded = format switch
        {
            "d" => f.ShortDatePattern,
            "D" => f.LongDatePattern,
            "t" => f.ShortTimePattern,
            "T" => f.LongTimePattern,
            "f" => f.LongDatePattern + " " + f.ShortTimePattern,
            "F" => f.LongDatePattern + " " + f.LongTimePattern,
            "g" => f.ShortDatePattern + " " + f.ShortTimePattern,
            "G" => f.ShortDatePattern + " " + f.LongTimePattern,
            _ => format,
        };
        return expanded.Replace("tt", "am/pm", StringComparison.Ordinal);
    }

    /// <summary>The picture inside \@ "..." of a DATE/TIME instruction, or null.</summary>
    public static string? Picture(Field field)
    {
        ArgumentNullException.ThrowIfNull(field);
        Match match = PicturePattern().Match(field.Instruction);
        return match.Success ? match.Groups["picture"].Value : null;
    }

    /// <summary>What a DATE or TIME field shows at <paramref name="now"/>.</summary>
    public static string FormatDate(Field field, DateTime now, CultureInfo? culture = null)
    {
        ArgumentNullException.ThrowIfNull(field);
        culture ??= CultureInfo.CurrentCulture;
        string format = Picture(field) is { } picture ? ToDotNetFormat(picture) : field.Kind == FieldKind.Time ? "t" : "d";
        try
        {
            return now.ToString(format, culture);
        }
        catch (FormatException)
        {
            return now.ToString(field.Kind == FieldKind.Time ? "t" : "d", culture);
        }
    }

    /// <summary>What a document property field shows, or null when the property is empty.</summary>
    public static string? DocumentPropertyText(Field field, DocumentMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(field);
        ArgumentNullException.ThrowIfNull(metadata);
        string name = field.Instruction.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.ToUpperInvariant() ?? string.Empty;
        return name switch
        {
            "AUTHOR" => metadata.Author,
            "TITLE" => metadata.Title,
            "SUBJECT" => metadata.Subject,
            "COMMENTS" => metadata.Description,
            _ => null,
        };
    }

    /// <summary>The current text of any self-updating field; the stored result for the others.</summary>
    public static string Resolve(Field field, DateTime now, DocumentMetadata metadata) => field.Kind switch
    {
        FieldKind.Date or FieldKind.Time => FormatDate(field, now),
        FieldKind.DocProperty => DocumentPropertyText(field, metadata) ?? field.CachedResult,
        _ => field.CachedResult,
    };

    [GeneratedRegex("am/pm", RegexOptions.IgnoreCase)]
    private static partial Regex AmPm();

    [GeneratedRegex("\\\\@\\s*\"(?<picture>[^\"]*)\"")]
    private static partial Regex PicturePattern();
}
