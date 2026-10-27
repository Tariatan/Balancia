using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;

namespace Balancia.Desktop;

/// <summary>
/// A text box with a popup of earlier descriptions that start with what was typed. Up and Down move through the matches,
/// Tab accepts the highlighted one, Escape closes the popup, and clicking a match accepts it.
/// </summary>
internal sealed class DescriptionSuggestBox : Grid
{
    private static readonly IBrush SelectedRowBrush = Brush.Parse("#D8ECF3");

    private readonly TextBox input;
    private readonly List<string> candidates;
    private readonly StackPanel rows = new();
    private readonly Popup popup;
    private string[] matches = [];
    private int selected;
    private string? accepted;

    public DescriptionSuggestBox(TextBox input, IEnumerable<string> history)
    {
        this.input = input;
        candidates = history.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        popup = new Popup
        {
            PlacementTarget = input,
            Placement = PlacementMode.Bottom,
            IsLightDismissEnabled = true,
            Child = new Border
            {
                MinWidth = 300,
                Background = Brush.Parse("#E5E5E5"),
                BorderBrush = Brush.Parse("#BFC7CB"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(8),
                Child = new ScrollViewer { MaxHeight = 240, Content = rows }
            }
        };
        Children.Add(input);
        Children.Add(popup);

        input.TextChanged += (_, _) => HandleTextChanged();
        input.LostFocus += (_, _) => popup.IsOpen = false;
        input.AddHandler(KeyDownEvent, (_, args) => HandleKeyDown(args), RoutingStrategies.Tunnel);
    }

    /// <summary>Adds a description that was just saved, so it is offered from now on, first among its matches. A blank one is ignored.</summary>
    public void Remember(string description)
    {
        if (string.IsNullOrWhiteSpace(description))
        {
            return;
        }

        candidates.RemoveAll(value => string.Equals(value, description, StringComparison.OrdinalIgnoreCase));
        candidates.Insert(0, description);
    }

    /// <summary>The descriptions currently offered, best first.</summary>
    internal IReadOnlyList<string> Matches => matches;

    /// <summary>The offered rows, one button per match.</summary>
    internal IReadOnlyList<Control> Rows => rows.Children;

    /// <summary>The highlighted match.</summary>
    internal int SelectedIndex => selected;

    internal bool IsOpen => popup.IsOpen;

    private void HandleTextChanged()
    {
        if (string.Equals(input.Text, accepted, StringComparison.Ordinal))
        {
            popup.IsOpen = false;
            return;
        }

        accepted = null;
        var query = input.Text ?? string.Empty;
        matches = string.IsNullOrWhiteSpace(query) ? [] : candidates
            .Where(value => value.StartsWith(query, StringComparison.OrdinalIgnoreCase))
            .Take(20)
            .ToArray();
        selected = 0;
        Render();
        popup.IsOpen = input.IsFocused && matches.Length > 0;
    }

    private void HandleKeyDown(KeyEventArgs args)
    {
        if (!popup.IsOpen)
        {
            return;
        }

        switch (args.Key)
        {
            case Key.Up or Key.Down:
                selected = Math.Clamp(selected + (args.Key == Key.Down ? 1 : -1), 0, matches.Length - 1);
                Render();
                args.Handled = true;
                break;
            case Key.Tab when args.KeyModifiers == KeyModifiers.None:
                Accept(matches[selected]);
                break;
            case Key.Escape:
                popup.IsOpen = false;
                args.Handled = true;
                break;
        }
    }

    private void Accept(string value)
    {
        accepted = value;
        input.Text = value;
        input.CaretIndex = value.Length;
        popup.IsOpen = false;
    }

    private void Render()
    {
        rows.Children.Clear();
        foreach (var (index, value) in matches.Index())
        {
            var button = new Button
            {
                Content = value,
                Focusable = false,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Background = index == selected ? SelectedRowBrush : Brushes.Transparent
            };
            button.Click += (_, _) => Accept(value);
            rows.Children.Add(button);
        }
    }
}
