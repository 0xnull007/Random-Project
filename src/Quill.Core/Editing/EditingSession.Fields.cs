using System.Globalization;
using Quill.Core.Model;
using Quill.Core.Text;

namespace Quill.Core.Editing;

/// <summary>Self-updating fields: DATE/TIME and document properties (Insert > Text > Quick Parts).</summary>
public sealed partial class EditingSession
{
    /// <summary>Inserts a DATE (or TIME) field with a Word picture such as "d MMMM yyyy"; it shows the current value on every layout.</summary>
    public void InsertDateField(string picture, bool time)
    {
        ArgumentNullException.ThrowIfNull(picture);
        var field = new Field(time ? FieldKind.Time : FieldKind.Date, FieldSupport.DateInstruction(picture, time), string.Empty);
        field = field.WithCachedResult(FieldSupport.FormatDate(field, DateTime.Now, CultureInfo.CurrentCulture));
        InsertSelfUpdatingField(field);
    }

    /// <summary>Inserts an AUTHOR, TITLE, SUBJECT or COMMENTS field showing the document property (empty until it is set).</summary>
    public void InsertDocumentPropertyField(string property)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(property);
        var field = new Field(FieldKind.DocProperty, FieldSupport.DocumentPropertyInstruction(property), string.Empty);
        field = field.WithCachedResult(FieldSupport.DocumentPropertyText(field, Document.Metadata) ?? string.Empty);
        InsertSelfUpdatingField(field);
    }

    private void InsertSelfUpdatingField(Field field)
    {
        (Document working, TextPosition at, ChangeSet? deletion) = DeleteSelectionIfAny();
        EditResult inserted = DocumentEditor.InsertInline(working, at, field);
        Commit(WithDeletion(inserted, deletion), EditKind.Other, startsNewGroup: true);
        PendingFormat = null;
    }
}
