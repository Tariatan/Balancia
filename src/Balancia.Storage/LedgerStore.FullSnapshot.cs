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

    // The top-category rollup below (subcategory rolls up to its parent's name) mirrors
    // DesktopSnapshot.ReadDesktopSnapshotForFilter's SQL version of the same rule — keep both in sync.
    private LedgerSnapshot ReadSnapshot(SqliteConnection c, SqliteTransaction tx)
    {
        var categories = ReadCategories(c, tx);
        var accounts = ReadAccountsWithBalances(c, tx);
        var accountMap = accounts.ToDictionary(a => a.Id);
        var categoryMap = categories.ToDictionary(a => a.Id);
        var entries = new List<LedgerEntry>();
        using (var cmd = Command(c, tx, """
            SELECT l.id,l.kind,l.date,l.description,l.category_id,l.memo,m.account_id,m.amount,d.account_id
            FROM ledger l JOIN movements m ON m.transaction_id=l.id
            LEFT JOIN movements d ON l.kind='Transfer' AND d.transaction_id=l.id AND d.amount>0
            WHERE l.kind<>'OpeningBalance' AND (l.kind<>'Transfer' OR m.amount<0)
            ORDER BY l.date DESC,l.id DESC
            """))
        using (var r = cmd.ExecuteReader())
        {
            while (r.Read())
            {
                var category = r.IsDBNull(4) ? null : r.GetString(4);
                var account = r.GetString(6);
                var destination = r.IsDBNull(8) ? null : r.GetString(8);
                var draft = new TransactionDraft(Enum.Parse<TransactionKind>(r.GetString(1)), ParseDate(r.GetString(2)), r.GetString(3),
                    new Money(Math.Abs(r.GetInt64(7))), account, destination, category, r.GetString(5));
                entries.Add(new LedgerEntry(r.GetString(0), draft, accountMap[account].Name, destination is null ? null : accountMap[destination].Name,
                    category is null ? null : categoryMap[category].Path));
            }
        }

        var today = Today;
        var monthEntries = entries.Where(e => e.Draft.Date.Year == today.Year && e.Draft.Date.Month == today.Month).ToArray();
        var top = monthEntries.Where(e => e.Draft.Kind == TransactionKind.Expense)
            .GroupBy(e => e.Draft.CategoryId is null ? "Uncategorized" : categoryMap[e.Draft.CategoryId].ParentId is { } parent ? categoryMap[parent].Name : categoryMap[e.Draft.CategoryId].Name)
            .Select(g => new CategoryTotal(g.Key, Total(g.Select(e => e.Draft.Amount.Centimes))))
            .OrderByDescending(g => g.Amount).Take(5).ToArray();
        return new LedgerSnapshot(accounts, categories, entries, Total(accounts.Select(a => a.Balance.Centimes)),
            Total(monthEntries.Where(e => e.Draft.Kind == TransactionKind.Income).Select(e => e.Draft.Amount.Centimes)),
            Total(monthEntries.Where(e => e.Draft.Kind == TransactionKind.Expense).Select(e => e.Draft.Amount.Centimes)), top,
            Convert.ToInt64(Scalar(c, tx, "SELECT revision FROM metadata WHERE id=1")));
        Money Total(IEnumerable<long> amounts) => new(checked((long)amounts.Sum(v => (decimal)v)));
    }
}
