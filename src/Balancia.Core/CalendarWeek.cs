namespace Balancia.Core;

public static class CalendarWeek
{
    public static DateOnly StartOfWeek(DateOnly date) => date.AddDays(-(((int)date.DayOfWeek + 6) % 7));

    public static DateOnly EndOfWeek(DateOnly date) => StartOfWeek(date).AddDays(6);
}
