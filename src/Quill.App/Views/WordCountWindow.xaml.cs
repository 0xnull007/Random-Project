using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using Quill.Core.Model;

namespace Quill.App.Views;

/// <summary>Word's statistics dialog: pages, words, characters, paragraphs and lines, for the document or the selection.</summary>
public partial class WordCountWindow : Window
{
    public WordCountWindow(DocumentStatistics statistics, int pages, int lines, bool forSelection)
    {
        InitializeComponent();
        Scope.Text = forSelection ? "Selection" : "Document";
        AddRow("Pages", pages);
        AddRow("Words", statistics.Words);
        AddRow("Characters (no spaces)", statistics.Characters);
        AddRow("Characters (with spaces)", statistics.CharactersWithSpaces);
        AddRow("Paragraphs", statistics.Paragraphs);
        AddRow("Lines", lines);
    }

    private void AddRow(string label, int value)
    {
        int row = Table.RowDefinitions.Count;
        Table.RowDefinitions.Add(new RowDefinition());
        var name = new TextBlock { Text = label, Margin = new Thickness(0, 3, 24, 3) };
        var number = new TextBlock { Text = value.ToString("N0", CultureInfo.CurrentCulture), Margin = new Thickness(0, 3, 0, 3), HorizontalAlignment = HorizontalAlignment.Right };
        Grid.SetRow(name, row);
        Grid.SetColumn(name, 0);
        Grid.SetRow(number, row);
        Grid.SetColumn(number, 1);
        Table.Children.Add(name);
        Table.Children.Add(number);
    }
}
