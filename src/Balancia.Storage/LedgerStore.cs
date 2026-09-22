using System.Globalization;
using Microsoft.Data.Sqlite;

namespace Balancia.Storage;

/// <summary>Owns the single-writer ledger boundary; all logical changes commit together.</summary>
public sealed partial class LedgerStore(string path, TimeProvider? clock = null)
{
    private readonly string path = path;
    private readonly SqliteConnectionFactory connections = new(path);
    private readonly TimeProvider clock = clock ?? TimeProvider.System;
    private DateOnly Today => DateOnly.FromDateTime(clock.GetLocalNow().DateTime);

    private void Write(Action<SqliteConnection, SqliteTransaction> action)
    {
        using var c = connections.Open();
        using var tx = c.BeginTransaction();
        action(c, tx);
        ValidateLedgerTotals(c, tx);
        Execute(c, tx, "UPDATE metadata SET revision=revision+1 WHERE id=1");
        tx.Commit();
    }

    private static void ValidateLedgerTotals(SqliteConnection c, SqliteTransaction tx)
    {
        var accountTotals = new Dictionary<string, decimal>();
        using (var cmd = Command(c, tx, "SELECT account_id,amount FROM movements"))
        using (var reader = cmd.ExecuteReader())
        {
            while (reader.Read())
            {
                var id = reader.GetString(0);
                accountTotals[id] = accountTotals.GetValueOrDefault(id) + reader.GetInt64(1);
            }
        }

        foreach (var total in accountTotals.Values)
        {
            _ = checked((long)total);
        }

        _ = checked((long)accountTotals.Values.Sum());
        var monthly = new Dictionary<(string, string), decimal>();
        using (var cmd = Command(c, tx, "SELECT substr(l.date,1,7),l.kind,m.amount FROM ledger l JOIN movements m ON m.transaction_id=l.id WHERE l.kind IN ('Expense','Income')"))
        using (var reader = cmd.ExecuteReader())
        {
            while (reader.Read())
            {
                var key = (reader.GetString(0), reader.GetString(1));
                monthly[key] = monthly.GetValueOrDefault(key) + Math.Abs((decimal)reader.GetInt64(2));
            }
        }

        foreach (var total in monthly.Values)
        {
            _ = checked((long)total);
        }
    }

    private static void Require(SqliteConnection c, SqliteTransaction tx, string sql, string id, string error)
    {
        if (Scalar(c, tx, sql, ("$id", id)) is null)
        {
            throw new ArgumentException(error);
        }
    }
    private static SqliteCommand Command(SqliteConnection c, SqliteTransaction tx, string sql, params (string, object?)[] parameters)
    {
        var cmd = c.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        foreach (var (name, value) in parameters)
        {
            cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }

        return cmd;
    }
    private static object? Scalar(SqliteConnection c, SqliteTransaction tx, string sql, params (string, object?)[] parameters)
    {
        using var cmd = Command(c, tx, sql, parameters);
        var result = cmd.ExecuteScalar();
        return result is DBNull ? null : result;
    }
    private static void Execute(SqliteConnection c, SqliteTransaction tx, string sql, params (string, object?)[] parameters)
    {
        using var cmd = Command(c, tx, sql, parameters);
        cmd.ExecuteNonQuery();
    }
    private static string DateText(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    private static DateOnly ParseDate(string date) => DateOnly.ParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture);
}
