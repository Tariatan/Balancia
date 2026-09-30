using Balancia.Core;
using Microsoft.Data.Sqlite;
using System.Text.Json;

namespace Balancia.Storage;

public sealed record TransactionsFilter(string? Description = null, string? AccountId = null,
    TransactionKind? Kind = null, string? CategoryId = null, DateOnly? From = null,
    DateOnly? To = null, Money? Minimum = null, Money? Maximum = null,
    IReadOnlyList<string>? CategoryIds = null, IReadOnlyList<string>? AccountIds = null)
{
    public bool HasAccountFilter => AccountId is not null || AccountIds is { Count: > 0 };

    public bool HasCategoryFilter => CategoryId is not null || CategoryIds is { Count: > 0 };

    public void Validate()
    {
        if (From > To)
        {
            throw new ArgumentException("Start date must be on or before end date.");
        }

        if (Minimum < Money.Zero || Maximum < Money.Zero ||
            this is { Minimum: { } minimum, Maximum: { } maximum } && minimum > maximum)
        {
            throw new ArgumentException("Amount range must be positive and ordered.");
        }
    }
}
public sealed record TransactionsHit(LedgerEntry Entry, Money? AccountEffect);
public sealed record TransactionsPage(IReadOnlyList<TransactionsHit> Hits, long TotalCount, int Offset, int PageSize, long Revision);

public sealed partial class LedgerStore
{
    private const string TransactionsFrom = """
        FROM ledger l
        JOIN movements m ON m.transaction_id=l.id AND (l.kind<>'Transfer' OR m.amount<0)
        JOIN accounts a ON a.id=m.account_id
        LEFT JOIN movements d ON l.kind='Transfer' AND d.transaction_id=l.id AND d.amount>0
        LEFT JOIN accounts destination ON destination.id=d.account_id
        LEFT JOIN categories category ON category.id=l.category_id
        LEFT JOIN categories parent ON parent.id=category.parent_id
        WHERE l.kind<>'OpeningBalance'
          AND ($description IS NULL OR contains_ci(l.description,$description))
          AND ($accounts IS NULL OR EXISTS(SELECT 1 FROM movements matched WHERE matched.transaction_id=l.id AND matched.account_id IN (SELECT value FROM json_each($accounts))))
          AND ($kind IS NULL OR l.kind=$kind)
          AND ($categories IS NULL OR l.category_id IN (SELECT value FROM json_each($categories))
               OR category.parent_id IN (SELECT value FROM json_each($categories)))
          AND ($from IS NULL OR l.date>=$from)
          AND ($to IS NULL OR l.date<=$to)
          AND ($minimum IS NULL OR abs(m.amount)>=$minimum)
          AND ($maximum IS NULL OR abs(m.amount)<=$maximum)
        """;
    private const string TransactionsCount = """
        SELECT COUNT(*) FROM ledger l WHERE l.kind<>'OpeningBalance'
          AND ($description IS NULL OR contains_ci(l.description,$description))
          AND ($accounts IS NULL OR EXISTS(SELECT 1 FROM movements matched WHERE matched.transaction_id=l.id AND matched.account_id IN (SELECT value FROM json_each($accounts))))
          AND ($kind IS NULL OR l.kind=$kind)
          AND ($categories IS NULL OR l.category_id IN (SELECT value FROM json_each($categories))
               OR EXISTS(SELECT 1 FROM categories child WHERE child.id=l.category_id
                         AND child.parent_id IN (SELECT value FROM json_each($categories))))
          AND ($from IS NULL OR l.date>=$from)
          AND ($to IS NULL OR l.date<=$to)
          AND ($minimum IS NULL OR EXISTS(SELECT 1 FROM movements amount WHERE amount.transaction_id=l.id AND abs(amount.amount)>=$minimum))
          AND ($maximum IS NULL OR EXISTS(SELECT 1 FROM movements amount WHERE amount.transaction_id=l.id AND abs(amount.amount)<=$maximum))
        """;

    public TransactionsPage ReadTransactions(TransactionsFilter filter, int offset = 0, int pageSize = 100)
        => ReadTransactionsCore(filter, offset, pageSize, null, null);

    public TransactionsPage ReadAllTransactions(TransactionsFilter filter)
        => ReadTransactionsCore(filter, 0, null, null, null);

    public int FindTransactionOffset(string transactionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(transactionId);
        using var c = connections.Open();
        using var tx = c.BeginTransaction(deferred: true);
        var date = Scalar(c, tx, "SELECT date FROM ledger WHERE id=$id AND kind<>'OpeningBalance'", ("$id", transactionId)) as string
            ?? throw new ArgumentException("The selected transaction no longer exists.", nameof(transactionId));
        var offset = Convert.ToInt64(Scalar(c, tx, """
            SELECT COUNT(*) FROM ledger
            WHERE kind<>'OpeningBalance' AND (date>$date OR (date=$date AND id>$id))
            """, ("$date", date), ("$id", transactionId)));
        tx.Commit();
        return checked((int)offset);
    }

    public TransactionsPage ReadTransactionsAfter(TransactionsFilter filter, DateOnly afterDate, string afterId, int pageSize = 100)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(afterId);
        return ReadTransactionsCore(filter, 0, pageSize, afterDate, afterId);
    }

    private TransactionsPage ReadTransactionsCore(TransactionsFilter filter, int offset, int? pageSize, DateOnly? afterDate, string? afterId)
    {
        if (offset < 0 || pageSize is < 1 or > 1000)
        {
            throw new ArgumentOutOfRangeException(nameof(pageSize));
        }

        filter.Validate();

        using var c = connections.Open();
        using var tx = c.BeginTransaction(deferred: true);
        var values = FilterParameters(filter);
        var count = Convert.ToInt64(Scalar(c, tx, TransactionsCount, values));
        var hits = new List<TransactionsHit>();
        using (var cmd = Command(c, tx, """
            SELECT l.id,l.kind,l.date,l.description,l.category_id,l.memo,m.account_id,m.amount,
                   d.account_id,a.name,destination.name,
                   CASE WHEN category.id IS NULL THEN NULL WHEN parent.id IS NULL THEN category.name ELSE parent.name || ' / ' || category.name END,
                   d.amount
            """ + " " + TransactionsFrom +
            " AND ($cursorDate IS NULL OR l.date<$cursorDate OR (l.date=$cursorDate AND l.id<$cursorId))" +
            " ORDER BY l.date DESC,l.id DESC LIMIT $limit OFFSET $offset",
            [.. values, ("$cursorDate", afterDate is null ? null : DateText(afterDate.Value)),
                ("$cursorId", afterId), ("$limit", pageSize ?? -1), ("$offset", offset)]))
        using (var reader = cmd.ExecuteReader())
        {
            while (reader.Read())
            {
                var source = reader.GetString(6);
                var destination = reader.IsDBNull(8) ? null : reader.GetString(8);
                var amount = reader.GetInt64(7);
                var draft = new TransactionDraft(Enum.Parse<TransactionKind>(reader.GetString(1)),
                    ParseDate(reader.GetString(2)), reader.GetString(3), new Money(Math.Abs(amount)),
                    source, destination, reader.IsDBNull(4) ? null : reader.GetString(4), reader.GetString(5));
                var entry = new LedgerEntry(reader.GetString(0), draft, reader.GetString(9),
                    reader.IsDBNull(10) ? null : reader.GetString(10), reader.IsDBNull(11) ? null : reader.GetString(11));
                Money? effect = filter.AccountId is null ? null : new Money(filter.AccountId == source ? amount : reader.GetInt64(12));
                hits.Add(new TransactionsHit(entry, effect));
            }
        }

        var revision = Convert.ToInt64(Scalar(c, tx, "SELECT revision FROM metadata WHERE id=1"));
        tx.Commit();
        return new TransactionsPage(hits, count, offset, pageSize ?? hits.Count, revision);
    }

    // Shared by DesktopSnapshot.ReadDesktopSnapshotForFilter and FlowAverages.ReadFlowAverages, which both
    // need the absolute Income/Expense total for the same filtered period.
    private static object? ReadAbsoluteFlowTotal(SqliteConnection c, SqliteTransaction tx, (string, object?)[] filterValues, string kind) =>
        Scalar(c, tx, "SELECT COALESCE(SUM(abs(m.amount)),0) " + TransactionsFrom + " AND l.kind=$flowKind",
            [.. filterValues, ("$flowKind", kind)]);

    private static (string, object?)[] FilterParameters(TransactionsFilter filter) =>
    [
        ("$description", string.IsNullOrEmpty(filter.Description) ? null : filter.Description),
        ("$accounts", filter.HasAccountFilter ? JsonSerializer.Serialize((filter.AccountIds ?? []).Concat(filter.AccountId is { } accountId ? [accountId] : []).Distinct()) : null),
        ("$kind", filter.Kind?.ToString()),
        ("$category", filter.CategoryId),
        ("$categories", filter.HasCategoryFilter
            ? JsonSerializer.Serialize((filter.CategoryIds ?? []).Concat(filter.CategoryId is { } id ? [id] : []).Distinct())
            : null),
        ("$from", filter.From is null ? null : DateText(filter.From.Value)),
        ("$to", filter.To is null ? null : DateText(filter.To.Value)),
        ("$minimum", filter.Minimum?.Centimes),
        ("$maximum", filter.Maximum?.Centimes)
    ];
}
