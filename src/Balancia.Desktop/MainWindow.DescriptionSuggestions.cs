using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;

namespace Balancia.Desktop;

public partial class MainWindow
{
    private static Control DescriptionInput(TextBox input, List<string> history)
    {
        var rows = new StackPanel();
        var popup = new Popup
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
        var container = new Grid();
        container.Children.Add(input);
        container.Children.Add(popup);
        string[] matches = [];
        var selected = 0;
        string? accepted = null;

        input.TextChanged += (_, _) =>
        {
            if (string.Equals(input.Text, accepted, StringComparison.Ordinal))
            {
                popup.IsOpen = false;
                return;
            }

            accepted = null;
            var query = input.Text ?? string.Empty;
            matches = string.IsNullOrWhiteSpace(query) ? [] : history
                .Where(value => value.StartsWith(query, StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase).Take(20).ToArray();
            selected = 0;
            Render();
            popup.IsOpen = input.IsFocused && matches.Length > 0;
        };
        input.LostFocus += (_, _) => popup.IsOpen = false;
        input.AddHandler(InputElement.KeyDownEvent, (_, args) =>
        {
            if (!popup.IsOpen || matches.Length == 0)
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
        }, RoutingStrategies.Tunnel);
        return container;

        void Accept(string value)
        {
            accepted = value;
            input.Text = value;
            input.CaretIndex = value.Length;
            popup.IsOpen = false;
        }

        void Render()
        {
            rows.Children.Clear();
            for (var index = 0; index < matches.Length; index++)
            {
                var value = matches[index];
                var button = new Button
                {
                    Content = value,
                    Focusable = false,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    HorizontalContentAlignment = HorizontalAlignment.Left,
                    Background = Brush.Parse(index == selected ? "#D8ECF3" : "Transparent")
                };
                button.Click += (_, _) => Accept(value);
                rows.Children.Add(button);
            }
        }
    }
}
