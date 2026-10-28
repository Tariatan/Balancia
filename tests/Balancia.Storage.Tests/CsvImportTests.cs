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
    public void ApplyCsvImport_ImportedAccountDeleted_ReimportRestoresIt()
    {
        // Arrange
        Csv("o1,2026-09-01,OpeningBalance,Opening balance,100.00,A,,,\n");
        store.ApplyCsvImport(store.PreviewCsvImport(csv));
        store.DeleteAccount(store.ReadSnapshot().Accounts.Single().Id);

        // Act
        store.ApplyCsvImport(store.PreviewCsvImport(csv));

        // Assert
        var account = Assert.Single(store.ReadSnapshot().Accounts);
        Assert.Equal("A", account.Name);
        Assert.Equal(10000, account.Balance.Centimes);
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
        Assert.Equal(5, result.Imported);
        Assert.Equal(11110, snapshot.NetWorth.Centimes);
        Assert.Equal(120, snapshot.MonthlyIncome.Centimes);
        Assert.Equal(1010, snapshot.MonthlyExpenses.Centimes);
        Assert.Equal(3, snapshot.Entries.Count);
        Assert.Contains("\n", expense.Draft.Description);
        Assert.Equal("note, preserved", expense.Draft.Memo);
    }

    [Fact]
    public void ApplyCsvImport_SameFileTwice_LeavesTheSameLedgerWithANewRevision()
    {
        // Arrange
        Csv(Rows);
        store.ApplyCsvImport(store.PreviewCsvImport(csv));
        var first = store.ReadSnapshot();

        // Act
        var second = store.ApplyCsvImport(store.PreviewCsvImport(csv));

        // Assert
        var again = store.ReadSnapshot();
        Assert.Equal(5, second.Imported);
        Assert.Equal(first.NetWorth, again.NetWorth);
        Assert.Equal(first.Entries.Count, again.Entries.Count);
        Assert.True(again.Revision > first.Revision);
    }

    [Fact]
    public void ApplyCsvImport_ChangedFile_ReplacesTheEarlierData()
    {
        // Arrange
        Csv(Rows);
        store.ApplyCsvImport(store.PreviewCsvImport(csv));
        Csv(Rows.Replace("-10.10", "-11.10"));

        // Act
        store.ApplyCsvImport(store.PreviewCsvImport(csv));

        // Assert
        Assert.Equal(11010, store.ReadSnapshot().NetWorth.Centimes);
    }

    private void SeedExistingLedger()
    {
        var account = store.SaveAccount(null, "Old account", new DateOnly(2026, 1, 1), Money.FromFrancs(50m));
        var category = store.SaveCategory(null, "Old category", null);
        store.SaveTransaction(null, new TransactionDraft(TransactionKind.Expense, new DateOnly(2026, 2, 1), "Old purchase", Money.FromFrancs(5m), account, CategoryId: category));
        store.SaveReminder(null, "Old reminder", new DateOnly(2026, 3, 1), Money.FromFrancs(7m), 1);
    }

    [Fact]
    public void PreviewCsvImport_LedgerWithData_ReportsWhatTheImportWillReplace()
    {
        // Arrange
        SeedExistingLedger();
        Csv(Rows);

        // Act
        var preview = store.PreviewCsvImport(csv);

        // Assert
        Assert.Equal(new ExistingLedger(Accounts: 1, Transactions: 1, Reminders: 1), preview.Existing);
        Assert.False(preview.Existing.IsEmpty);
    }

    [Fact]
    public void PreviewCsvImport_EmptyLedger_ReportsNothingToReplace()
    {
        // Arrange
        Csv(Rows);

        // Act
        var preview = store.PreviewCsvImport(csv);

        // Assert
        Assert.True(preview.Existing.IsEmpty);
    }

    [Fact]
    public void ApplyCsvImport_LedgerWithData_ReplacesItInsteadOfAppending()
    {
        // Arrange
        SeedExistingLedger();
        var identity = store.ReadLiveMetadata();
        Csv(Rows);

        // Act
        var result = store.ApplyCsvImport(store.PreviewCsvImport(csv));

        // Assert
        var snapshot = store.ReadSnapshot();
        Assert.Equal(5, result.Imported);
        Assert.Equal(["A", "B"], snapshot.Accounts.Select(a => a.Name).Order());
        Assert.Equal(["Food", "Food / Lunch"], snapshot.Categories.Select(c => c.Path).Order());
        Assert.DoesNotContain(snapshot.Entries, e => e.Draft.Description == "Old purchase");
        Assert.Empty(store.ReadReminders());
        Assert.Equal(11110, snapshot.NetWorth.Centimes);
        var after = store.ReadLiveMetadata();
        Assert.Equal(identity.DatasetId, after.DatasetId);
        Assert.True(after.Revision > identity.Revision);
    }

    [Fact]
    public void ApplyCsvImport_LedgerWithData_SavesARecoveryCopyOfIt()
    {
        // Arrange
        SeedExistingLedger();
        Csv(Rows);

        // Act
        var result = store.ApplyCsvImport(store.PreviewCsvImport(csv));

        // Assert
        Assert.NotNull(result.RecoveryCopyPath);
        Assert.True(File.Exists(result.RecoveryCopyPath));
        var copy = new LedgerStore(result.RecoveryCopyPath!);
        Assert.Equal("Old account", Assert.Single(copy.ReadSnapshot().Accounts).Name);
        Assert.Equal("Old reminder", Assert.Single(copy.ReadReminders()).Template.Description);
    }

    [Fact]
    public void ApplyCsvImport_EmptyLedger_SavesNoRecoveryCopy()
    {
        // Arrange
        Csv(Rows);

        // Act
        var result = store.ApplyCsvImport(store.PreviewCsvImport(csv));

        // Assert
        Assert.Null(result.RecoveryCopyPath);
        Assert.Empty(Directory.GetFiles(dir, "*.pre-import-*.bak"));
    }

    [Fact]
    public void ApplyCsvImport_RepeatedImports_KeepTheThreeNewestRecoveryCopies()
    {
        // Arrange
        Csv(Rows);
        store.ApplyCsvImport(store.PreviewCsvImport(csv));

        // Act
        for (var index = 0; index < 4; index++)
        {
            store.ApplyCsvImport(store.PreviewCsvImport(csv));
        }

        // Assert
        Assert.Equal(3, Directory.GetFiles(dir, "*.pre-import-*.bak").Length);
    }

    [Fact]
    public void ApplyCsvImport_LedgerChangedAfterPreview_ThrowsAndSavesNoRecoveryCopy()
    {
        // Arrange
        SeedExistingLedger();
        Csv(Rows);
        var preview = store.PreviewCsvImport(csv);
        store.SaveAccount(null, "Added later", new DateOnly(2026, 1, 1), Money.Zero);

        // Act
        void Act() => store.ApplyCsvImport(preview);

        // Assert
        Assert.Throws<CsvImportConflictException>(Act);
        Assert.Empty(Directory.GetFiles(dir, "*.pre-import-*.bak"));
        Assert.Equal(2, store.ReadSnapshot().Accounts.Count);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   \n")]
    public void PreviewCsvImport_FileWithNoRows_BlocksImportSoItCannotEmptyTheLedger(string rows)
    {
        // Arrange
        SeedExistingLedger();
        Csv(rows);

        // Act
        var preview = store.PreviewCsvImport(csv);

        // Assert
        Assert.False(preview.CanApply);
        Assert.Contains(preview.Issues, issue => issue.Message == "The file has no rows to import.");
        Assert.Throws<CsvImportConflictException>(() => store.ApplyCsvImport(preview));
        Assert.Equal("Old account", Assert.Single(store.ReadSnapshot().Accounts).Name);
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
    public void ApplyCsvImport_CategoriesDifferingOnlyByANonAsciiLetterCase_ShareOneCategory()
    {
        // Arrange
        Csv(OpeningRow +
            "x1,2026-09-02,Expense,X,-1.00,A,,Épicerie,\n" +
            "x2,2026-09-03,Expense,Y,-1.00,A,,épicerie,\n");

        // Act
        store.ApplyCsvImport(store.PreviewCsvImport(csv));

        // Assert
        var snapshot = store.ReadSnapshot();
        Assert.Equal("Épicerie", Assert.Single(snapshot.Categories).Path);
        Assert.Single(snapshot.Entries.Select(e => e.Draft.CategoryId).Distinct());
    }

    [Fact]
    public void ApplyCsvImport_SubcategoryOfAParentTheFileAlsoUses_CreatesOnlyTheSubcategoryUnderIt()
    {
        // Arrange
        Csv(OpeningRow +
            "x1,2026-09-02,Expense,X,-1.00,A,,Food,\n" +
            "x2,2026-09-03,Expense,Y,-1.00,A,,FOOD/Lunch,\n");

        // Act
        store.ApplyCsvImport(store.PreviewCsvImport(csv));

        // Assert
        var categories = store.ReadSnapshot().Categories;
        Assert.Equal(2, categories.Count);
        Assert.Equal(categories.Single(c => c.Path == "Food").Id, categories.Single(c => c.Path == "Food / Lunch").ParentId);
    }

    [Fact]
    public void ApplyCsvImport_ArchivedCategoryInTheLedger_IsReplacedWithAnActiveOne()
    {
        // Arrange
        var categoryId = store.SaveCategory(null, "Groceries", null);
        store.SaveCategory(categoryId, "Groceries", null, archived: true);
        Csv(OpeningRow + "x1,2026-09-02,Expense,X,-1.00,A,,Groceries,\n");

        // Act
        store.ApplyCsvImport(store.PreviewCsvImport(csv));

        // Assert
        var category = Assert.Single(store.ReadSnapshot().Categories);
        Assert.Equal("Groceries", category.Path);
        Assert.False(category.Archived);
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
    public void ApplyCsvImport_FailureMidBatch_KeepsTheLedgerThatWasThere()
    {
        // Arrange
        SeedExistingLedger();
        Csv(Rows);
        var preview = store.PreviewCsvImport(csv);
        SqliteScript.Execute(DatabasePath, "CREATE TRIGGER reject_import BEFORE INSERT ON import_sources WHEN NEW.external_id='t1' BEGIN SELECT RAISE(ABORT,'test rollback'); END;");

        // Act
        void Act() => store.ApplyCsvImport(preview);

        // Assert
        Assert.Throws<SqliteException>(Act);
        var snapshot = store.ReadSnapshot();
        Assert.Equal("Old account", Assert.Single(snapshot.Accounts).Name);
        Assert.Equal("Old purchase", Assert.Single(snapshot.Entries).Draft.Description);
        Assert.Equal("Old reminder", Assert.Single(store.ReadReminders()).Template.Description);
    }

    [Fact]
    public void ApplyCsvImport_EntryEditedLocallyAfterAnImport_IsOverwrittenByTheNextImport()
    {
        // Arrange
        Csv(Rows);
        store.ApplyCsvImport(store.PreviewCsvImport(csv));
        var entry = store.ReadSnapshot().Entries.Single(e => e.Draft.Kind == TransactionKind.Income);
        store.SaveTransaction(entry.Id, entry.Draft with { Description = "edited" });

        // Act
        store.ApplyCsvImport(store.PreviewCsvImport(csv));

        // Assert
        Assert.DoesNotContain(store.ReadSnapshot().Entries, e => e.Draft.Description == "edited");
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

        // Assert
        Assert.True(preview.CanApply, $"Private export has {preview.Issues.Count} import issues; first line: {preview.Issues.FirstOrDefault()?.Line}.");
        Assert.Equal(preview.Summary.Expenses + preview.Summary.Incomes + preview.Summary.Transfers + preview.Summary.Openings, applied.Imported);
        var accounts = store.ReadSnapshot().Accounts;
        foreach (var expected in preview.Summary.AccountTotals)
        {
            Assert.Equal(expected.Centimes, accounts.Single(a => a.Name == expected.Account).Balance.Centimes);
        }

    }

    public void Dispose() => Directory.Delete(dir, true);
}
