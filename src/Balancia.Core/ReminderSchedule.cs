namespace Balancia.Core;

public static class ReminderSchedule
{
    public static DateOnly AddMonthsClamped(DateOnly date, int months, int desiredDay)
    {
        var shifted = date.AddMonths(months);
        return new DateOnly(shifted.Year, shifted.Month, Math.Min(desiredDay, DateTime.DaysInMonth(shifted.Year, shifted.Month)));
    }
}
