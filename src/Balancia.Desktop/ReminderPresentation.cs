using Balancia.Core;

namespace Balancia.Desktop;

/// <summary>Pure presentation rules for reminders: row emphasis, row color and footer totals.</summary>
internal static class ReminderPresentation
{
    internal readonly record struct Totals(Money Net, Money Upcoming, bool UpcomingIsThisMonth);

    internal static bool IsDueSoon(Reminder reminder, DateOnly today) =>
        reminder.Occurrence.DayNumber - today.DayNumber is >= 0 and <= 5;

    internal static bool IsEmphasized(Reminder reminder, DateOnly today) =>
        !reminder.Satisfied && (reminder.Overdue || IsDueSoon(reminder, today));

    internal static string ColorFor(Reminder reminder, DateOnly today)
    {
        var monthsUntilDue = (reminder.Occurrence.Year - today.Year) * 12 + reminder.Occurrence.Month - today.Month;
        return reminder switch
        {
            { Satisfied: true } => "#263C48",
            { Overdue: true } => "#B95D4D",
            _ when IsDueSoon(reminder, today) => "#B58B00",
            _ when monthsUntilDue == 1 => "#3989A7",
            _ when monthsUntilDue > 1 => "#2C8B6D",
            _ => "#263C48",
        };
    }

    internal static Totals Summarize(IReadOnlyList<Reminder> reminders, DateOnly today)
    {
        var net = reminders.Aggregate(Money.Zero, (total, r) => total + r.Template.IndicativeAmount);
        var pending = reminders.Where(r => !r.Satisfied).ToArray();
        var hasPendingThisMonth = pending.Any(r => IsInMonth(r, today));
        var upcomingMonth = hasPendingThisMonth ? today : today.AddMonths(1);
        var upcoming = pending
            .Where(r => IsInMonth(r, upcomingMonth))
            .Aggregate(Money.Zero, (total, r) => total + r.Template.IndicativeAmount);
        return new Totals(net, upcoming, hasPendingThisMonth);
    }

    private static bool IsInMonth(Reminder reminder, DateOnly month) =>
        reminder.Occurrence.Year == month.Year && reminder.Occurrence.Month == month.Month;
}
