using Balancia.Core;
using Microsoft.Data.Sqlite;

namespace Balancia.Storage;

public sealed partial class LedgerStore
{
    public LedgerSnapshot ReadSnapshot()
    {
        using var c = connections.Open();
        using var tx = c.BeginTransaction(deferred: true);
        var result = ReadSnapshot(c, tx);
        tx.Commit();
        return result;
    }

    /// <summary>The current month's overview, as <see cref="ReadDesktopSnapshotForFilter"/> reads it, plus every ledger entry.</summary>
    private LedgerSnapshot ReadSnapshot(SqliteConnection c, SqliteTransaction tx)
    {
        var today = Today;
        var start = new DateOnly(today.Year, today.Month, 1);
        var overview = ReadDesktopSnapshot(c, tx, new TransactionsFilter(From: start, To: start.AddMonths(1).AddDays(-1)));
        var entries = ReadEntries(c, tx, overview.Accounts.ToDictionary(account => account.Id, account => account.Name),
            overview.Categories.ToDictionary(category => category.Id));
        return overview with { Entries = entries };
    }

    private static List<LedgerEntry> ReadEntries(SqliteConnection c, SqliteTransaction tx,
        Dictionary<string, string> accountNames, Dictionary<string, Category> categoryMap)
    {
        var entries = new List<LedgerEntry>();
        using var cmd = LedgerSql.Command(c, tx, """
            SELECT l.id,l.kind,l.date,l.description,l.category_id,l.memo,m.account_id,m.amount,d.account_id
            FROM ledger l JOIN movements m ON m.transaction_id=l.id
            LEFT JOIN movements d ON l.kind='Transfer' AND d.transaction_id=l.id AND d.amount>0
            WHERE l.kind<>'OpeningBalance' AND (l.kind<>'Transfer' OR m.amount<0)
            ORDER BY l.date DESC,l.id DESC
            """);
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var category = r.IsDBNull(4) ? null : r.GetString(4);
            var account = r.GetString(6);
            var destination = r.IsDBNull(8) ? null : r.GetString(8);
            var draft = new TransactionDraft(Enum.Parse<TransactionKind>(r.GetString(1)), LedgerSql.ParseDate(r.GetString(2)), r.GetString(3),
                new Money(Math.Abs(r.GetInt64(7))), account, destination, category, r.GetString(5));
            entries.Add(new LedgerEntry(r.GetString(0), draft, accountNames[account], destination is null ? null : accountNames[destination],
                category is null ? null : categoryMap[category].Path));
        }

        return entries;
    }
}
