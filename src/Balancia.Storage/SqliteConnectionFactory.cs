using Microsoft.Data.Sqlite;

namespace Balancia.Storage;

/// <summary>Opens app-local databases with foreign-key enforcement on every connection.</summary>
public sealed class SqliteConnectionFactory
{
    private readonly string _connectionString;

    public SqliteConnectionFactory(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        if (!Path.IsPathFullyQualified(databasePath))
        {
            throw new ArgumentException("Use an absolute application-local database path.", nameof(databasePath));
        }

        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            ForeignKeys = true,
            Pooling = false

        }.ToString();
    }

    public SqliteConnection Open()
    {
        var connection = new SqliteConnection(_connectionString);
        try
        {
            connection.Open();
            connection.CreateFunction<string?, string?, bool>("contains_ci", (value, term) =>
                value?.Contains(term ?? "", StringComparison.OrdinalIgnoreCase) ?? false);
            return connection;
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }
}
