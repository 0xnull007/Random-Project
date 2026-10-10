using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using Quill.App.Settings;

namespace Quill.App.Views;

/// <summary>File > Options: autosave interval, AutoCorrect rules and measurement units.</summary>
public partial class OptionsWindow : Window
{
    public OptionsWindow(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        InitializeComponent();
        AutosaveBox.ItemsSource = new[] { "0", "1", "2", "5", "10", "15", "30" };
        AutosaveBox.Text = settings.AutosaveMinutes.ToString(CultureInfo.CurrentCulture);
        QuotesBox.IsChecked = settings.AutoCorrect.SmartQuotes;
        DashesBox.IsChecked = settings.AutoCorrect.Dashes;
        SymbolsBox.IsChecked = settings.AutoCorrect.Symbols;
        CapitalizeBox.IsChecked = settings.AutoCorrect.CapitalizeSentences;
        ListsBox.IsChecked = settings.AutoCorrect.AutomaticLists;
        UnitsBox.SelectedIndex = settings.Units switch
        {
            "Inches" => 1,
            "Centimeters" => 2,
            _ => 0,
        };
        AutoCorrect = settings.AutoCorrect.Clone();
        Units = settings.Units;
        AutosaveMinutes = settings.AutosaveMinutes;
    }

    public int AutosaveMinutes { get; private set; }

    public AutoCorrectSettings AutoCorrect { get; private set; }

    public string Units { get; private set; }

    private void OnOk(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(AutosaveBox.Text, NumberStyles.Integer, CultureInfo.CurrentCulture, out int minutes) || minutes is < 0 or > 120)
        {
            ErrorText.Text = "Autosave minutes must be a whole number between 0 and 120.";
            ErrorText.Visibility = Visibility.Visible;
            return;
        }

        AutosaveMinutes = minutes;
        AutoCorrect = new AutoCorrectSettings
        {
            SmartQuotes = QuotesBox.IsChecked == true,
            Dashes = DashesBox.IsChecked == true,
            Symbols = SymbolsBox.IsChecked == true,
            CapitalizeSentences = CapitalizeBox.IsChecked == true,
            AutomaticLists = ListsBox.IsChecked == true,
        };
        Units = (UnitsBox.SelectedItem as ComboBoxItem)?.Tag as string ?? "Auto";
        DialogResult = true;
    }
}
