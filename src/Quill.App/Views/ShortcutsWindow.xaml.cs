using System.Windows;

namespace Quill.App.Views;

/// <summary>F1: every keyboard shortcut the editor understands.</summary>
public partial class ShortcutsWindow : Window
{
    public static readonly IReadOnlyList<Shortcut> Shortcuts =
    [
        new("Ctrl+N / Ctrl+O / Ctrl+S / F12", "New, Open, Save, Save As"),
        new("Ctrl+P", "Print"),
        new("Ctrl+Z / Ctrl+Y", "Undo / Redo"),
        new("Ctrl+X / Ctrl+C / Ctrl+V", "Cut / Copy / Paste (formatted)"),
        new("Ctrl+Alt+V", "Paste as plain text"),
        new("Ctrl+Shift+C / Ctrl+Shift+V", "Copy / paste formatting (Format Painter)"),
        new("Ctrl+Space", "Clear character formatting"),
        new("Ctrl+K", "Insert or edit a hyperlink"),
        new("Ctrl+D", "Font dialog"),
        new("Right-click / Menu key", "Context menu (Font, Paragraph, lists, links, pictures)"),
        new("Ctrl+Click", "Follow a hyperlink"),
        new("Ctrl+A", "Select all"),
        new("Ctrl+F / Ctrl+H", "Find / Find and replace"),
        new("F3 / Shift+F4", "Find next / Find previous"),
        new("Ctrl+G", "Go to page"),
        new("Ctrl+Shift+G", "Word count"),
        new("F1", "This list"),
        new("Ctrl+B / Ctrl+I / Ctrl+U", "Bold / Italic / Underline"),
        new("Ctrl+Shift+Plus", "Superscript"),
        new("Ctrl+Shift+> / Ctrl+Shift+<", "Grow / shrink font"),
        new("Ctrl+] / Ctrl+[", "Font size up / down by 1 pt"),
        new("Shift+F3", "Change case (lowercase, UPPERCASE, Capitalize Each Word)"),
        new("Ctrl+L / Ctrl+E / Ctrl+R / Ctrl+J", "Align left / center / right / justify"),
        new("Ctrl+1 / Ctrl+2 / Ctrl+5", "Single / double / 1.5 line spacing"),
        new("Ctrl+Alt+1 / 2 / 3", "Heading 1 / 2 / 3"),
        new("Ctrl+Shift+N", "Normal style"),
        new("Tab / Shift+Tab (start of list item)", "Increase / decrease list level"),
        new("Enter (empty list item)", "End the list"),
        new("Enter / Shift+Enter / Ctrl+Enter", "New paragraph / line break / page break"),
        new("Ctrl+Backspace / Ctrl+Delete", "Delete word left / right"),
        new("Ctrl+Left / Ctrl+Right", "Move by word"),
        new("Home / End", "Start / end of line"),
        new("Ctrl+Home / Ctrl+End", "Start / end of document"),
        new("Page Up / Page Down", "Scroll a screen"),
        new("Shift + any movement", "Extend the selection"),
        new("Double-click / Triple-click", "Select word / paragraph"),
        new("Double-click top or bottom margin", "Edit header / footer"),
        new("Esc (in a header or footer)", "Back to the document"),
        new("Ctrl+Plus / Ctrl+Minus / Ctrl+Wheel", "Zoom in / out"),
    ];

    public ShortcutsWindow()
    {
        InitializeComponent();
        List.ItemsSource = Shortcuts;
    }
}

public sealed record Shortcut(string Keys, string Action);
