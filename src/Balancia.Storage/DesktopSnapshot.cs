using Balancia.Core;

namespace Balancia.Storage;

public sealed partial class LedgerStore
{
    public LedgerSnapshot ReadDesktopSnapshot()
    {
        var today = Today;
        var start = new DateOnly(today.Year, today.Month, 1);
        return ReadDesktopSnapshotForPeriod(start, start.AddMonths(1).AddDays(-1));
    }

    public LedgerSnapshot ReadDesktopSnapshotForPeriod(DateOnly? from, DateOnly? to)
        => ReadDesktopSnapshotForFilter(new HistoryFilter(From: from, To: to));

    public LedgerSnapshot ReadDesktopSnapshotForFilter(HistoryFilter filter)
    {
        filter.Validate();

        using var c = connections.Open();
        using var tx = c.BeginTransaction(deferred: true);
        var categories = ReadCategories(c, tx);
        var accounts = ReadAccountsWithBalances(c, tx);

        var values = FilterParameters(filter);
        var top = new List<CategoryTotal>();
        // Mirrors the top-category rollup in LedgerStore.ReadSnapshot's LINQ grouping — keep both in sync.
        using (var cmd = Command(c, tx, """
            SELECT CASE WHEN parent.id=$category THEN category.name
                   ELSE COALESCE(parent.name,category.name,'Uncategorized') END,
                   SUM(-m.amount)
            """ + " " + HistoryFrom + """
             AND l.kind='Expense'
            GROUP BY CASE WHEN parent.id=$category THEN category.id
                          ELSE COALESCE(parent.id,category.id,'') END
            ORDER BY 2 DESC LIMIT 5
            """, values))
        using (var r = cmd.ExecuteReader())
        {
            while (r.Read())
            {
                top.Add(new CategoryTotal(r.GetString(0), new Money(r.GetInt64(1))));
            }
        }

        var result = new LedgerSnapshot(accounts, categories, [], new Money(checked((long)accounts.Sum(a => (decimal)a.Balance.Centimes))),
            new Money(PeriodTotal("Income")), new Money(PeriodTotal("Expense")), top,
            Convert.ToInt64(Scalar(c, tx, "SELECT revision FROM metadata WHERE id=1")));
        tx.Commit();
        return result;

        long PeriodTotal(string kind) => Convert.ToInt64(Scalar(c, tx,
            "SELECT COALESCE(SUM(abs(m.amount)),0) " + HistoryFrom + " AND l.kind=$flowKind",
            [.. values, ("$flowKind", kind)]));
    }
}
