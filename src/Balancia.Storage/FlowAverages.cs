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
    public FlowAverages ReadFlowAverages(TransactionsFilter filter, AverageInterval interval)
    {
        filter.Validate();

        using var connection = connections.Open();
        using var transaction = connection.BeginTransaction(deferred: true);
        if (Scalar(connection, transaction,
                "SELECT MIN(date) FROM ledger WHERE kind IN ('Income','Expense')") is not string earliest)
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
            var total = Convert.ToDecimal(ReadAbsoluteFlowTotal(connection, transaction, values, kind));
            return new Money(checked((long)decimal.Round(total / periodCount, 0, MidpointRounding.AwayFromZero)));
        }
    }

    private static long PeriodCount(DateOnly from, DateOnly to, AverageInterval interval) => interval switch
    {
        AverageInterval.Day => (long)to.DayNumber - from.DayNumber + 1,
        AverageInterval.Week => ((long)CalendarWeek.StartOfWeek(to).DayNumber - CalendarWeek.StartOfWeek(from).DayNumber) / 7 + 1,
        AverageInterval.Month => ((long)to.Year - from.Year) * 12 + to.Month - from.Month + 1,
        AverageInterval.Year => (long)to.Year - from.Year + 1,
        _ => throw new ArgumentOutOfRangeException(nameof(interval)),
    };
}
