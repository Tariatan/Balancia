using Microsoft.Data.Sqlite;

namespace Balancia.Storage;

/// <summary>Opens app-local databases (read-only on request) with foreign-key enforcement and the <c>contains_ci</c> search function wired up on every connection.</summary>
public sealed class SqliteConnectionFactory
{
    private readonly string connectionString;

    public SqliteConnectionFactory(string databasePath, bool readOnly = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        if (!Path.IsPathFullyQualified(databasePath))
        {
            throw new ArgumentException("Use an absolute application-local database path.", nameof(databasePath));
        }

        connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = readOnly ? SqliteOpenMode.ReadOnly : SqliteOpenMode.ReadWriteCreate,
            ForeignKeys = true,
            Pooling = false
        }.ToString();
    }

    public SqliteConnection Open()
    {
        var connection = new SqliteConnection(connectionString);
        try
        {
            connection.Open();
            connection.CreateFunction<string?, string?, bool>("contains_ci", ContainsIgnoreCase, isDeterministic: true);
            return connection;
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }

    private static bool ContainsIgnoreCase(string? value, string? term) =>
        value?.Contains(term ?? "", StringComparison.OrdinalIgnoreCase) ?? false;
}
