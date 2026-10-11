using System.Windows;

namespace Quill.App.Views;

/// <summary>Attached properties for the ribbon: the Segoe Fluent Icons glyph a large button shows above its label.</summary>
public static class Ribbon
{
    public static readonly DependencyProperty IconProperty =
        DependencyProperty.RegisterAttached("Icon", typeof(string), typeof(Ribbon), new PropertyMetadata(string.Empty));

    public static string GetIcon(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return (string)element.GetValue(IconProperty);
    }

    public static void SetIcon(DependencyObject element, string value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(IconProperty, value);
    }
}
