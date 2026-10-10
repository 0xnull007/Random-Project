using System.Globalization;
using System.Windows;

namespace Quill.App.Views;

/// <summary>Number of columns and rows for a new table.</summary>
public partial class InsertTableWindow : Window
{
    private const int MaxColumns = 63;
    private const int MaxRows = 200;

    public InsertTableWindow()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            ColumnsBox.Focus();
            ColumnsBox.SelectAll();
        };
    }

    public int Columns { get; private set; } = 3;

    public int Rows { get; private set; } = 3;

    private void OnOk(object sender, RoutedEventArgs e)
    {
        if (int.TryParse(ColumnsBox.Text.Trim(), NumberStyles.Integer, CultureInfo.CurrentCulture, out int columns)
            && int.TryParse(RowsBox.Text.Trim(), NumberStyles.Integer, CultureInfo.CurrentCulture, out int rows)
            && columns is >= 1 and <= MaxColumns && rows is >= 1 and <= MaxRows)
        {
            Columns = columns;
            Rows = rows;
            DialogResult = true;
            return;
        }

        ErrorText.Text = $"Please enter 1 to {MaxColumns} columns and 1 to {MaxRows} rows.";
        ErrorText.Visibility = Visibility.Visible;
    }
}
