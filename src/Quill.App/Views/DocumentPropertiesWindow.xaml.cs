using System.Globalization;
using System.Windows;
using Quill.Core.Model;

namespace Quill.App.Views;

/// <summary>File > Properties: the core properties stored in the .docx and used for PDF metadata.</summary>
public partial class DocumentPropertiesWindow : Window
{
    private readonly DocumentMetadata _original;

    public DocumentPropertiesWindow(DocumentMetadata metadata, string? path, string documentName, DocumentStatistics statistics, int pages)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        InitializeComponent();
        _original = metadata;
        FileText.Text = path ?? documentName + " (not saved yet)";
        TitleBox.Text = metadata.Title ?? string.Empty;
        AuthorBox.Text = metadata.Author ?? string.Empty;
        SubjectBox.Text = metadata.Subject ?? string.Empty;
        CommentsBox.Text = metadata.Description ?? string.Empty;
        CreatedText.Text = Format(metadata.Created);
        ModifiedText.Text = Format(metadata.Modified);
        StatisticsText.Text = string.Format(
            CultureInfo.CurrentCulture,
            "{0:N0} pages, {1:N0} words, {2:N0} characters ({3:N0} with spaces), {4:N0} paragraphs",
            pages,
            statistics.Words,
            statistics.Characters,
            statistics.CharactersWithSpaces,
            statistics.Paragraphs);
        Loaded += (_, _) =>
        {
            TitleBox.Focus();
            TitleBox.SelectAll();
        };
    }

    /// <summary>The edited properties, set when the dialog returns true.</summary>
    public DocumentMetadata Result { get; private set; } = DocumentMetadata.Empty;

    private static string Format(DateTimeOffset? time) =>
        time is { } t ? t.ToLocalTime().ToString("f", CultureInfo.CurrentCulture) : "unknown";

    private static string? Clean(string text)
    {
        string trimmed = text.Trim();
        return trimmed.Length == 0 ? null : trimmed;
    }

    private void OnOk(object sender, RoutedEventArgs e)
    {
        Result = _original with
        {
            Title = Clean(TitleBox.Text),
            Author = Clean(AuthorBox.Text),
            Subject = Clean(SubjectBox.Text),
            Description = Clean(CommentsBox.Text),
        };
        DialogResult = true;
    }
}
