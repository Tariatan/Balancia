namespace Balancia.Storage.Tests;

/// <summary>Runs raw SQL against a ledger file so tests can inject faults and inspect storage directly.</summary>
internal static class SqliteScript
{
    public static void Execute(string path, string sql)
    {
        using var connection = new SqliteConnectionFactory(path).Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    public static object? Scalar(string path, string sql)
    {
        using var connection = new SqliteConnectionFactory(path).Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteScalar();
    }
}
