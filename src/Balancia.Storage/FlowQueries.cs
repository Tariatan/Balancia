using Balancia.Core;

namespace Balancia.Storage;

public sealed partial class LedgerStore
{
    /// <summary>
    /// Reads daily income and expense totals for the filtered period and the equally long period before it.
    /// </summary>
    /// <remarks>
    /// <see cref="FlowAnalytics.Current"/> and <see cref="FlowAnalytics.Previous"/> are guaranteed to be sorted
    /// by date ascending with one entry per day (<c>GROUP BY l.date ORDER BY l.date</c>).
    /// </remarks>
    public FlowAnalytics ReadFlowAnalytics(TransactionsFilter filter)
    {
        filter.Validate();
        using var connection = connections.Open();
        using var transaction = connection.BeginTransaction(deferred: true);
        var to = filter.To ?? (filter.From is { } start && start > Today ? start : Today);
        var from = filter.From ?? ReadEarliestDate();
        var length = to.DayNumber - from.DayNumber + 1;
        DateOnly? previousTo = from.DayNumber == 0 ? null : from.AddDays(-1);
        DateOnly? previousFrom = previousTo is null ? null :
            DateOnly.FromDayNumber(Math.Max(0, from.DayNumber - length));

        // The previous period ends the day before the current one starts, so a single query covers both.
        List<DailyFlow> current = [];
        List<DailyFlow> previous = [];
        foreach (var day in ReadDays(previousFrom ?? from, to))
        {
            (day.Date < from ? previous : current).Add(day);
        }

        transaction.Commit();
        return new FlowAnalytics(from, to, previousFrom, previousTo, current, previous);

        DateOnly ReadEarliestDate()
        {
            return LedgerSql.Scalar(connection, transaction,
                "SELECT MIN(l.date) " + TransactionsFrom + " AND l.kind IN ('Income','Expense')",
                FilterParameters(filter with
                {
                    To = to
                })) is not string earliest ? to : LedgerSql.ParseDate(earliest);
        }

        IReadOnlyList<DailyFlow> ReadDays(DateOnly begin, DateOnly end)
        {
            var days = new List<DailyFlow>();
            using var command = LedgerSql.Command(connection, transaction,
                "SELECT l.date, SUM(CASE WHEN l.kind='Income' THEN m.amount ELSE 0 END), " +
                "SUM(CASE WHEN l.kind='Expense' THEN -m.amount ELSE 0 END) " + TransactionsFrom +
                " AND l.kind IN ('Income','Expense') GROUP BY l.date ORDER BY l.date",
                FilterParameters(filter with
                {
                    From = begin,
                    To = end,
                }));
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                days.Add(new DailyFlow(LedgerSql.ParseDate(reader.GetString(0)),
                    new Money(reader.GetInt64(1)), new Money(reader.GetInt64(2))));
            }

            return days;
        }
    }
}
