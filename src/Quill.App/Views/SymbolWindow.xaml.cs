using System.Globalization;
using System.Windows;
using System.Windows.Controls;

namespace Quill.App.Views;

/// <summary>A palette of common symbols plus a code-point box; each click inserts into the document and the window stays open.</summary>
public partial class SymbolWindow : Window
{
    private const string Symbols =
        "©®™°±×÷¬µ§¶•…–—‘’“”‚„«»¿¡¢£€¥¤½¼¾⅓⅔⅛←→↑↓↔⇐⇒⇔✓✗★☆♥♦♣♠☺☹☐☑☒αβγδεζηθλμπρστφχψωΔΘΛΞΠΣΦΨΩ∞≈≠≡≤≥√∑∏∂∆∫∈∉∩∪⊂⊃∀∃¹²³ⁿ₀₁₂₃ªº‰†‡‹›′″‖¦¨¯´¸ÀÁÂÃÄÅÆÇÈÉÊËÌÍÎÏÐÑÒÓÔÕÖØÙÚÛÜÝÞßàáâãäåæçèéêëìíîïðñòóôõöøùúûüýþÿŒœŠšŸŽžƒ";

    private readonly Action<string> _insert;

    public SymbolWindow(Action<string> insert)
    {
        ArgumentNullException.ThrowIfNull(insert);
        InitializeComponent();
        _insert = insert;
        foreach (string symbol in Enumerate(Symbols))
        {
            var button = new Button
            {
                Content = symbol,
                Width = 30,
                Height = 30,
                Margin = new Thickness(1),
                Padding = new Thickness(0),
                FontSize = 16,
                Tag = symbol,
                ToolTip = "U+" + char.ConvertToUtf32(symbol, 0).ToString("X4", CultureInfo.InvariantCulture),
            };
            button.Click += OnSymbolClick;
            Grid.Children.Add(button);
        }
    }

    private static IEnumerable<string> Enumerate(string text)
    {
        var enumerator = StringInfo.GetTextElementEnumerator(text);
        while (enumerator.MoveNext())
        {
            yield return enumerator.GetTextElement();
        }
    }

    private void OnSymbolClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string symbol })
        {
            _insert(symbol);
        }
    }

    private string? CodeSymbol()
    {
        string text = CodeBox.Text.Trim();
        if (text.StartsWith("U+", StringComparison.OrdinalIgnoreCase) || text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            text = text[2..];
        }

        if (int.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int code) && code is >= 0x20 and <= 0x10FFFF && code is not (>= 0xD800 and <= 0xDFFF))
        {
            return char.ConvertFromUtf32(code);
        }

        return null;
    }

    private void OnCodeChanged(object sender, TextChangedEventArgs e)
    {
        string? symbol = CodeSymbol();
        CodePreview.Text = symbol ?? string.Empty;
        InsertCodeButton.IsEnabled = symbol is not null;
    }

    private void OnInsertCode(object sender, RoutedEventArgs e)
    {
        if (CodeSymbol() is { } symbol)
        {
            _insert(symbol);
        }
    }
}
