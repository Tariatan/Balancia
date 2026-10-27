using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Balancia.Core;

using static Balancia.Desktop.Localization.UiText;

namespace Balancia.Desktop;

/// <summary>The controls the edit dialogs are made of: labelled fields, date and text inputs, choice boxes and button rows.</summary>
internal static class FormControls
{
    private static readonly IBrush BodyTextBrush = Brush.Parse("#344D44");

    internal static CalendarDatePicker DateInput(DateOnly? date) => new LocalizedCalendarDatePicker
    {
        SelectedDate = date?.ToDateTime(TimeOnly.MinValue),
        SelectedDateFormat = CalendarDatePickerFormat.Custom,
        CustomDateFormatString = "yyyy-MM-dd",
        PlaceholderText = Get("Select a date"),
        FontSize = 13,
        HorizontalAlignment = HorizontalAlignment.Stretch
    };

    internal static TextBox Input(string text) => new()
    {
        Text = text,
        HorizontalAlignment = HorizontalAlignment.Stretch
    };

    internal static TextBlock Text(string text) => new()
    {
        Text = Get(text),
        TextWrapping = TextWrapping.Wrap,
        Foreground = BodyTextBrush
    };

    /// <summary>The input under its localized label, which also becomes the input's accessibility name.</summary>
    internal static StackPanel Field(string label, Control input)
    {
        AutomationProperties.SetName(input, Get(label));
        return new StackPanel
        {
            Spacing = 5,
            Children = { Text(label), input }
        };
    }

    internal static WrapPanel Row(params Control[] controls)
    {
        var row = new WrapPanel();
        foreach (var control in controls)
        {
            control.Margin = new Thickness(0, 10, 10, 10);
            row.Children.Add(control);
        }

        return row;
    }

    internal static ComboBox ChoiceBox<T>(Choice<T>[] choices, Choice<T>? selected) => new()
    {
        ItemsSource = choices,
        SelectedItem = selected,
        HorizontalAlignment = HorizontalAlignment.Stretch
    };

    internal static T SelectedValue<T>(ComboBox box) => ((Choice<T>)box.SelectedItem!).Value;
}
