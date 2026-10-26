namespace Balancia.Core;

public static class ReminderSchedule
{
    public static DateOnly AddMonthsClamped(DateOnly date, int months, int desiredDay)
    {
        var shifted = date.AddMonths(months);
        return new DateOnly(shifted.Year, shifted.Month, Math.Min(desiredDay, DateTime.DaysInMonth(shifted.Year, shifted.Month)));
    }

    /// <summary>
    /// The first occurrence of <paramref name="template"/> whose calendar month has no payment, starting at its expected date
    /// and stepping by the repeat interval (a payment in any other month, including an earlier one, does not count).
    /// </summary>
    /// <param name="isPaid">Whether the calendar month of the given date already holds a payment for this reminder.</param>
    public static Reminder Resolve(ReminderTemplate template, DateOnly today, Func<DateOnly, bool> isPaid)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(template.IntervalMonths);
        var occurrence = template.ExpectedDate;
        while (isPaid(occurrence))
        {
            occurrence = AddMonthsClamped(occurrence, template.IntervalMonths, template.ExpectedDate.Day);
        }

        return new Reminder(template, occurrence, occurrence < today);
    }

    /// <summary>Whole calendar months from the month of <paramref name="today"/> to the month of <paramref name="occurrence"/>; 0 is this month, negative is earlier.</summary>
    public static int MonthsUntil(DateOnly occurrence, DateOnly today) =>
        (occurrence.Year - today.Year) * 12 + occurrence.Month - today.Month;
}
