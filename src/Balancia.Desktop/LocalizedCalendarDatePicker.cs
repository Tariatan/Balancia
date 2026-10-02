using System.Globalization;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;

using static Balancia.Desktop.Localization.UiText;

namespace Balancia.Desktop;

internal sealed class LocalizedCalendarDatePicker : CalendarDatePicker
{
    private Avalonia.Controls.Calendar? calendar;

    protected override Type StyleKeyOverride => typeof(CalendarDatePicker);

    public LocalizedCalendarDatePicker()
    {
        CalendarOpened += (_, _) => RefreshCalendarCulture();
        AddHandler(KeyDownEvent, OnDateKeyDown, RoutingStrategies.Tunnel);
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        calendar?.RemoveHandler(InputElement.PointerPressedEvent, OnCalendarPointerPressed);
        calendar?.RemoveHandler(InputElement.KeyDownEvent, OnCalendarKeyDown);
        calendar?.RemoveHandler(InputElement.PointerWheelChangedEvent, OnCalendarPointerWheelChanged);

        base.OnApplyTemplate(e);

        calendar = e.NameScope.Find<Avalonia.Controls.Calendar>("PART_Calendar");
        calendar?.AddHandler(InputElement.PointerPressedEvent, OnCalendarPointerPressed, RoutingStrategies.Tunnel);
        calendar?.AddHandler(InputElement.KeyDownEvent, OnCalendarKeyDown, RoutingStrategies.Tunnel);
        calendar?.AddHandler(InputElement.PointerWheelChangedEvent, OnCalendarPointerWheelChanged, RoutingStrategies.Tunnel);
    }

    private void OnDateKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Handled || IsDropDownOpen || e.KeyModifiers != KeyModifiers.None ||
            e.Key is not (Key.Up or Key.Down) || SelectedDate is null)
        {
            return;
        }

        e.Handled = true;
        AdjustDate(e.Key == Key.Up ? 1 : -1);
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        if (!e.Handled && !IsDropDownOpen && SelectedDate is not null && e.Delta.Y != 0)
        {
            e.Handled = true;
            AdjustDate(e.Delta.Y > 0 ? 1 : -1);
        }

        base.OnPointerWheelChanged(e);
    }

    private void AdjustDate(int direction)
    {
        if (SelectedDate is not { } selectedDate)
        {
            return;
        }

        if (direction < 0 && selectedDate.Date == DateTime.MinValue.Date ||
            direction > 0 && selectedDate.Date == DateTime.MaxValue.Date)
        {
            return;
        }

        var nextDate = selectedDate.AddDays(direction);
        if (DisplayDateStart is { } start && nextDate.Date < start.Date ||
            DisplayDateEnd is { } end && nextDate.Date > end.Date ||
            BlackoutDates?.Contains(nextDate) == true)
        {
            return;
        }

        SetCurrentValue(SelectedDateProperty, nextDate);
    }

    private static void OnCalendarPointerPressed(object? sender, PointerPressedEventArgs e) => ApplySelectedCulture();

    private static void OnCalendarKeyDown(object? sender, KeyEventArgs e) => ApplySelectedCulture();

    private static void OnCalendarPointerWheelChanged(object? sender, PointerWheelEventArgs e) => ApplySelectedCulture();

    private void RefreshCalendarCulture()
    {
        ApplySelectedCulture();

        if (calendar is null)
        {
            return;
        }

        var firstDayOfWeek = Culture.DateTimeFormat.FirstDayOfWeek;
        FirstDayOfWeek = firstDayOfWeek;

        if (calendar.FirstDayOfWeek == firstDayOfWeek)
        {
            calendar.FirstDayOfWeek = firstDayOfWeek == DayOfWeek.Sunday
                ? DayOfWeek.Monday
                : DayOfWeek.Sunday;
        }

        calendar.FirstDayOfWeek = firstDayOfWeek;
    }

    private static void ApplySelectedCulture()
    {
        var culture = Culture;
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
    }
}
