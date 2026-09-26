using Balancia.Core;

namespace Balancia.Storage;

public enum AverageInterval
{
    Day,
    Week,
    Month,
    Year,
}

public sealed record FlowAverages(Money Income, Money Expenses);

public sealed partial class LedgerStore
{
    public FlowAverages ReadFlowAverages(HistoryFilter filter, AverageInterval interval)
    {
        filter.Validate();

        using var connection = connections.Open();
        using var transaction = connection.BeginTransaction(deferred: true);
        var earliest = Scalar(connection, transaction,
            "SELECT MIN(date) FROM ledger WHERE kind IN ('Income','Expense')") as string;
        if (earliest is null)
        {
            return new FlowAverages(Money.Zero, Money.Zero);
        }

        var from = filter.From ?? ParseDate(earliest);
        var to = filter.To ?? Today;
        if (from > to)
        {
            return new FlowAverages(Money.Zero, Money.Zero);
        }

        var periodCount = PeriodCount(from, to, interval);
        var values = FilterParameters(filter with
        {
            From = from,
            To = to,
        });
        var income = Average("Income");
        var expenses = Average("Expense");
        transaction.Commit();
        return new FlowAverages(income, expenses);

        Money Average(string kind)
        {
            var total = Convert.ToDecimal(Scalar(connection, transaction,
                "SELECT COALESCE(SUM(abs(m.amount)),0) " + HistoryFrom + " AND l.kind=$flowKind",
                [.. values, ("$flowKind", kind)]));
            return new Money(checked((long)decimal.Round(total / periodCount, 0, MidpointRounding.AwayFromZero)));
        }
    }

    private static long PeriodCount(DateOnly from, DateOnly to, AverageInterval interval) => interval switch
    {
        AverageInterval.Day => (long)to.DayNumber - from.DayNumber + 1,
        AverageInterval.Week => ((long)Monday(to).DayNumber - Monday(from).DayNumber) / 7 + 1,
        AverageInterval.Month => ((long)to.Year - from.Year) * 12 + to.Month - from.Month + 1,
        AverageInterval.Year => (long)to.Year - from.Year + 1,
        _ => throw new ArgumentOutOfRangeException(nameof(interval)),
    };

    private static DateOnly Monday(DateOnly date) => date.AddDays(-(((int)date.DayOfWeek + 6) % 7));
}
