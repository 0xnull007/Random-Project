using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;

namespace Quill.App;

/// <summary>
/// Works around a Fluent theme defect: the shipped ComboBox template paints its dropdown border with a brush that
/// ignores dark mode while the items' text follows it, which reads as white on white. Tooltips and context menus
/// are patched the same way. Colors are set directly on the popup elements when a dropdown opens, using the
/// palette the main window resolved.
/// </summary>
/// <remarks>
/// Hooks are attached by scanning a window's visual tree (<see cref="AttachAll"/>) rather than through the Loaded
/// event: a Loaded class handler on ComboBox was observed to fire only for editable combos. Call
/// <see cref="AttachAll"/> from every window's Loaded handler.
/// </remarks>
public static class PopupThemeFix
{
    private static readonly DependencyProperty AttachedProperty =
        DependencyProperty.RegisterAttached("IsAttached", typeof(bool), typeof(PopupThemeFix), new PropertyMetadata(false));

    private static readonly Palette Dark = new(Frozen(0xFF2C2C2Cu), Frozen(0x33000000u), Frozen(0xFFFFFFFFu));
    private static readonly Palette Light = new(Frozen(0xFFF9F9F9u), Frozen(0x0F000000u), Frozen(0xE4000000u));

    private static bool s_registered;

    /// <summary>Call once before any window is created.</summary>
    public static void Register()
    {
        if (s_registered)
        {
            return;
        }

        s_registered = true;
        EventManager.RegisterClassHandler(typeof(ComboBox), FrameworkElement.LoadedEvent, new RoutedEventHandler((s, _) => Attach(s as ComboBox)), handledEventsToo: true);
        EventManager.RegisterClassHandler(typeof(ToolTip), FrameworkElement.LoadedEvent, new RoutedEventHandler(OnPopupControlLoaded), handledEventsToo: true);
        EventManager.RegisterClassHandler(typeof(ContextMenu), FrameworkElement.LoadedEvent, new RoutedEventHandler(OnPopupControlLoaded), handledEventsToo: true);
    }

    /// <summary>Hooks every ComboBox under <paramref name="root"/>. Call after a window has loaded and whenever new content appears.</summary>
    public static void AttachAll(DependencyObject root)
    {
        ArgumentNullException.ThrowIfNull(root);
        foreach (ComboBox combo in Descendants<ComboBox>(root))
        {
            Attach(combo);
        }
    }

    /// <summary>The palette the application window resolved: light text means the dark theme is active.</summary>
    private static Palette CurrentPalette()
    {
        if (Application.Current?.MainWindow is { } window && window.TryFindResource("TextFillColorPrimary") is Color text)
        {
            return IsLight(text) ? Dark : Light;
        }

        return Light;
    }

    private static void Attach(ComboBox? combo)
    {
        if (combo is null || (bool)combo.GetValue(AttachedProperty))
        {
            return;
        }

        combo.SetValue(AttachedProperty, true);
        combo.DropDownOpened += (_, _) => combo.Dispatcher.BeginInvoke(() => FixDropDown(combo), DispatcherPriority.Loaded);
    }

    private static void FixDropDown(ComboBox combo)
    {
        try
        {
            Popup? popup = combo.Template?.FindName("PART_Popup", combo) as Popup ?? FindDescendant<Popup>(combo);
            if (popup?.Child is not FrameworkElement root)
            {
                return;
            }

            Palette palette = CurrentPalette();
            Border? border = root as Border ?? FindDescendant<Border>(root);
            if (border is not null)
            {
                border.Background = palette.Background;
                border.BorderBrush = palette.Border;
            }

            root.SetValue(TextElement.ForegroundProperty, palette.Foreground);
            for (int i = 0; i < combo.Items.Count; i++)
            {
                if (combo.ItemContainerGenerator.ContainerFromIndex(i) is ComboBoxItem item)
                {
                    item.Foreground = palette.Foreground;
                }
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or NullReferenceException)
        {
            LogFailure("dropdown", ex);
        }
    }

    private static void OnPopupControlLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not Control control)
        {
            return;
        }

        Palette palette = CurrentPalette();
        control.Background = palette.Background;
        control.BorderBrush = palette.Border;
        control.Foreground = palette.Foreground;
    }

    private static bool IsLight(Color color) => (color.R * 299 + color.G * 587 + color.B * 114) / 1000 > 128;

    private static IEnumerable<T> Descendants<T>(DependencyObject root)
        where T : DependencyObject
    {
        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(root, i);
            if (child is T match)
            {
                yield return match;
            }

            foreach (T nested in Descendants<T>(child))
            {
                yield return nested;
            }
        }
    }

    private static T? FindDescendant<T>(DependencyObject root)
        where T : DependencyObject
        => Descendants<T>(root).FirstOrDefault();

    private static void LogFailure(string what, Exception exception)
    {
        try
        {
            string path = Path.Combine(Path.GetDirectoryName(App.CrashLogPath)!, "theme.log");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.AppendAllText(path, $"{DateTimeOffset.Now:u} {what} fix failed: {exception}{Environment.NewLine}");
        }
        catch (Exception)
        {
            // Diagnostics must never break the app.
        }
    }

    private static SolidColorBrush Frozen(uint argb)
    {
        var brush = new SolidColorBrush(Color.FromArgb((byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb));
        brush.Freeze();
        return brush;
    }

    private sealed record Palette(SolidColorBrush Background, SolidColorBrush Border, SolidColorBrush Foreground);
}
