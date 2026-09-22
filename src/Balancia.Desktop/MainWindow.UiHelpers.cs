using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Balancia.Core;

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

    private static Border Panel(Control content) => new()
    {
        Background = Brushes.White,
        BorderBrush = Brush.Parse("#DCE6EB"),
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(6),
        Padding = new Thickness(15),
        Child = content
    };

    private static TextBlock QuietText(string content, double size) => new()
    {
        Text = content,
        FontSize = size,
        Foreground = Brush.Parse("#71838D"),
        VerticalAlignment = VerticalAlignment.Center
    };

    private static TextBlock Heading(string content, double size) => new()
    {
        Text = content,
        FontSize = size,
        FontWeight = FontWeight.SemiBold,
        Foreground = Brush.Parse("#263C48"),
        Margin = new Thickness(0, 0, 0, 10)
    };

    private static IBrush BalanceColor(Money amount) => Brush.Parse(amount > Money.Zero ? "#2C8B6D" : "#B95D4D");

    private static Grid TwoColumn(string left, string right, double size, IBrush? valueColor = null)
    {
        var row = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            Margin = new Thickness(0, 4)
        };
        row.Children.Add(new TextBlock
        {
            Text = left,
            FontSize = size,
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        var value = new TextBlock
        {
            Text = right,
            FontSize = size,
            FontWeight = FontWeight.SemiBold,
            Foreground = valueColor ?? Brush.Parse("#263C48"),
            Margin = new Thickness(8, 0, 0, 0)
        };
        AddColumn(row, value, 1);
        return row;
    }

    private static CalendarDatePicker DateInput(DateOnly? date) => new()
    {
        SelectedDate = date?.ToDateTime(TimeOnly.MinValue),
        SelectedDateFormat = CalendarDatePickerFormat.Custom,
        CustomDateFormatString = "yyyy-MM-dd",
        PlaceholderText = "Select a date",
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
        Text = text,
        TextWrapping = TextWrapping.Wrap,
        Foreground = Brush.Parse("#344D44")
    };

    private static StackPanel Field(string label, Control input)
    {
        AutomationProperties.SetName(input, label);
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
        var button = new Button { Content = title };
        button.Click += async (_, _) => await action();
        return button;
    }

    private static Button IconButton(string icon, string tooltip, Func<Task> action)
    {
        var button = ActionButton(icon, action);
        button.Width = 30;
        button.Padding = new Thickness(0);
        button.FontSize = 18;
        button.HorizontalContentAlignment = HorizontalAlignment.Center;
        ToolTip.SetTip(button, tooltip);
        return button;
    }
    private sealed record Choice<T>(T Value, string Label)
    {
        public override string ToString() => Label;
    }
}
