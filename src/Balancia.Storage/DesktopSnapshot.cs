using Balancia.Core;
using Microsoft.Data.Sqlite;

namespace Balancia.Storage;

public sealed partial class LedgerStore
{
    private const int TopCategoryCount = 5;

    public LedgerSnapshot ReadDesktopSnapshot()
    {
        var today = Today;
        var start = new DateOnly(today.Year, today.Month, 1);
        return ReadDesktopSnapshotForPeriod(start, start.AddMonths(1).AddDays(-1));
    }

    public LedgerSnapshot ReadDesktopSnapshotForPeriod(DateOnly? from, DateOnly? to)
        => ReadDesktopSnapshotForFilter(new TransactionsFilter(From: from, To: to));

    public LedgerSnapshot ReadDesktopSnapshotForFilter(TransactionsFilter filter)
    {
        using var c = connections.Open();
        using var tx = c.BeginTransaction(deferred: true);
        var result = ReadDesktopSnapshot(c, tx, filter);
        tx.Commit();
        return result;
    }

    /// <summary>The balances, filtered totals and top expense categories; <see cref="LedgerSnapshot.Entries"/> stays empty.</summary>
    private static LedgerSnapshot ReadDesktopSnapshot(SqliteConnection c, SqliteTransaction tx, TransactionsFilter filter)
    {
        filter.Validate();

        var categories = ReadCategories(c, tx);
        var accounts = ReadAccountsWithBalances(c, tx);
        var values = FilterParameters(filter);

        var result = new LedgerSnapshot(accounts, categories, [], new Money(accounts.Sum(a => a.Balance.Centimes)),
            new Money(PeriodTotal("Income")), new Money(PeriodTotal("Expense")), ReadTopExpenseCategories(c, tx, values),
            Convert.ToInt64(LedgerSql.Scalar(c, tx, "SELECT revision FROM metadata WHERE id=1")));
        return result;

        long PeriodTotal(string kind) => Convert.ToInt64(ReadAbsoluteFlowTotal(c, tx, values, kind));
    }

    private static List<CategoryTotal> ReadTopExpenseCategories(SqliteConnection c, SqliteTransaction tx, (string, object?)[] filterValues)
    {
        using var cmd = LedgerSql.Command(c, tx, $"""
            SELECT CASE WHEN parent.id=$category THEN category.name
                   ELSE COALESCE(parent.name,category.name,'Uncategorized') END,
                   SUM(-m.amount)
            {TransactionsFrom}
             AND l.kind='Expense'
            GROUP BY CASE WHEN parent.id=$category THEN category.id
                          ELSE COALESCE(parent.id,category.id,'') END
            ORDER BY 2 DESC LIMIT {TopCategoryCount}
            """, filterValues);
        using var r = cmd.ExecuteReader();
        var top = new List<CategoryTotal>(TopCategoryCount);
        while (r.Read())
        {
            top.Add(new CategoryTotal(r.GetString(0), new Money(r.GetInt64(1))));
        }

        return top;
    }
}
