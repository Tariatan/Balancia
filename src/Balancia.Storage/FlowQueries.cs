using Balancia.Core;

namespace Balancia.Storage;

public sealed partial class LedgerStore
{
    public FlowAnalytics ReadFlowAnalytics(HistoryFilter filter)
    {
        filter.Validate();
        using var connection = connections.Open();
        using var transaction = connection.BeginTransaction(deferred: true);
        var to = filter.To ?? (filter.From is { } start && start > Today ? start : Today);
        var earliest = Scalar(connection, transaction,
            "SELECT MIN(l.date) " + HistoryFrom + " AND l.kind IN ('Income','Expense')",
            FilterParameters(filter with
            {
                To = to
            })) as string;
        var from = filter.From ?? (earliest is null ? to : ParseDate(earliest));
        var length = to.DayNumber - from.DayNumber + 1;
        DateOnly? previousTo = from.DayNumber == 0 ? null : from.AddDays(-1);
        DateOnly? previousFrom = previousTo is null ? null :
            DateOnly.FromDayNumber(Math.Max(0, from.DayNumber - length));
        var current = ReadDays(from, to);
        var previous = previousFrom is { } first && previousTo is { } last ? ReadDays(first, last) : [];
        transaction.Commit();
        return new FlowAnalytics(from, to, previousFrom, previousTo, current, previous);

        IReadOnlyList<DailyFlow> ReadDays(DateOnly first, DateOnly last)
        {
            var days = new List<DailyFlow>();
            using var command = Command(connection, transaction,
                "SELECT l.date, SUM(CASE WHEN l.kind='Income' THEN m.amount ELSE 0 END), " +
                "SUM(CASE WHEN l.kind='Expense' THEN -m.amount ELSE 0 END) " + HistoryFrom +
                " AND l.kind IN ('Income','Expense') GROUP BY l.date ORDER BY l.date",
                FilterParameters(filter with
                {
                    From = first,
                    To = last,
                }));
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                days.Add(new DailyFlow(ParseDate(reader.GetString(0)),
                    new Money(reader.GetInt64(1)), new Money(reader.GetInt64(2))));
            }

            return days;
        }
    }
}
