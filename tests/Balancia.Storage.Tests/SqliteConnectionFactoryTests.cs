using Microsoft.Data.Sqlite;
using Xunit;

namespace Balancia.Storage.Tests;

public sealed class SqliteConnectionFactoryTests : IDisposable
{
    private readonly string path = Path.Combine(Path.GetTempPath(), $"balancia-test-{Guid.NewGuid():N}.db");

    [Fact]
    public void Open_CommittedIntegerAmount_SurvivesReopening()
    {
        // Arrange
        var factory = new SqliteConnectionFactory(path);
        using (var connection = factory.Open())
        {
            using var insert = connection.CreateCommand();
            insert.CommandText = "CREATE TABLE probe (amount INTEGER NOT NULL); INSERT INTO probe VALUES ($amount);";
            insert.Parameters.AddWithValue("$amount", long.MaxValue);
            insert.ExecuteNonQuery();
        }

        // Act
        using var reopened = factory.Open();
        using var query = reopened.CreateCommand();
        query.CommandText = "SELECT amount FROM probe";
        var amount = (long)query.ExecuteScalar()!;

        // Assert
        Assert.Equal(long.MaxValue, amount);
    }

    [Fact]
    public void Open_RepeatedConnections_EachEnforcesForeignKeys()
    {
        // Arrange
        var factory = new SqliteConnectionFactory(path);
        var errorCodes = new List<int>();

        // Act
        for (var attempt = 0; attempt < 2; attempt++)
        {
            using var connection = factory.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "CREATE TABLE IF NOT EXISTS parent (id INTEGER PRIMARY KEY); " +
                "CREATE TABLE IF NOT EXISTS child (parent_id INTEGER REFERENCES parent(id)); INSERT INTO child VALUES (99);";
            errorCodes.Add(Assert.Throws<SqliteException>(() => command.ExecuteNonQuery()).SqliteErrorCode);
        }

        // Assert
        Assert.Equal([19, 19], errorCodes);
    }

    [Fact]
    public void Constructor_RelativePath_ThrowsArgumentException()
    {
        // Arrange
        const string relativePath = "ledger.db";

        // Act
        void Act() => _ = new SqliteConnectionFactory(relativePath);

        // Assert
        Assert.Throws<ArgumentException>(Act);
    }

    public void Dispose() => File.Delete(path);
}
