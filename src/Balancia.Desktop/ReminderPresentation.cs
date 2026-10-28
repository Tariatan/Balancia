using Avalonia.Media;
using Balancia.Core;

namespace Balancia.Desktop;

/// <summary>Pure presentation rules for reminders: row emphasis, row brush and footer totals.</summary>
internal static class ReminderPresentation
{

    internal readonly record struct Totals(Money Net, Money Upcoming, bool UpcomingIsThisMonth);

    internal static bool IsEmphasized(Reminder reminder, DateOnly today) =>
        reminder.Overdue || IsDueSoon(reminder, today);

    /// <summary>Whether the reminder falls beyond next month, which tints its list row.</summary>
    internal static bool IsBeyondNextMonth(Reminder reminder, DateOnly today) =>
        ReminderSchedule.MonthsUntil(reminder.Occurrence, today) >= 2;

    internal static IBrush BrushFor(Reminder reminder, DateOnly today)
    {
        if (reminder.Overdue) return Palette.Negative;
        if (IsDueSoon(reminder, today)) return Palette.Warning;

        return ReminderSchedule.MonthsUntil(reminder.Occurrence, today) switch
        {
            1 => Palette.Accent,
            > 1 => Palette.Positive,
            _ => Palette.Text,
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
