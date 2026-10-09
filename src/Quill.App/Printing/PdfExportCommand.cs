using System.IO;
using System.Windows;
using Microsoft.Win32;
using Quill.Core.Model;
using Quill.Core.Styles;

namespace Quill.App.Printing;

/// <summary>Asks where to save and exports; shared by the main window and the print preview.</summary>
public static class PdfExportCommand
{
    /// <summary>Returns the written path, or null when cancelled or failed (the failure is shown to the user).</summary>
    public static string? Run(Document document, StyleResolver resolver, string suggestedName, Window? owner)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(resolver);
        var dialog = new SaveFileDialog
        {
            Filter = "PDF documents (*.pdf)|*.pdf",
            DefaultExt = ".pdf",
            FileName = suggestedName + ".pdf",
            AddExtension = true,
            Title = "Export PDF",
        };
        bool? ok = owner is null ? dialog.ShowDialog() : dialog.ShowDialog(owner);
        if (ok != true)
        {
            return null;
        }

        try
        {
            PdfExporter.Export(document, resolver, dialog.FileName);
            return dialog.FileName;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or PdfSharp.PdfSharpException)
        {
            MessageBox.Show("Could not export the PDF." + Environment.NewLine + Environment.NewLine + ex.Message, "Quill", MessageBoxButton.OK, MessageBoxImage.Error);
            return null;
        }
    }
}
