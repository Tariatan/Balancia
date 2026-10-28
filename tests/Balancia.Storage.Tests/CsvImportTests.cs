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
    private string DatabasePath => Path.Combine(dir, "test.db");
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
        store = new LedgerStore(DatabasePath, new TestClock(new DateTimeOffset(2026, 9, 30, 12, 0, 0, TimeSpan.Zero)));
        store.Initialize();
    }

    private void Csv(string rows) => File.WriteAllText(csv, Header + rows, new UTF8Encoding(true));

    [Theory]
    [InlineData("x1,2026-10-01,Expense,Rent,-10.00,A,,,\n")]
    [InlineData("o2,2026-10-01,OpeningBalance,Opening balance,5.00,B,,,\n")]
    public void PreviewCsvImport_FutureDate_BlocksImportWithoutWrites(string row)
    {
        // Arrange
        Csv("o1,2026-09-01,OpeningBalance,Opening balance,100.00,A,,,\n" + row);

        // Act
        var preview = store.PreviewCsvImport(csv);

        // Assert
        Assert.False(preview.CanApply);
        Assert.Contains(preview.Issues, issue => issue.Message == "Date cannot be in the future.");
        Assert.Empty(store.ReadSnapshot().Accounts);
    }

    [Theory]
    [InlineData("A / B / C")]
    [InlineData("A / ")]
    [InlineData("/ B")]
    [InlineData(" ")]
    public void PreviewCsvImport_InvalidCategoryPath_ReportsThePathRule(string category)
    {
        // Arrange
        Csv("o1,2026-09-01,OpeningBalance,Opening balance,100.00,A,,,\n" +
            $"x1,2026-09-02,Expense,Rent,-10.00,A,,{category},\n");

        // Act
        var preview = store.PreviewCsvImport(csv);

        // Assert
        Assert.False(preview.CanApply);
        Assert.Contains(preview.Issues, issue => issue.Message == CategoryRules.InvalidPathMessage);
    }

    [Fact]
    public void ApplyCsvImport_ImportedAccountDeleted_ReimportReportsConflictInsteadOfSilentlySkipping()
    {
        // Arrange
        Csv("o1,2026-09-01,OpeningBalance,Opening balance,100.00,A,,,\n");
        store.ApplyCsvImport(store.PreviewCsvImport(csv));
        store.DeleteAccount(store.ReadSnapshot().Accounts.Single().Id);

        // Act
        void Act() => store.ApplyCsvImport(store.PreviewCsvImport(csv));

        // Assert
        Assert.Throws<CsvImportConflictException>(Act);
        Assert.Empty(store.ReadSnapshot().Accounts);
    }

    [Fact]
    public void PreviewCsvImport_DescriptionWithSurroundingSpaces_ImportsTheTrimmedText()
    {
        // Arrange
        Csv("o1,2026-09-01,OpeningBalance,Opening balance,100.00,A,,,\nx1,2026-09-02,Expense,  Rent ,-10.00,A,,,\n");

        // Act
        store.ApplyCsvImport(store.PreviewCsvImport(csv));

        // Assert
        Assert.Equal("Rent", store.ReadSnapshot().Entries.Single().Draft.Description);
    }

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
        Assert.Throws<CsvImportConflictException>(() => store.ApplyCsvImport(preview));
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
        Assert.Throws<CsvImportConflictException>(() => store.ApplyCsvImport(preview));
        Assert.Empty(store.ReadSnapshot().Accounts);
    }

    [Fact]
    public void PreviewCsvImport_ValidExport_SummarizesRowsByKind()
    {
        // Arrange
        Csv(Rows);

        // Act
        var preview = store.PreviewCsvImport(csv);

        // Assert
        Assert.True(preview.CanApply, string.Join("; ", preview.Issues.Select(x => x.Message)));
        Assert.Equal(5, preview.Summary.Rows);
        Assert.Equal(2, preview.Summary.Openings);
        Assert.Equal(1, preview.Summary.Transfers);
    }

    [Fact]
    public void ApplyCsvImport_ValidExport_ImportsBalancesTotalsAndMultilineText()
    {
        // Arrange
        Csv(Rows);
        var preview = store.PreviewCsvImport(csv);

        // Act
        var result = store.ApplyCsvImport(preview);

        // Assert
        var snapshot = store.ReadSnapshot();
        var expense = snapshot.Entries.Single(e => e.Draft.Kind == TransactionKind.Expense);
        Assert.Equal(5, result.Added);
        Assert.Equal(11110, snapshot.NetWorth.Centimes);
        Assert.Equal(120, snapshot.MonthlyIncome.Centimes);
        Assert.Equal(1010, snapshot.MonthlyExpenses.Centimes);
        Assert.Equal(3, snapshot.Entries.Count);
        Assert.Contains("\n", expense.Draft.Description);
        Assert.Equal("note, preserved", expense.Draft.Memo);
    }

    [Fact]
    public void ApplyCsvImport_SameFileTwice_AddsNothingAndKeepsRevision()
    {
        // Arrange
        Csv(Rows);
        store.ApplyCsvImport(store.PreviewCsvImport(csv));
        var revision = store.ReadSnapshot().Revision;

        // Act
        var second = store.ApplyCsvImport(store.PreviewCsvImport(csv));

        // Assert
        Assert.Equal(0, second.Added);
        Assert.Equal(5, second.Unchanged);
        Assert.Equal(revision, store.ReadSnapshot().Revision);
    }

    [Fact]
    public void ApplyCsvImport_KnownIdWithChangedAmount_ThrowsAndKeepsNetWorth()
    {
        // Arrange
        Csv(Rows);
        store.ApplyCsvImport(store.PreviewCsvImport(csv));
        Csv(Rows.Replace("-10.10", "-11.10"));
        var preview = store.PreviewCsvImport(csv);

        // Act
        void Act() => store.ApplyCsvImport(preview);

        // Assert
        Assert.Throws<CsvImportConflictException>(Act);
        Assert.Equal(11110, store.ReadSnapshot().NetWorth.Centimes);
    }

    [Theory]
    [InlineData("1", "1.00")]
    [InlineData("Expense,Income", "1.00")]
    [InlineData(" Expense", "-1.00")]
    [InlineData("expense", "-1.00")]
    public void PreviewCsvImport_NonExactType_ReportsUnsupportedType(string type, string amount)
    {
        // Arrange
        Csv($"o1,2026-09-01,OpeningBalance,Opening balance,10.00,A,,,\nx1,2026-09-02,\"{type}\",X,{amount},A,,,\n");

        // Act
        var preview = store.PreviewCsvImport(csv);

        // Assert
        Assert.False(preview.CanApply);
        Assert.Contains(preview.Issues, issue => issue.Line == 3 && issue.Message == "Unsupported transaction type.");
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
    public void ApplyCsvImport_InvalidRow_BlocksImportWithoutWrites(string row)
    {
        // Arrange
        Csv(row);
        var preview = store.PreviewCsvImport(csv);

        // Act
        void Act() => store.ApplyCsvImport(preview);

        // Assert
        Assert.False(preview.CanApply);
        Assert.Throws<CsvImportConflictException>(Act);
        Assert.Empty(store.ReadSnapshot().Accounts);
    }

    [Fact]
    public void PreviewCsvImport_UnexpectedHeader_ReportsColumnCountOnFirstLine()
    {
        // Arrange
        File.WriteAllText(csv, "ID,Date,Description\nx1,02-09-26,X\n");

        // Act
        var preview = store.PreviewCsvImport(csv);

        // Assert
        Assert.False(preview.CanApply);
        Assert.Contains(preview.Issues, i => i.Line == 1 && i.Message.Contains("9 Balancia export columns"));
    }

    [Fact]
    public void PreviewCsvImport_WrongColumnCount_BlocksImport()
    {
        // Arrange
        Csv("x1,2026-09-02,Expense,X,-1.00,A,,\n");

        // Act
        var preview = store.PreviewCsvImport(csv);

        // Assert
        Assert.False(preview.CanApply);
        Assert.Contains(preview.Issues, i => i.Message.Contains("Expected 9 columns"));
    }

    [Fact]
    public void PreviewCsvImport_MalformedQuoting_BlocksImport()
    {
        // Arrange
        Csv("x1,2026-09-02,Expense,\"unterminated,-1.00,A,,,\n");

        // Act
        var preview = store.PreviewCsvImport(csv);

        // Assert
        Assert.False(preview.CanApply);
        Assert.Contains(preview.Issues, i => i.Message.Contains("Malformed CSV quoting"));
    }

    [Theory]
    [InlineData("o1,2026-09-01,OpeningBalance,Opening balance,100.00,A,,Cash,\n")]
    [InlineData("t1,2026-09-04,Transfer,Transfer,5.00,A,B,Cash,\n")]
    public void PreviewCsvImport_CategoryOnOpeningOrTransfer_BlocksImport(string row)
    {
        // Arrange
        Csv(row);

        // Act
        var preview = store.PreviewCsvImport(csv);

        // Assert
        Assert.False(preview.CanApply);
        Assert.Contains(preview.Issues, i => i.Message.Contains("cannot have a category"));
    }

    [Fact]
    public void PreviewCsvImport_MixedCaseCategoryPaths_ListsDistinctSortedPaths()
    {
        // Arrange
        Csv("o1,2026-09-01,OpeningBalance,Opening balance,100.00,A,,,\n" +
            "x1,2026-09-02,Expense,X,-1.00,A,,Food / Lunch,\n" +
            "x2,2026-09-03,Expense,Y,-2.00,A,,food / dinner,\n" +
            "x3,2026-09-04,Expense,Z,-3.00,A,,Transport,\n");

        // Act
        var preview = store.PreviewCsvImport(csv);

        // Assert
        Assert.True(preview.CanApply, string.Join("; ", preview.Issues.Select(x => x.Message)));
        Assert.Equal(["food / dinner", "Food / Lunch", "Transport"], preview.Summary.Categories);
    }

    private const string OpeningRow = "o1,2026-09-01,OpeningBalance,Opening balance,100.00,A,,,\n";

    [Fact]
    public void PreviewCsvImport_SpellingVariantsOfOnePath_ListsItOnce()
    {
        // Arrange
        Csv(OpeningRow +
            "x1,2026-09-02,Expense,X,-1.00,A,,Food/Lunch,\n" +
            "x2,2026-09-03,Expense,Y,-2.00,A,,Food /Lunch,\n" +
            "x3,2026-09-04,Expense,Z,-3.00,A,,food / lunch,\n");

        // Act
        var preview = store.PreviewCsvImport(csv);

        // Assert
        Assert.True(preview.CanApply, string.Join("; ", preview.Issues.Select(x => x.Message)));
        Assert.Equal(["Food / Lunch"], preview.Summary.Categories);
    }

    [Fact]
    public void ApplyCsvImport_SpellingVariantsOfOnePath_ShareOneCategory()
    {
        // Arrange
        Csv(OpeningRow +
            "x1,2026-09-02,Expense,X,-1.00,A,,Food/Lunch,\n" +
            "x2,2026-09-03,Expense,Y,-2.00,A,,Food /Lunch,\n" +
            "x3,2026-09-04,Expense,Z,-3.00,A,,food / LUNCH,\n");

        // Act
        store.ApplyCsvImport(store.PreviewCsvImport(csv));

        // Assert
        var snapshot = store.ReadSnapshot();
        Assert.Equal(["Food", "Food / Lunch"], snapshot.Categories.Select(c => c.Path).Order());
        Assert.Single(snapshot.Entries.Select(e => e.Draft.CategoryId).Distinct());
    }

    [Fact]
    public void ApplyCsvImport_CategoryDifferingOnlyByANonAsciiLetterCase_UsesTheExistingCategory()
    {
        // Arrange
        var existingId = store.SaveCategory(null, "Épicerie", null);
        Csv(OpeningRow + "x1,2026-09-02,Expense,X,-1.00,A,,épicerie,\n");

        // Act
        store.ApplyCsvImport(store.PreviewCsvImport(csv));

        // Assert
        var snapshot = store.ReadSnapshot();
        Assert.Equal("Épicerie", Assert.Single(snapshot.Categories).Path);
        Assert.Equal(existingId, Assert.Single(snapshot.Entries).Draft.CategoryId);
    }

    [Fact]
    public void ApplyCsvImport_NewSubcategoryUnderAnExistingParent_CreatesOnlyTheSubcategory()
    {
        // Arrange
        var parentId = store.SaveCategory(null, "Food", null);
        Csv(OpeningRow + "x1,2026-09-02,Expense,X,-1.00,A,,FOOD/Lunch,\n");

        // Act
        store.ApplyCsvImport(store.PreviewCsvImport(csv));

        // Assert
        var categories = store.ReadSnapshot().Categories;
        Assert.Equal(2, categories.Count);
        Assert.Equal(parentId, categories.Single(c => c.Path == "Food / Lunch").ParentId);
    }

    [Fact]
    public void ApplyCsvImport_ArchivedCategory_ThrowsAndWritesNothing()
    {
        // Arrange
        var categoryId = store.SaveCategory(null, "Groceries", null);
        store.SaveCategory(categoryId, "Groceries", null, archived: true);
        Csv("o1,2026-09-01,OpeningBalance,Opening balance,100.00,A,,,\n" +
            "x1,2026-09-02,Expense,X,-1.00,A,,Groceries,\n");
        var preview = store.PreviewCsvImport(csv);

        // Act
        var error = Assert.Throws<CsvImportConflictException>(() => store.ApplyCsvImport(preview));

        // Assert
        Assert.True(preview.CanApply);
        Assert.Contains("archived", error.Message);
        Assert.Empty(store.ReadSnapshot().Accounts);
    }

    [Fact]
    public void PreviewCsvImport_TwoOpeningBalancesForOneAccount_FlagsSingleIssue()
    {
        // Arrange
        Csv("o1,2026-09-01,OpeningBalance,Opening balance,100.00,A,,,\n" +
            "o1b,2026-09-01,OpeningBalance,Opening balance,50.00,A,,,\n");

        // Act
        var preview = store.PreviewCsvImport(csv);

        // Assert
        Assert.False(preview.CanApply);
        Assert.All(preview.Issues, issue => Assert.Contains("exactly one OpeningBalance", issue.Message));
        Assert.Single(preview.Issues);
    }

    [Fact]
    public void ApplyCsvImport_FileChangedAfterPreview_ThrowsAndWritesNothing()
    {
        // Arrange
        Csv(Rows);
        var preview = store.PreviewCsvImport(csv);
        File.AppendAllText(csv, "\n");

        // Act
        void Act() => store.ApplyCsvImport(preview);

        // Assert
        Assert.Throws<CsvImportConflictException>(Act);
        Assert.Empty(store.ReadSnapshot().Accounts);
    }

    [Fact]
    public void ApplyCsvImport_FailureMidBatch_RollsBackEveryRow()
    {
        // Arrange
        Csv(Rows);
        var preview = store.PreviewCsvImport(csv);
        SqliteScript.Execute(DatabasePath, "CREATE TRIGGER reject_import BEFORE INSERT ON import_sources WHEN NEW.external_id='t1' BEGIN SELECT RAISE(ABORT,'test rollback'); END;");

        // Act
        void Act() => store.ApplyCsvImport(preview);

        // Assert
        Assert.Throws<SqliteException>(Act);
        Assert.Empty(store.ReadSnapshot().Accounts);
    }

    [Fact]
    public void PreviewCsvImport_LocallyEditedImportedEntry_BecomesReimportConflict()
    {
        // Arrange
        Csv(Rows);
        store.ApplyCsvImport(store.PreviewCsvImport(csv));
        var entry = store.ReadSnapshot().Entries.Single(e => e.Draft.Kind == TransactionKind.Income);
        store.SaveTransaction(entry.Id, entry.Draft with { Description = "edited" });
        var preview = store.PreviewCsvImport(csv);

        // Act
        void Act() => store.ApplyCsvImport(preview);

        // Assert
        Assert.Throws<CsvImportConflictException>(Act);
    }

    [Fact]
    public void ApplyCsvImport_PriorProvenanceLabels_StillMatchesRepeatedImport()
    {
        // Arrange
        Csv(Rows);
        store.ApplyCsvImport(store.PreviewCsvImport(csv));
        SqliteScript.Execute(DatabasePath, "UPDATE import_sources SET source='LegacySource'");

        // Act
        var result = store.ApplyCsvImport(store.PreviewCsvImport(csv));

        // Assert
        Assert.Equal(0, result.Added);
        Assert.Equal(5, result.Unchanged);
    }

    [Fact]
    public void ApplyCsvImport_PrivateExport_ReconcilesAccountTotalsWhenProvided()
    {
        var source = Environment.GetEnvironmentVariable("BALANCIA_PRIVATE_IMPORT_PATH");
        Assert.SkipWhen(source is null, "Set BALANCIA_PRIVATE_IMPORT_PATH to reconcile a private export.");

        // Arrange
        var preview = store.PreviewCsvImport(source!);

        // Act
        var applied = store.ApplyCsvImport(preview);
        var again = store.ApplyCsvImport(store.PreviewCsvImport(source!));

        // Assert
        Assert.True(preview.CanApply, $"Private export has {preview.Issues.Count} import issues; first line: {preview.Issues.FirstOrDefault()?.Line}.");
        Assert.Equal(preview.Summary.Expenses + preview.Summary.Incomes + preview.Summary.Transfers + preview.Summary.Openings, applied.Added);
        var accounts = store.ReadSnapshot().Accounts;
        foreach (var expected in preview.Summary.AccountTotals)
        {
            Assert.Equal(expected.Centimes, accounts.Single(a => a.Name == expected.Account).Balance.Centimes);
        }

        Assert.Equal(0, again.Added);
    }

    public void Dispose() => Directory.Delete(dir, true);
}
