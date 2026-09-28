using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Balancia.Core;

using static Balancia.Desktop.Localization.UiText;

namespace Balancia.Desktop;

public partial class MainWindow
{
    private static void AddColumn(Grid grid, Control control, int column)
    {
        Grid.SetColumn(control, column);
        grid.Children.Add(control);
    }

    private static void AddRow(Grid grid, Control control, int row)
    {
        Grid.SetRow(control, row);
        grid.Children.Add(control);
    }

    private static TextBlock QuietText(string content, double size) => new()
    {
        Text = Get(content),
        FontSize = size,
        Foreground = Brush.Parse("#71838D"),
        VerticalAlignment = VerticalAlignment.Center
    };

    private static IBrush BalanceColor(Money amount) => Brush.Parse(amount > Money.Zero ? "#2C8B6D" : "#B95D4D");

    private static CalendarDatePicker DateInput(DateOnly? date) => new LocalizedCalendarDatePicker
    {
        SelectedDate = date?.ToDateTime(TimeOnly.MinValue),
        SelectedDateFormat = CalendarDatePickerFormat.Custom,
        CustomDateFormatString = "yyyy-MM-dd",
        PlaceholderText = Get("Select a date"),
        FontSize = 13,
        HorizontalAlignment = HorizontalAlignment.Stretch
    };
    private static TextBox Input(string text) => new()
    {
        Text = text,
        HorizontalAlignment = HorizontalAlignment.Stretch
    };
    private static TextBlock Text(string text) => new()
    {
        Text = Get(text),
        TextWrapping = TextWrapping.Wrap,
        Foreground = Brush.Parse("#344D44")
    };

    private static StackPanel Field(string label, Control input)
    {
        AutomationProperties.SetName(input, Get(label));
        return new StackPanel
        {
            Spacing = 5,
            Children = { Text(label), input }
        };
    }
    private static WrapPanel Row(params Control[] controls)
    {
        var row = new WrapPanel();
        foreach (var control in controls)
        {
            control.Margin = new Thickness(0, 10, 10, 10);
            row.Children.Add(control);
        }
        return row;
    }

    private static Button ActionButton(string title, Func<Task> action)
    {
        var button = new Button { Content = Get(title) };
        button.Click += async (_, _) => await action();
        return button;
    }

    private sealed record Choice<T>(T Value, string Label)
    {
        public override string ToString() => Label;
    }
}
