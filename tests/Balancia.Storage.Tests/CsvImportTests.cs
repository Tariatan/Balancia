using System.Text;
using Balancia.Core;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Balancia.Storage.Tests;

public sealed class CsvImportTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "balancia-import-" + Guid.NewGuid().ToString("N"));
    private readonly LedgerStore _store;
    private readonly string _csv;
    private const string Header = "ID,Date,Description,Currency,Amount,Type,Tags,Account,Status,Memo,IOU\n";
    private const string Rows =
        "o1,01-09-26,Opening balance,CHF,100.00,Transfer,,A,Cleared,,\n" +
        "o2,01-09-26,Opening balance,CHF,20.00,Transfer,,B,Cleared,,\n" +
        "x1,02-09-26,\"Meal, with colleague\ncontinued\",CHF,-10.10,Expense,Food / Lunch,A,Cleared,\"note, preserved\",\n" +
        "x2,03-09-26,,CHF,1.20,Income,,A,Cleared,,\n" +
        "t1,04-09-26,Transfer,CHF,-5.00,Transfer,,A,Cleared,,\n" +
        "t1,04-09-26,Transfer,CHF,5.00,Transfer,,B,Cleared,,\n";

    public CsvImportTests()
    {
        Directory.CreateDirectory(_dir);
        _csv = Path.Combine(_dir, "source.csv");
        _store = new LedgerStore(Path.Combine(_dir, "test.db"));
        _store.Initialize();
    }
    private void Csv(string rows) => File.WriteAllText(_csv, Header + rows, new UTF8Encoding(true));

    [Fact]
    public void I01_I02_I03_I07_QuotedTextOpeningsTransfersAndIdempotency()
    {
        Csv(Rows);
        var preview = _store.PreviewCsvImport(_csv);
        Assert.True(preview.CanApply, string.Join("; ", preview.Issues.Select(x => x.Message)));
        Assert.Equal(6, preview.Summary.Rows);
        Assert.Equal(2, preview.Summary.Openings);
        Assert.Equal(1, preview.Summary.Transfers);
        Assert.Equal(5, _store.ApplyCsvImport(preview).Added);
        var s = _store.ReadSnapshot();
        Assert.Equal(11110, s.NetWorth.Centimes);
        Assert.Equal(120, s.MonthlyIncome.Centimes);
        Assert.Equal(1010, s.MonthlyExpenses.Centimes);
        Assert.Equal(3, s.Entries.Count);
        Assert.Contains("\n", s.Entries.Single(e => e.Draft.Kind == TransactionKind.Expense).Draft.Description);
        Assert.Equal("note, preserved", s.Entries.Single(e => e.Draft.Kind == TransactionKind.Expense).Draft.Memo);
        var revision = s.Revision;
        Assert.Equal(0, _store.ApplyCsvImport(_store.PreviewCsvImport(_csv)).Added);
        Assert.Equal(5, _store.ApplyCsvImport(_store.PreviewCsvImport(_csv)).Unchanged);
        Assert.Equal(revision, _store.ReadSnapshot().Revision);
        Csv(Rows.Replace("-10.10", "-11.10"));
        Assert.Throws<InvalidOperationException>(() => _store.ApplyCsvImport(_store.PreviewCsvImport(_csv)));
        Assert.Equal(11110, _store.ReadSnapshot().NetWorth.Centimes);
    }

    [Theory]
    [InlineData("t1,04-09-26,Transfer,CHF,-5.00,Transfer,,A,Cleared,,\n")]
    [InlineData("x1,02-09-26,X,USD,-1.00,Expense,,A,Cleared,,\n")]
    [InlineData("x1,02-09-26,X,CHF,-1.001,Expense,,A,Cleared,,\n")]
    [InlineData("x1,31-02-26,X,CHF,-1.00,Expense,,A,Cleared,,\n")]
    [InlineData("x1,02-09-26,X,CHF,-1.00,Refund,,A,Cleared,,\n")]
    public void I04_InvalidRowsBlockImport(string row)
    {
        Csv(row);
        var preview = _store.PreviewCsvImport(_csv);
        Assert.False(preview.CanApply);
        Assert.Throws<InvalidOperationException>(() => _store.ApplyCsvImport(preview));
        Assert.Empty(_store.ReadSnapshot().Accounts);
    }

    [Fact]
    public void I05_ChangedFileAndFailedBatchLeaveLedgerUntouched()
    {
        Csv(Rows);
        var preview = _store.PreviewCsvImport(_csv);
        File.AppendAllText(_csv, "\n");
        Assert.Throws<InvalidOperationException>(() => _store.ApplyCsvImport(preview));
        Assert.Empty(_store.ReadSnapshot().Accounts);
        Csv(Rows);
        preview = _store.PreviewCsvImport(_csv);
        using (var c = new SqliteConnectionFactory(Path.Combine(_dir, "test.db")).Open())
        using (var cmd = c.CreateCommand())
        {
            cmd.CommandText = "CREATE TRIGGER reject_import BEFORE INSERT ON import_sources WHEN NEW.external_id='t1' BEGIN SELECT RAISE(ABORT,'test rollback'); END;";
            cmd.ExecuteNonQuery();
        }
        Assert.Throws<SqliteException>(() => _store.ApplyCsvImport(preview));
        Assert.Empty(_store.ReadSnapshot().Accounts);
    }

    [Fact]
    public void LocalEditBecomesExplicitReimportConflict()
    {
        Csv(Rows);
        _store.ApplyCsvImport(_store.PreviewCsvImport(_csv));
        var entry = _store.ReadSnapshot().Entries.Single(e => e.Draft.Kind == TransactionKind.Income);
        _store.SaveTransaction(entry.Id, entry.Draft with
        {
            Description = "edited"
        });
        Assert.Throws<InvalidOperationException>(() => _store.ApplyCsvImport(_store.PreviewCsvImport(_csv)));
    }

    [Fact]
    public void PriorProvenanceLabelsStillMatchRepeatedImports()
    {
        Csv(Rows);
        _store.ApplyCsvImport(_store.PreviewCsvImport(_csv));
        using (var c = new SqliteConnectionFactory(Path.Combine(_dir, "test.db")).Open())
        using (var cmd = c.CreateCommand())
        {
            cmd.CommandText = "UPDATE import_sources SET source='LegacySource'";
            cmd.ExecuteNonQuery();
        }

        var result = _store.ApplyCsvImport(_store.PreviewCsvImport(_csv));

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

        var preview = _store.PreviewCsvImport(source);
        Assert.True(preview.CanApply, $"Private export has {preview.Issues.Count} import issues; first line: {preview.Issues.FirstOrDefault()?.Line}.");
        var applied = _store.ApplyCsvImport(preview);
        Assert.Equal(preview.Summary.Expenses + preview.Summary.Incomes + preview.Summary.Transfers + preview.Summary.Openings, applied.Added);
        var accounts = _store.ReadSnapshot().Accounts;
        foreach (var expected in preview.Summary.AccountTotals)
        {
            Assert.Equal(expected.Centimes, accounts.Single(a => a.Name == expected.Account).Balance.Centimes);
        }

        Assert.Equal(0, _store.ApplyCsvImport(_store.PreviewCsvImport(source)).Added);
    }

    [Fact]
    public void V1MigrationCreatesRecoverableBackupAndKeepsBalances()
    {
        var id = _store.SaveAccount(null, "A", new DateOnly(2026, 9, 1), Money.FromFrancs(12));
        var db = Path.Combine(_dir, "test.db");
        using (var c = new SqliteConnectionFactory(db).Open())
        using (var cmd = c.CreateCommand())
        {
            cmd.CommandText = "DROP TABLE import_sources; PRAGMA user_version=1";
            cmd.ExecuteNonQuery();
        }
        _store.Initialize();
        Assert.Equal(1200, _store.ReadSnapshot().Accounts.Single(a => a.Id == id).Balance.Centimes);
        var backup = Assert.Single(Directory.GetFiles(_dir, "test.db.pre-v2-*.bak"));
        using var restored = new SqliteConnectionFactory(backup).Open();
        using var check = restored.CreateCommand();
        check.CommandText = "PRAGMA user_version";
        Assert.Equal(1L, Convert.ToInt64(check.ExecuteScalar()));
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(_dir, true);
    }
}
