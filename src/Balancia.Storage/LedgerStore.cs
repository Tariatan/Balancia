using System.Runtime.CompilerServices;
using Microsoft.Data.Sqlite;

namespace Balancia.Storage;

/// <summary>Owns the single-writer ledger boundary; all logical changes commit together.</summary>
public sealed partial class LedgerStore(string path, TimeProvider? clock = null)
{
    private readonly string path = path;
    private readonly SqliteConnectionFactory connections = new(path);
    private readonly TimeProvider clock = clock ?? TimeProvider.System;
    private DateOnly Today => DateOnly.FromDateTime(clock.GetLocalNow().DateTime);

    private void Write(Action<SqliteConnection, SqliteTransaction> action, [CallerMemberName] string operation = "") =>
        LogOperation(() =>
        {
            using var c = connections.Open();
            using var tx = c.BeginTransaction();
            action(c, tx);
            ValidateLedgerTotals(c, tx);
            LedgerSql.Execute(c, tx, "UPDATE metadata SET revision=revision+1 WHERE id=1");
            tx.Commit();
        }, operation);

    /// <summary>Rejects a write whose account or monthly totals no longer fit in 64-bit centimes; sums are exact decimals so overflow surfaces as <see cref="OverflowException"/>.</summary>
    private static void ValidateLedgerTotals(SqliteConnection c, SqliteTransaction tx)
    {
        var accountTotals = ReadAccountTotals(c, tx).Values;
        EnsureFitsInLong(accountTotals);
        _ = (long)accountTotals.Sum();
        EnsureFitsInLong(ReadMonthlyTotals(c, tx).Values);
    }

    /// <summary>
    /// Sums per account inside SQLite so only one row per account crosses into .NET. SQLite's own <c>SUM</c> throws on 64-bit overflow,
    /// so each amount is split into a signed high half and an unsigned low half whose sums cannot overflow and are recombined as exact decimals.
    /// </summary>
    private static Dictionary<string, decimal> ReadAccountTotals(SqliteConnection c, SqliteTransaction tx)
    {
        var totals = new Dictionary<string, decimal>();
        using var cmd = LedgerSql.Command(c, tx, "SELECT account_id,SUM(amount>>32),SUM(amount&4294967295) FROM movements GROUP BY account_id");
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            totals[reader.GetString(0)] = Combine(reader.GetInt64(1), reader.GetInt64(2));
        }

        return totals;
    }

    /// <summary>Sums absolute income and expense amounts per month and kind, split like <see cref="ReadAccountTotals"/> (positives and negatives apart, since <c>abs</c> overflows on the minimum value).</summary>
    private static Dictionary<(string Month, string Kind), decimal> ReadMonthlyTotals(SqliteConnection c, SqliteTransaction tx)
    {
        var totals = new Dictionary<(string Month, string Kind), decimal>();
        using var cmd = LedgerSql.Command(c, tx, """
            SELECT substr(l.date,1,7),l.kind,
                   SUM(CASE WHEN m.amount>0 THEN m.amount>>32 ELSE 0 END),
                   SUM(CASE WHEN m.amount>0 THEN m.amount&4294967295 ELSE 0 END),
                   SUM(CASE WHEN m.amount<0 THEN m.amount>>32 ELSE 0 END),
                   SUM(CASE WHEN m.amount<0 THEN m.amount&4294967295 ELSE 0 END)
            FROM ledger l JOIN movements m ON m.transaction_id=l.id
            WHERE l.kind IN ('Expense','Income')
            GROUP BY 1,2
            """);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            totals[(reader.GetString(0), reader.GetString(1))] =
                Combine(reader.GetInt64(2), reader.GetInt64(3)) - Combine(reader.GetInt64(4), reader.GetInt64(5));
        }

        return totals;
    }

    private static decimal Combine(long high, long low) => high * 4294967296m + low;

    private static void EnsureFitsInLong(IEnumerable<decimal> totals)
    {
        foreach (var total in totals)
        {
            _ = (long)total;
        }
    }
}
