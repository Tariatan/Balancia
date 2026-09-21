using Microsoft.Data.Sqlite;
using Xunit;

namespace Balancia.Storage.Tests;

public sealed class SqliteConnectionFactoryTests : IDisposable
{
    private readonly string path = Path.Combine(Path.GetTempPath(), $"balancia-test-{Guid.NewGuid():N}.db");

    [Fact]
    public void CommittedIntegerAmountSurvivesReopening()
    {
        var factory = new SqliteConnectionFactory(path);
        using (var connection = factory.Open())
        {
            using var command = connection.CreateCommand();
            command.CommandText = "CREATE TABLE probe (amount INTEGER NOT NULL); INSERT INTO probe VALUES ($amount);";
            command.Parameters.AddWithValue("$amount", long.MaxValue);
            command.ExecuteNonQuery();
        }
        using var reopened = factory.Open();
        using var query = reopened.CreateCommand();
        query.CommandText = "SELECT amount FROM probe";
        Assert.Equal(long.MaxValue, (long)query.ExecuteScalar()!);
    }

    [Fact]
    public void EveryConnectionEnforcesForeignKeys()
    {
        var factory = new SqliteConnectionFactory(path);
        for (var attempt = 0; attempt < 2; attempt++)
        {
            using var connection = factory.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "CREATE TABLE IF NOT EXISTS parent (id INTEGER PRIMARY KEY); CREATE TABLE IF NOT EXISTS child (parent_id INTEGER REFERENCES parent(id)); INSERT INTO child VALUES (99);";
            var error = Assert.Throws<SqliteException>(() => command.ExecuteNonQuery());
            Assert.Equal(19, error.SqliteErrorCode);
        }
    }

    [Fact]
    public void RelativePathsAreRejected() => Assert.Throws<ArgumentException>(() => new SqliteConnectionFactory("ledger.db"));

    public void Dispose() => File.Delete(path);
}
