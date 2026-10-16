using Balancia.Core;

namespace Balancia.Desktop;

/// <summary>Pure presentation rules for reminders: row emphasis, row color and footer totals.</summary>
internal static class ReminderPresentation
{
    private const string TextColor = "#263C48";
    private const string OverdueColor = "#B95D4D";
    private const string DueSoonColor = "#B58B00";
    private const string NextMonthColor = "#3989A7";
    private const string LaterColor = "#2C8B6D";

    internal readonly record struct Totals(Money Net, Money Upcoming, bool UpcomingIsThisMonth);

    internal static bool IsEmphasized(Reminder reminder, DateOnly today) =>
        reminder.Overdue || IsDueSoon(reminder, today);

    internal static string ColorFor(Reminder reminder, DateOnly today)
    {
        if (reminder.Overdue) return OverdueColor;
        if (IsDueSoon(reminder, today)) return DueSoonColor;

        var monthsUntilDue = (reminder.Occurrence.Year - today.Year) * 12 + reminder.Occurrence.Month - today.Month;
        return monthsUntilDue switch
        {
            1 => NextMonthColor,
            > 1 => LaterColor,
            _ => TextColor,
        };
    }

    internal static Totals Summarize(IReadOnlyList<Reminder> reminders, DateOnly today)
    {
        var nextMonth = today.AddMonths(1);
        var net = Money.Zero;
        var pendingThisMonth = Money.Zero;
        var pendingNextMonth = Money.Zero;
        var hasPendingThisMonth = false;

        foreach (var reminder in reminders)
        {
            var amount = reminder.Template.IndicativeAmount;
            net += amount;
            if (IsInMonth(reminder, today))
            {
                pendingThisMonth += amount;
                hasPendingThisMonth = true;
            }
            else if (IsInMonth(reminder, nextMonth))
            {
                pendingNextMonth += amount;
            }
        }

        return new Totals(net, hasPendingThisMonth ? pendingThisMonth : pendingNextMonth, hasPendingThisMonth);
    }

    private static bool IsDueSoon(Reminder reminder, DateOnly today) =>
        reminder.Occurrence.DayNumber - today.DayNumber is >= 0 and <= 5;

    private static bool IsInMonth(Reminder reminder, DateOnly month) =>
        reminder.Occurrence.Year == month.Year && reminder.Occurrence.Month == month.Month;
}
