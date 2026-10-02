using Balancia.Core;
using Microsoft.VisualBasic.FileIO;
using Xunit;

namespace Balancia.Storage.Tests;

public sealed class CsvExportTests
{
    [Fact]
    public void ExportIncludesOpeningsAndOneRowPerTransactionWithEscapedText()
    {
        var directory = Path.Combine(Path.GetTempPath(), "balancia-csv-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var store = new LedgerStore(Path.Combine(directory, "ledger.db"));
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

            var path = Path.Combine(directory, "export.csv");
            Assert.Equal(5, store.ExportCsv(path));
            using var parser = new TextFieldParser(path);
            parser.HasFieldsEnclosedInQuotes = true;
            parser.SetDelimiters(",");
            var rows = new List<string[]>();
            while (!parser.EndOfData)
            {
                rows.Add(parser.ReadFields()!);
            }

            Assert.Equal(6, rows.Count);
            Assert.Equal(["ID", "Date", "Type", "Description", "Amount", "Account", "DestinationAccount", "Category", "Memo"], rows[0]);
            Assert.Equal(2, rows.Count(row => row[2] == "OpeningBalance"));
            Assert.Contains(rows, row => row[2] == "Expense" && row[3] == "Bread, \"fresh\"\nloaf" &&
                row[4] == "-12.34" && row[7] == "Food" && row[8] == "first\nsecond");
            Assert.Contains(rows, row => row[2] == "Income" && row[4] == "5.00");
            Assert.Single(rows, row => row[2] == "Transfer" && row[4] == "20.00" &&
                row[5] == "Everyday" && row[6] == "Savings");

            // Act: import the actual exported file into a fresh ledger.
            var restored = new LedgerStore(Path.Combine(directory, "restored.db"));
            restored.Initialize();
            var preview = restored.PreviewCsvImport(path);
            Assert.True(preview.CanApply, string.Join("; ", preview.Issues.Select(issue => issue.Message)));
            Assert.Equal(5, restored.ApplyCsvImport(preview).Added);

            // Assert: opening balances, zero openings, transfer direction, and text survive.
            var original = store.ReadSnapshot();
            var imported = restored.ReadSnapshot();
            Assert.Equal(original.NetWorth, imported.NetWorth);
            Assert.Equal(original.Accounts.Select(account => (account.Name, account.OpeningDate, account.OpeningAmount, account.Balance)).OrderBy(account => account.Name),
                imported.Accounts.Select(account => (account.Name, account.OpeningDate, account.OpeningAmount, account.Balance)).OrderBy(account => account.Name));
            Assert.Equal(original.Entries.Select(entry => (entry.Draft.Kind, entry.Draft.Date, entry.Draft.Description,
                    entry.Draft.Amount, entry.AccountName, entry.DestinationName, entry.CategoryPath, entry.Draft.Memo)).OrderBy(entry => entry.Date),
                imported.Entries.Select(entry => (entry.Draft.Kind, entry.Draft.Date, entry.Draft.Description,
                    entry.Draft.Amount, entry.AccountName, entry.DestinationName, entry.CategoryPath, entry.Draft.Memo)).OrderBy(entry => entry.Date));
            var revision = imported.Revision;
            Assert.Equal(new ImportResult(0, 5), restored.ApplyCsvImport(restored.PreviewCsvImport(path)));
            Assert.Equal(revision, restored.ReadSnapshot().Revision);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }
}
