using System.Globalization;
using Microsoft.Data.Sqlite;

namespace Balancia.Storage;

/// <summary>Parameterized SQLite command helpers and the ledger's text date format, shared by every storage operation.</summary>
internal static class LedgerSql
{
    private const string DateFormat = "yyyy-MM-dd";

    public static void Require(SqliteConnection c, SqliteTransaction tx, string sql, string id, string error)
    {
        if (Scalar(c, tx, sql, ("$id", id)) is null)
        {
            throw new ArgumentException(error);
        }
    }

    public static SqliteCommand Command(SqliteConnection c, SqliteTransaction tx, string sql, params (string, object?)[] parameters)
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

    public static object? Scalar(SqliteConnection c, SqliteTransaction tx, string sql, params (string, object?)[] parameters)
    {
        using var cmd = Command(c, tx, sql, parameters);
        var result = cmd.ExecuteScalar();
        return result is DBNull ? null : result;
    }

    public static void Execute(SqliteConnection c, SqliteTransaction tx, string sql, params (string, object?)[] parameters)
    {
        using var cmd = Command(c, tx, sql, parameters);
        cmd.ExecuteNonQuery();
    }

    public static string DateText(DateOnly date) => date.ToString(DateFormat, CultureInfo.InvariantCulture);

    public static DateOnly ParseDate(string date) => DateOnly.ParseExact(date, DateFormat, CultureInfo.InvariantCulture);
}
