using System.Text;
using Balancia.Core;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Balancia.Storage.Tests;

public sealed class CsvImportTests : IDisposable
{
    private readonly string dir = Path.Combine(Path.GetTempPath(), "balancia-import-" + Guid.NewGuid().ToString("N"));
    private readonly LedgerStore store;
    private readonly string csv;
    private const string Header = "ID,Date,Type,Description,Amount,Account,DestinationAccount,Category,Memo\n";
    private const string Rows =
        "o1,2026-09-01,OpeningBalance,Opening balance,100.00,A,,,\n" +
        "o2,2026-09-01,OpeningBalance,Opening balance,20.00,B,,,\n" +
        "x1,2026-09-02,Expense,\"Meal, with colleague\ncontinued\",-10.10,A,,Food / Lunch,\"note, preserved\"\n" +
        "x2,2026-09-03,Income,,1.20,A,,,\n" +
        "t1,2026-09-04,Transfer,Transfer,5.00,A,B,,\n";

    public CsvImportTests()
    {
        Directory.CreateDirectory(dir);
        csv = Path.Combine(dir, "source.csv");
        store = new LedgerStore(Path.Combine(dir, "test.db"), new FixedClock());
        store.Initialize();
    }
    private void Csv(string rows) => File.WriteAllText(csv, Header + rows, new UTF8Encoding(true));

    [Fact]
    public void PreviewCsvImport_LegacyHeader_BlocksImportWithoutWrites()
    {
        // Arrange
        File.WriteAllText(csv, "ID,Date,Description,Currency,Amount,Type,Tags,Account,Status,Memo,IOU\n" +
            "o1,01-09-26,Opening balance,CHF,100.00,Transfer,,A,Cleared,,\n");

        // Act
        var preview = store.PreviewCsvImport(csv);

        // Assert
        Assert.False(preview.CanApply);
        Assert.Throws<InvalidOperationException>(() => store.ApplyCsvImport(preview));
        Assert.Empty(store.ReadSnapshot().Accounts);
    }

    [Fact]
    public void ApplyCsvImport_NegativeOpeningBalance_PreservesBalance()
    {
        // Arrange
        Csv("o1,2026-09-01,OpeningBalance,Opening balance,-12.34,A,,,\n");

        // Act
        store.ApplyCsvImport(store.PreviewCsvImport(csv));

        // Assert
        Assert.Equal(-1234, store.ReadSnapshot().NetWorth.Centimes);
    }

    [Fact]
    public void PreviewCsvImport_DuplicateExportId_BlocksImportWithoutWrites()
    {
        // Arrange
        Csv(Rows + "x2,2026-09-05,Income,Duplicate,1.20,A,,,\n");

        // Act
        var preview = store.PreviewCsvImport(csv);

        // Assert
        Assert.False(preview.CanApply);
        Assert.Contains(preview.Issues, issue => issue.Message.Contains("ID must occur exactly once"));
        Assert.Throws<InvalidOperationException>(() => store.ApplyCsvImport(preview));
        Assert.Empty(store.ReadSnapshot().Accounts);
    }

    [Fact]
    public void I01_I02_I03_I07_QuotedTextOpeningsTransfersAndIdempotency()
    {
        Csv(Rows);
        var preview = store.PreviewCsvImport(csv);
        Assert.True(preview.CanApply, string.Join("; ", preview.Issues.Select(x => x.Message)));
        Assert.Equal(5, preview.Summary.Rows);
        Assert.Equal(2, preview.Summary.Openings);
        Assert.Equal(1, preview.Summary.Transfers);
        Assert.Equal(5, store.ApplyCsvImport(preview).Added);
        var s = store.ReadSnapshot();
        Assert.Equal(11110, s.NetWorth.Centimes);
        Assert.Equal(120, s.MonthlyIncome.Centimes);
        Assert.Equal(1010, s.MonthlyExpenses.Centimes);
        Assert.Equal(3, s.Entries.Count);
        Assert.Contains("\n", s.Entries.Single(e => e.Draft.Kind == TransactionKind.Expense).Draft.Description);
        Assert.Equal("note, preserved", s.Entries.Single(e => e.Draft.Kind == TransactionKind.Expense).Draft.Memo);
        var revision = s.Revision;
        Assert.Equal(0, store.ApplyCsvImport(store.PreviewCsvImport(csv)).Added);
        Assert.Equal(5, store.ApplyCsvImport(store.PreviewCsvImport(csv)).Unchanged);
        Assert.Equal(revision, store.ReadSnapshot().Revision);
        Csv(Rows.Replace("-10.10", "-11.10"));
        Assert.Throws<InvalidOperationException>(() => store.ApplyCsvImport(store.PreviewCsvImport(csv)));
        Assert.Equal(11110, store.ReadSnapshot().NetWorth.Centimes);
    }

    [Theory]
    [InlineData("t1,2026-09-04,Transfer,Transfer,5.00,A,,,\n")]
    [InlineData("t1,2026-09-04,Transfer,Transfer,5.00,A,a,,\n")]
    [InlineData("x1,2026-09-02,Expense,X,-1.001,A,,,\n")]
    [InlineData("x1,2026-02-31,Expense,X,-1.00,A,,,\n")]
    [InlineData("x1,02-09-26,Expense,X,-1.00,A,,,\n")]
    [InlineData("x1,2026-09-02,Refund,X,-1.00,A,,,\n")]
    [InlineData(",2026-09-02,Expense,X,-1.00,A,,,\n")]
    [InlineData("x1,2026-09-02,Expense,X,0.00,A,,,\n")]
    [InlineData("x1,2026-09-02,Expense,X,-1.00,,,,\n")]
    [InlineData("x1,2026-09-02,Expense,X,-1.00,A,B,,\n")]
    [InlineData("x1,2026-09-02,Expense,X,-1.00,A,,A/B/C,\n")]
    [InlineData("x1,2026-09-02,Expense,X,-1.00,A,,A/,\n")]
    [InlineData("x1,2026-09-02,Income,X,79228162514264337593543950335,A,,,\n")]
    public void I04_InvalidRowsBlockImport(string row)
    {
        Csv(row);
        var preview = store.PreviewCsvImport(csv);
        Assert.False(preview.CanApply);
        Assert.Throws<InvalidOperationException>(() => store.ApplyCsvImport(preview));
        Assert.Empty(store.ReadSnapshot().Accounts);
    }

    [Fact]
    public void UnexpectedHeaderBlocksImport()
    {
        File.WriteAllText(csv, "ID,Date,Description\nx1,02-09-26,X\n");
        var preview = store.PreviewCsvImport(csv);
        Assert.False(preview.CanApply);
        Assert.Contains(preview.Issues, i => i.Line == 1 && i.Message.Contains("9 Balancia export columns"));
    }

    [Fact]
    public void WrongColumnCountBlocksImport()
    {
        Csv("x1,2026-09-02,Expense,X,-1.00,A,,\n");
        var preview = store.PreviewCsvImport(csv);
        Assert.False(preview.CanApply);
        Assert.Contains(preview.Issues, i => i.Message.Contains("Expected 9 columns"));
    }

    [Fact]
    public void MalformedQuotingBlocksImport()
    {
        Csv("x1,2026-09-02,Expense,\"unterminated,-1.00,A,,,\n");
        var preview = store.PreviewCsvImport(csv);
        Assert.False(preview.CanApply);
        Assert.Contains(preview.Issues, i => i.Message.Contains("Malformed CSV quoting"));
    }

    [Fact]
    public void CategorizedOpeningBalanceBlocksImport()
    {
        Csv("o1,2026-09-01,OpeningBalance,Opening balance,100.00,A,,Cash,\n");
        var preview = store.PreviewCsvImport(csv);
        Assert.False(preview.CanApply);
        Assert.Contains(preview.Issues, i => i.Message.Contains("cannot have a category"));
    }

    [Fact]
    public void CategorizedTransferBlocksImport()
    {
        Csv("t1,2026-09-04,Transfer,Transfer,5.00,A,B,Cash,\n");
        var preview = store.PreviewCsvImport(csv);
        Assert.False(preview.CanApply);
        Assert.Contains(preview.Issues, i => i.Message.Contains("cannot have a category"));
    }

    [Fact]
    public void SummaryListsDistinctSortedCategoryPaths()
    {
        Csv("o1,2026-09-01,OpeningBalance,Opening balance,100.00,A,,,\n" +
            "x1,2026-09-02,Expense,X,-1.00,A,,Food / Lunch,\n" +
            "x2,2026-09-03,Expense,Y,-2.00,A,,food / dinner,\n" +
            "x3,2026-09-04,Expense,Z,-3.00,A,,Transport,\n");
        var preview = store.PreviewCsvImport(csv);
        Assert.True(preview.CanApply, string.Join("; ", preview.Issues.Select(x => x.Message)));
        Assert.Equal(["food / dinner", "Food / Lunch", "Transport"], preview.Summary.Categories);
    }

    [Fact]
    public void ArchivedCategoryBlocksImport()
    {
        var categoryId = store.SaveCategory(null, "Groceries", null);
        store.SaveCategory(categoryId, "Groceries", null, archived: true);
        Csv("o1,2026-09-01,OpeningBalance,Opening balance,100.00,A,,,\n" +
            "x1,2026-09-02,Expense,X,-1.00,A,,Groceries,\n");
        var preview = store.PreviewCsvImport(csv);
        Assert.True(preview.CanApply);
        var error = Assert.Throws<InvalidOperationException>(() => store.ApplyCsvImport(preview));
        Assert.Contains("archived", error.Message);
        Assert.Empty(store.ReadSnapshot().Accounts);
    }

    [Fact]
    public void DuplicateOpeningBalancesForOneAccountAreFlagged()
    {
        Csv("o1,2026-09-01,OpeningBalance,Opening balance,100.00,A,,,\n" +
            "o1b,2026-09-01,OpeningBalance,Opening balance,50.00,A,,,\n");
        var preview = store.PreviewCsvImport(csv);
        Assert.False(preview.CanApply);
        Assert.All(preview.Issues, issue => Assert.Contains("exactly one OpeningBalance", issue.Message));
        Assert.Single(preview.Issues);
    }

    [Fact]
    public void I05_ChangedFileAndFailedBatchLeaveLedgerUntouched()
    {
        Csv(Rows);
        var preview = store.PreviewCsvImport(csv);
        File.AppendAllText(csv, "\n");
        Assert.Throws<InvalidOperationException>(() => store.ApplyCsvImport(preview));
        Assert.Empty(store.ReadSnapshot().Accounts);
        Csv(Rows);
        preview = store.PreviewCsvImport(csv);
        using (var c = new SqliteConnectionFactory(Path.Combine(dir, "test.db")).Open())
        using (var cmd = c.CreateCommand())
        {
            cmd.CommandText = "CREATE TRIGGER reject_import BEFORE INSERT ON import_sources WHEN NEW.external_id='t1' BEGIN SELECT RAISE(ABORT,'test rollback'); END;";
            cmd.ExecuteNonQuery();
        }
        Assert.Throws<SqliteException>(() => store.ApplyCsvImport(preview));
        Assert.Empty(store.ReadSnapshot().Accounts);
    }

    [Fact]
    public void LocalEditBecomesExplicitReimportConflict()
    {
        Csv(Rows);
        store.ApplyCsvImport(store.PreviewCsvImport(csv));
        var entry = store.ReadSnapshot().Entries.Single(e => e.Draft.Kind == TransactionKind.Income);
        store.SaveTransaction(entry.Id, entry.Draft with
        {
            Description = "edited"
        });
        Assert.Throws<InvalidOperationException>(() => store.ApplyCsvImport(store.PreviewCsvImport(csv)));
    }

    [Fact]
    public void PriorProvenanceLabelsStillMatchRepeatedImports()
    {
        Csv(Rows);
        store.ApplyCsvImport(store.PreviewCsvImport(csv));
        using (var c = new SqliteConnectionFactory(Path.Combine(dir, "test.db")).Open())
        using (var cmd = c.CreateCommand())
        {
            cmd.CommandText = "UPDATE import_sources SET source='LegacySource'";
            cmd.ExecuteNonQuery();
        }

        var result = store.ApplyCsvImport(store.PreviewCsvImport(csv));

        Assert.Equal(0, result.Added);
        Assert.Equal(5, result.Unchanged);
    }

    [Fact]
    public void I06_PrivateExportReconcilesWhenExplicitlyProvided()
    {
        var source = Environment.GetEnvironmentVariable("BALANCIA_PRIVATE_IMPORT_PATH");
        if (source is null)
        {
            return;
        }

        var preview = store.PreviewCsvImport(source);
        Assert.True(preview.CanApply, $"Private export has {preview.Issues.Count} import issues; first line: {preview.Issues.FirstOrDefault()?.Line}.");
        var applied = store.ApplyCsvImport(preview);
        Assert.Equal(preview.Summary.Expenses + preview.Summary.Incomes + preview.Summary.Transfers + preview.Summary.Openings, applied.Added);
        var accounts = store.ReadSnapshot().Accounts;
        foreach (var expected in preview.Summary.AccountTotals)
        {
            Assert.Equal(expected.Centimes, accounts.Single(a => a.Name == expected.Account).Balance.Centimes);
        }

        Assert.Equal(0, store.ApplyCsvImport(store.PreviewCsvImport(source)).Added);
    }

    [Fact]
    public void V1MigrationCreatesRecoverableBackupAndKeepsBalances()
    {
        var id = store.SaveAccount(null, "A", new DateOnly(2026, 9, 1), Money.FromFrancs(12));
        var db = Path.Combine(dir, "test.db");
        using (var c = new SqliteConnectionFactory(db).Open())
        using (var cmd = c.CreateCommand())
        {
            cmd.CommandText = "DROP TABLE import_sources; DROP TABLE reminder_templates; PRAGMA user_version=1";
            cmd.ExecuteNonQuery();
        }
        store.Initialize();
        Assert.Equal(1200, store.ReadSnapshot().Accounts.Single(a => a.Id == id).Balance.Centimes);
        var backup = Assert.Single(Directory.GetFiles(dir, "test.db.pre-v2-*.bak"));
        using var restored = new SqliteConnectionFactory(backup).Open();
        using var check = restored.CreateCommand();
        check.CommandText = "PRAGMA user_version";
        Assert.Equal(1L, Convert.ToInt64(check.ExecuteScalar()));
    }

    [Fact]
    public void V1MigrationReachesCurrentSchemaInOneInitializeCall()
    {
        var db = Path.Combine(dir, "test.db");
        using (var c = new SqliteConnectionFactory(db).Open())
        using (var cmd = c.CreateCommand())
        {
            cmd.CommandText = "DROP TABLE import_sources; DROP TABLE reminder_templates; PRAGMA user_version=1";
            cmd.ExecuteNonQuery();
        }
        store.Initialize();
        store.SaveReminder(null, "Rent", new DateOnly(2026, 10, 1), Money.FromFrancs(1), 1);
        using var c2 = new SqliteConnectionFactory(db).Open();
        using var check = c2.CreateCommand();
        check.CommandText = "PRAGMA user_version";
        Assert.Equal(3L, Convert.ToInt64(check.ExecuteScalar()));
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(dir, true);
    }

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 9, 30, 12, 0, 0, TimeSpan.Zero);
    }
}
