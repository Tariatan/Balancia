using Balancia.Core;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Balancia.Storage.Tests;

public sealed class LedgerFailuresTests : IDisposable
{
    private readonly string path = Path.Combine(Path.GetTempPath(), $"balancia-failures-{Guid.NewGuid():N}.db");

    [Theory]
    [InlineData(19, true, true, false)] // constraint violation
    [InlineData(11, false, true, true)] // corrupt database
    [InlineData(26, false, true, true)] // not a database
    [InlineData(5, false, true, false)] // busy
    [InlineData(6, false, true, false)] // locked
    [InlineData(10, false, true, false)] // I/O error
    public void Classification_SqliteErrorCode_MapsToTheDomainCategory(int code, bool constraint, bool database, bool unreadable)
    {
        // Arrange
        var error = new SqliteException("private detail", code);

        // Act and Assert
        Assert.Equal(constraint, LedgerFailures.IsConstraintViolation(error));
        Assert.Equal(database, LedgerFailures.IsDatabaseFailure(error));
        Assert.Equal(unreadable, LedgerFailures.IsUnreadableDatabase(error));
    }

    [Theory]
    [MemberData(nameof(NonDatabaseErrors))]
    public void Classification_NonDatabaseException_IsNeverADatabaseFailure(Exception error)
    {
        // Act and Assert
        Assert.False(LedgerFailures.IsConstraintViolation(error));
        Assert.False(LedgerFailures.IsDatabaseFailure(error));
        Assert.False(LedgerFailures.IsUnreadableDatabase(error));
    }

    public static TheoryData<Exception> NonDatabaseErrors() =>
    [
        new IOException("disk"),
        new InvalidOperationException("state"),
        new InvalidDataException("data"),
        new ArgumentException("argument"),
    ];

    [Fact]
    public void IsConstraintViolation_FailureRaisedByTheEngineDuringAWrite_IsRecognised()
    {
        // Arrange
        var store = new LedgerStore(path);
        store.Initialize();
        var account = store.SaveAccount(null, "Synthetic", new DateOnly(2026, 1, 1), Money.FromFrancs(100));
        SqliteScript.Execute(path, "CREATE TRIGGER fail_insert BEFORE INSERT ON movements WHEN NEW.transaction_id NOT LIKE 'opening:%' BEGIN SELECT RAISE(ABORT,'injected failure'); END;");

        // Act
        var error = Record.Exception(() => store.SaveTransaction(null,
            new TransactionDraft(TransactionKind.Expense, new DateOnly(2026, 1, 2), "Lunch", Money.FromFrancs(10), account)));

        // Assert
        Assert.NotNull(error);
        Assert.True(LedgerFailures.IsConstraintViolation(error));
        Assert.True(LedgerFailures.IsDatabaseFailure(error));
        Assert.False(LedgerFailures.IsUnreadableDatabase(error));
    }

    [Fact]
    public void IsUnreadableDatabase_FileThatIsNotADatabase_IsRecognised()
    {
        // Arrange
        File.WriteAllText(path, "this is not a sqlite database, only text padding it out to a few hundred bytes. " + new string('x', 400));

        // Act
        var error = Record.Exception(() => LedgerReader.Open(path).ReadIdentity());

        // Assert
        Assert.NotNull(error);
        Assert.True(LedgerFailures.IsUnreadableDatabase(error));
        Assert.True(LedgerFailures.IsDatabaseFailure(error));
        Assert.False(LedgerFailures.IsConstraintViolation(error));
    }

    public void Dispose() => File.Delete(path);
}
