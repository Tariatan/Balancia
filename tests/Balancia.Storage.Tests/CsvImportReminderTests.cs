using System.Text;
using Balancia.Core;
using Xunit;

namespace Balancia.Storage.Tests;

public sealed class CsvImportReminderTests : IDisposable
{
    private const string Header = "ID,Date,Type,Description,Amount,Account,DestinationAccount,Category,Memo\n";
    private const string Opening = "o1,2026-01-01,OpeningBalance,Opening balance,100.00,A,,,\n";
    private const string RentRow = "reminder:r1,2026-01-05,Reminder,Rent,1200.00,,,,\"{\"\"IntervalMonths\"\":1,\"\"Archived\"\":false}\"\n";

    private readonly string dir = Path.Combine(Path.GetTempPath(), "balancia-import-reminders-" + Guid.NewGuid().ToString("N"));
    private readonly LedgerStore store;
    private readonly string csv;

    public CsvImportReminderTests()
    {
        Directory.CreateDirectory(dir);
        csv = Path.Combine(dir, "source.csv");
        store = NewStore("ledger.db");
    }

    private LedgerStore NewStore(string name)
    {
        var created = new LedgerStore(Path.Combine(dir, name), new TestClock(new DateTimeOffset(2026, 9, 30, 12, 0, 0, TimeSpan.Zero)));
        created.Initialize();
        return created;
    }

    private void Csv(string rows) => File.WriteAllText(csv, Header + rows, new UTF8Encoding(true));

    [Fact]
    public void ApplyCsvImport_ReminderRow_CreatesTemplateWithoutLedgerEntry()
    {
        // Arrange
        Csv(Opening + RentRow);

        // Act
        var result = store.ApplyCsvImport(store.PreviewCsvImport(csv));

        // Assert
        Assert.Equal(2, result.Added);
        var template = store.ReadReminders().Single().Template;
        Assert.Equal("Rent", template.Description);
        Assert.Equal(new DateOnly(2026, 1, 5), template.ExpectedDate);
        Assert.Equal(Money.FromFrancs(1200m), template.IndicativeAmount);
        Assert.Equal(1, template.IntervalMonths);
        Assert.Empty(store.ReadAllTransactions(new TransactionsFilter()).Hits);
    }

    [Fact]
    public void ApplyCsvImport_ActiveRemindersDifferingOnlyInLetterCase_ImportsBoth()
    {
        // Arrange
        Csv(Opening + RentRow +
            "reminder:r2,2026-01-06,Reminder,rent,10.00,,,,\"{\"\"IntervalMonths\"\":1,\"\"Archived\"\":false}\"\n");

        // Act
        store.ApplyCsvImport(store.PreviewCsvImport(csv));

        // Assert
        Assert.Equal(["Rent", "rent"], store.ReadReminders().Select(reminder => reminder.Template.Description).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void ApplyCsvImport_ActiveReminderWithTheSameExactDescription_ThrowsConflict()
    {
        // Arrange
        store.SaveReminder(null, "Rent", new DateOnly(2026, 2, 1), Money.FromFrancs(5m), 1);
        Csv(Opening + RentRow);

        // Act
        void Act() => store.ApplyCsvImport(store.PreviewCsvImport(csv));

        // Assert
        Assert.Throws<CsvImportConflictException>(Act);
    }

    [Fact]
    public void ApplyCsvImport_ArchivedReminderRow_IsStoredButNotListed()
    {
        // Arrange
        Csv(Opening + "reminder:r2,2026-02-01,Reminder,Old plan,9.00,,,,\"{\"\"IntervalMonths\"\":3,\"\"Archived\"\":true}\"\n");

        // Act
        store.ApplyCsvImport(store.PreviewCsvImport(csv));

        // Assert
        Assert.Empty(store.ReadReminders());
        Assert.Contains("Old plan", File.ReadAllText(ExportToDisk()));
    }

    [Fact]
    public void ApplyCsvImport_SameFileAgain_IsUnchanged()
    {
        // Arrange
        Csv(Opening + RentRow);
        store.ApplyCsvImport(store.PreviewCsvImport(csv));

        // Act
        var second = store.ApplyCsvImport(store.PreviewCsvImport(csv));

        // Assert
        Assert.Equal(0, second.Added);
        Assert.Equal(2, second.Unchanged);
        Assert.Single(store.ReadReminders());
    }

    [Fact]
    public void ExportCsv_ThenImportIntoFreshLedger_RestoresRemindersIncludingArchived()
    {
        // Arrange
        var account = store.SaveAccount(null, "A", new DateOnly(2026, 1, 1), new Money(10000));
        store.SaveReminder(null, "Rent", new DateOnly(2026, 1, 5), Money.FromFrancs(1200m), 1);
        store.SaveReminder(null, "Gym", new DateOnly(2026, 2, 10), Money.FromFrancs(40m), 6, archived: true);
        var exportPath = ExportToDisk();
        var fresh = NewStore("fresh.db");

        // Act
        fresh.ApplyCsvImport(fresh.PreviewCsvImport(exportPath));

        // Assert
        var rent = Assert.Single(fresh.ReadReminders());
        Assert.Equal("Rent", rent.Template.Description);
        Assert.Equal(new DateOnly(2026, 1, 5), rent.Template.ExpectedDate);
        Assert.Contains("Gym", File.ReadAllText(exportPath));
        var reexport = Path.Combine(dir, "reexport.csv");
        fresh.ExportCsv(reexport);
        Assert.Contains("Gym", File.ReadAllText(reexport));
        Assert.NotEmpty(account);
    }

    [Fact]
    public void ApplyCsvImport_ReminderEditedLocally_ReimportReportsConflict()
    {
        // Arrange
        Csv(Opening + RentRow);
        store.ApplyCsvImport(store.PreviewCsvImport(csv));
        var id = store.ReadReminders().Single().Template.Id;
        store.SaveReminder(id, "Rent", new DateOnly(2026, 1, 5), Money.FromFrancs(1300m), 1);

        // Act
        void Act() => store.ApplyCsvImport(store.PreviewCsvImport(csv));

        // Assert
        Assert.Throws<CsvImportConflictException>(Act);
    }

    [Fact]
    public void ApplyCsvImport_ReminderWithExistingActiveDescription_RejectsTheBatch()
    {
        // Arrange
        store.SaveReminder(null, "Rent", new DateOnly(2026, 3, 1), Money.FromFrancs(1m), 1);
        Csv(Opening + RentRow);

        // Act
        void Act() => store.ApplyCsvImport(store.PreviewCsvImport(csv));

        // Assert
        var error = Assert.Throws<CsvImportConflictException>(Act);
        Assert.Contains("already uses this description", error.Message);
        Assert.Empty(store.ReadSnapshot().Accounts);
    }

    [Fact]
    public void ApplyCsvImport_ReminderIdMatchingDifferentExistingTemplate_RejectsTheBatch()
    {
        // Arrange
        store.SaveReminder("r1", "Rent", new DateOnly(2026, 1, 5), Money.FromFrancs(999m), 1);
        Csv(Opening + RentRow);

        // Act
        void Act() => store.ApplyCsvImport(store.PreviewCsvImport(csv));

        // Assert
        var error = Assert.Throws<CsvImportConflictException>(Act);
        Assert.Contains("conflicts with existing reminder", error.Message);
    }

    [Theory]
    [InlineData("reminder:r1,2026-01-05,Reminder,,10.00,,,,\"{\"\"IntervalMonths\"\":1,\"\"Archived\"\":false}\"\n", "Enter a description.")]
    [InlineData("reminder:r1,2026-01-05,Reminder,Rent,0.00,,,,\"{\"\"IntervalMonths\"\":1,\"\"Archived\"\":false}\"\n", "Enter amount greater than zero.")]
    [InlineData("reminder:r1,2026-01-05,Reminder,Rent,10.00,A,,,\"{\"\"IntervalMonths\"\":1,\"\"Archived\"\":false}\"\n", "Invalid reminder fields.")]
    [InlineData("r1,2026-01-05,Reminder,Rent,10.00,,,,\"{\"\"IntervalMonths\"\":1,\"\"Archived\"\":false}\"\n", "Invalid reminder fields.")]
    [InlineData("reminder:r1,2026-01-05,Reminder,Rent,10.00,,,,\n", "Invalid reminder schedule.")]
    [InlineData("reminder:r1,2026-01-05,Reminder,Rent,10.00,,,,not json\n", "Invalid reminder schedule.")]
    [InlineData("reminder:r1,2026-01-05,Reminder,Rent,10.00,,,,\"{\"\"IntervalMonths\"\":0,\"\"Archived\"\":false}\"\n", "Repeat interval must be positive.")]
    [InlineData("reminder:r1,2026-01-05,Reminder,Rent,10.00,,,,\"{\"\"IntervalMonths\"\":1}\"\n", "Invalid reminder schedule.")]
    [InlineData("reminder:r1,2026-01-05,Reminder,Rent,10.00,,,,\"[1]\"\n", "Invalid reminder schedule.")]
    public void PreviewCsvImport_InvalidReminderRow_BlocksImport(string row, string expectedIssue)
    {
        // Arrange
        Csv(Opening + row);

        // Act
        var preview = store.PreviewCsvImport(csv);

        // Assert
        Assert.False(preview.CanApply);
        Assert.Contains(preview.Issues, issue => issue.Message == expectedIssue);
    }

    private string ExportToDisk()
    {
        var path = Path.Combine(dir, "export-" + Guid.NewGuid().ToString("N") + ".csv");
        store.ExportCsv(path);
        return path;
    }

    public void Dispose() => Directory.Delete(dir, true);
}
