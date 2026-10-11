using System.Windows;

namespace Quill.App.Views;

/// <summary>Where (in editor coordinates) a mouse selection gesture ended.</summary>
public sealed class SelectionGestureEventArgs(Point viewPoint) : EventArgs
{
    public Point ViewPoint { get; } = viewPoint;
}
