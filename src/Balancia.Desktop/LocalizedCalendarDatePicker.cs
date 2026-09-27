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

    private void OnCalendarPointerPressed(object? sender, PointerPressedEventArgs e) => ApplySelectedCulture();

    private void OnCalendarKeyDown(object? sender, KeyEventArgs e) => ApplySelectedCulture();

    private void OnCalendarPointerWheelChanged(object? sender, PointerWheelEventArgs e) => ApplySelectedCulture();

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
