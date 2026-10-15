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
    // DesktopSnapshot.ReadDesktopSnapshotForFilter's SQL version of the same rule - keep both in sync.
    private LedgerSnapshot ReadSnapshot(SqliteConnection c, SqliteTransaction tx)
    {
        var categories = ReadCategories(c, tx);
        var accounts = ReadAccountsWithBalances(c, tx);
        var categoryMap = categories.ToDictionary(category => category.Id);
        var entries = ReadEntries(c, tx, accounts.ToDictionary(account => account.Id, account => account.Name), categoryMap);

        var today = Today;
        var monthEntries = entries.Where(e => e.Draft.Date.Year == today.Year && e.Draft.Date.Month == today.Month).ToArray();
        var monthExpenses = monthEntries.Where(e => e.Draft.Kind == TransactionKind.Expense).ToArray();
        var top = monthExpenses
            .GroupBy(e => TopCategoryName(e.Draft.CategoryId))
            .Select(g => new CategoryTotal(g.Key, Total(g.Select(e => e.Draft.Amount.Centimes))))
            .OrderByDescending(g => g.Amount)
            .Take(TopCategoryCount)
            .ToArray();

        return new LedgerSnapshot(accounts, categories, entries, Total(accounts.Select(a => a.Balance.Centimes)),
            Total(monthEntries.Where(e => e.Draft.Kind == TransactionKind.Income).Select(e => e.Draft.Amount.Centimes)),
            Total(monthExpenses.Select(e => e.Draft.Amount.Centimes)), top,
            Convert.ToInt64(LedgerSql.Scalar(c, tx, "SELECT revision FROM metadata WHERE id=1")));

        string TopCategoryName(string? categoryId) => categoryId is null
            ? "Uncategorized"
            : categoryMap[categoryId].ParentId is { } parentId ? categoryMap[parentId].Name : categoryMap[categoryId].Name;

        // Decimal sums keep the total exact, so a value outside 64-bit centimes throws OverflowException instead of wrapping.
        static Money Total(IEnumerable<long> amounts) => new((long)amounts.Sum(v => (decimal)v));
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
