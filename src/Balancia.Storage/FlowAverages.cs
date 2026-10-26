using Balancia.Core;

namespace Balancia.Storage;

public sealed record FlowAverages(Money Income, Money Expenses)
{
    public static FlowAverages Zero { get; } = new(Money.Zero, Money.Zero);
}

public sealed partial class LedgerStore
{
    public FlowAverages ReadFlowAverages(TransactionsFilter filter, AverageInterval interval)
    {
        filter.Validate();

        using var connection = connections.Open();
        using var transaction = connection.BeginTransaction(deferred: true);
        if (LedgerSql.Scalar(connection, transaction,
                "SELECT MIN(date) FROM ledger WHERE kind IN ('Income','Expense')") is not string earliest)
        {
            return FlowAverages.Zero;
        }

        var from = filter.From ?? LedgerSql.ParseDate(earliest);
        var to = filter.To ?? Today;
        if (from > to)
        {
            return FlowAverages.Zero;
        }

        var periodCount = interval.CountPeriods(from, to);
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
            var total = Convert.ToInt64(ReadAbsoluteFlowTotal(connection, transaction, values, kind));
            return new Money((long)decimal.Round((decimal)total / periodCount, 0, MidpointRounding.AwayFromZero));
        }
    }
}
