using Avalonia.Controls;

namespace Balancia.Desktop;

internal static class GridLayout
{
    /// <summary>Adds <paramref name="control"/> to <paramref name="grid"/> in the given column.</summary>
    internal static void Place(Grid grid, Control control, int column)
    {
        Grid.SetColumn(control, column);
        grid.Children.Add(control);
    }
}
