using Balancia.Core;
using Microsoft.VisualBasic.FileIO;
using Xunit;

namespace Balancia.Storage.Tests;

public sealed class CsvExportTests : IDisposable
{
    private static readonly string[] ExpectedHeader =
        ["ID", "Date", "Type", "Description", "Amount", "Account", "DestinationAccount", "Category", "Memo"];

    private readonly string directory = Path.Combine(Path.GetTempPath(), "balancia-csv-" + Guid.NewGuid().ToString("N"));
    private readonly LedgerStore store;
    private readonly string exportPath;

    public CsvExportTests()
    {
        Directory.CreateDirectory(directory);
        exportPath = Path.Combine(directory, "export.csv");
        store = new LedgerStore(Path.Combine(directory, "ledger.db"));
        store.Initialize();
        var from = store.SaveAccount(null, "Everyday", new DateOnly(2026, 1, 1), Money.FromFrancs(100));
        var to = store.SaveAccount(null, "Savings", new DateOnly(2026, 1, 1), new Money(0));
        var category = store.SaveCategory(null, "Food", null);
        store.SaveTransaction(null, new TransactionDraft(TransactionKind.Expense, new DateOnly(2026, 1, 2),
            "Bread, \"fresh\"\nloaf", Money.FromFrancs(12.34m), from, CategoryId: category, Memo: "first\nsecond"));
        store.SaveTransaction(null, new TransactionDraft(TransactionKind.Income, new DateOnly(2026, 1, 3),
            "Refund", Money.FromFrancs(5), from));
        store.SaveTransaction(null, new TransactionDraft(TransactionKind.Transfer, new DateOnly(2026, 1, 4),
            "Move", Money.FromFrancs(20), from, to));
    }

    [Fact]
    public void ExportCsv_LedgerWithAllKinds_WritesHeaderOpeningsAndOneRowPerTransaction()
    {
        // Arrange
        // Fixture: two openings, one expense, one income and one transfer.

        // Act
        var written = store.ExportCsv(exportPath);
        var rows = ReadRows(exportPath);

        // Assert
        Assert.Equal(5, written);
        Assert.Equal(6, rows.Count);
        Assert.Equal(ExpectedHeader, rows[0]);
        Assert.Equal(2, rows.Count(row => row[2] == "OpeningBalance"));
        Assert.Contains(rows, row => row[2] == "Expense" && row[3] == "Bread, \"fresh\"\nloaf" &&
            row[4] == "-12.34" && row[7] == "Food" && row[8] == "first\nsecond");
        Assert.Contains(rows, row => row[2] == "Income" && row[4] == "5.00");
        Assert.Single(rows, row => row[2] == "Transfer" && row[4] == "20.00" &&
            row[5] == "Everyday" && row[6] == "Savings");
    }

    [Fact]
    public void ApplyCsvImport_ExportedFile_RestoresBalancesEntriesAndTextInFreshLedger()
    {
        // Arrange
        store.ExportCsv(exportPath);
        var restored = new LedgerStore(Path.Combine(directory, "restored.db"));
        restored.Initialize();

        // Act
        var preview = restored.PreviewCsvImport(exportPath);
        var result = restored.ApplyCsvImport(preview);

        // Assert
        Assert.True(preview.CanApply, string.Join("; ", preview.Issues.Select(issue => issue.Message)));
        Assert.Equal(5, result.Imported);
        var original = store.ReadSnapshot();
        var imported = restored.ReadSnapshot();
        Assert.Equal(original.NetWorth, imported.NetWorth);
        Assert.Equal(original.Accounts.Select(AccountKey).OrderBy(account => account.Name),
            imported.Accounts.Select(AccountKey).OrderBy(account => account.Name));
        Assert.Equal(original.Entries.Select(EntryKey).OrderBy(entry => entry.Date),
            imported.Entries.Select(EntryKey).OrderBy(entry => entry.Date));
    }

    [Fact]
    public void ApplyCsvImport_ExportedFileImportedTwice_LeavesTheSameLedger()
    {
        // Arrange
        store.ExportCsv(exportPath);
        var restored = new LedgerStore(Path.Combine(directory, "restored.db"));
        restored.Initialize();
        restored.ApplyCsvImport(restored.PreviewCsvImport(exportPath));
        var first = restored.ReadSnapshot();

        // Act
        var second = restored.ApplyCsvImport(restored.PreviewCsvImport(exportPath));

        // Assert
        Assert.Equal(5, second.Imported);
        Assert.NotNull(second.RecoveryCopyPath);
        var again = restored.ReadSnapshot();
        Assert.Equal(first.NetWorth, again.NetWorth);
        Assert.Equal(first.Accounts.Select(AccountKey).OrderBy(account => account.Name), again.Accounts.Select(AccountKey).OrderBy(account => account.Name));
        Assert.Equal(first.Entries.Select(EntryKey).OrderBy(entry => entry.Date), again.Entries.Select(EntryKey).OrderBy(entry => entry.Date));
    }

    private static List<string[]> ReadRows(string path)
    {
        using var parser = new TextFieldParser(path);
        parser.HasFieldsEnclosedInQuotes = true;
        parser.SetDelimiters(",");
        var rows = new List<string[]>();
        while (!parser.EndOfData)
        {
            rows.Add(parser.ReadFields()!);
        }

        return rows;
    }

    private static (string Name, DateOnly OpeningDate, Money OpeningAmount, Money Balance) AccountKey(Account account) =>
        (account.Name, account.OpeningDate, account.OpeningAmount, account.Balance);

    private static (TransactionKind Kind, DateOnly Date, string Description, Money Amount, string AccountName,
        string? DestinationName, string? CategoryPath, string? Memo) EntryKey(LedgerEntry entry) =>
        (entry.Draft.Kind, entry.Draft.Date, entry.Draft.Description, entry.Draft.Amount, entry.AccountName,
            entry.DestinationName, entry.CategoryPath, entry.Draft.Memo);

    public void Dispose() => Directory.Delete(directory, recursive: true);
}
