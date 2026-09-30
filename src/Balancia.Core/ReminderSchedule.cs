namespace Balancia.Core;

public static class ReminderSchedule
{
    public static DateOnly AddMonthsClamped(DateOnly date, int months, int desiredDay)
    {
        var first = new DateOnly(date.Year, date.Month, 1).AddMonths(months);
        return new DateOnly(first.Year, first.Month, Math.Min(desiredDay, DateTime.DaysInMonth(first.Year, first.Month)));
    }
}
