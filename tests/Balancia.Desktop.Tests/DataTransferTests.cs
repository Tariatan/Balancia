using System.Text;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Balancia.Core;
using Balancia.Desktop.Localization;
using Balancia.Storage;
using Xunit;

namespace Balancia.Desktop.Tests;

/// <summary>CSV and snapshot import/export driven through the window, with the native pickers answered by the test.</summary>
public sealed class DataTransferTests
{
    private const string ValidCsv = "ID,Date,Type,Description,Amount,Account,DestinationAccount,Category,Memo\n" +
        "o1,2026-01-01,OpeningBalance,Opening balance,100.00,Savings,,,\n" +
        "x1,2026-01-02,Expense,Bread,-4.50,Savings,,Food,\n";

    private static async Task<(MainWindow Window, LedgerStore Store)> OpenSeededAsync(DesktopWorkspace workspace)
    {
        var store = workspace.SeedStore();
        LedgerSeed.Expense(store, LedgerSeed.Account(store), "Coffee beans", LedgerSeed.Today);
        workspace.WriteSettings();
        return (await workspace.OpenConfiguredWindowAsync(withSnapshotKey: true), store);
    }

    private static string Status(Window window) => window.Text("OverviewStatus");

    [AvaloniaFact]
    public async Task ExportCsv_PickedFile_WritesTheLedgerAndReportsTheRowCount()
    {
        // Arrange
        using var workspace = new DesktopWorkspace();
        var (window, _) = await OpenSeededAsync(workspace);
        var target = Path.Combine(workspace.Root, "out.csv");
        window.SaveFilePicker = (_, _, _) => Task.FromResult<string?>(target);

        // Act
        await window.ExportCsv().CompleteAsync();

        // Assert
        var text = File.ReadAllText(target);
        Assert.StartsWith("ID,Date,Type,Description,Amount,Account,DestinationAccount,Category,Memo", text);
        Assert.Contains("Coffee beans", text);
        Assert.Equal("Exported 2 rows to CSV.", Status(window));
    }

    [AvaloniaFact]
    public async Task ExportCsv_PickerCanceled_WritesNothing()
    {
        // Arrange
        using var workspace = new DesktopWorkspace();
        var (window, _) = await OpenSeededAsync(workspace);
        window.SaveFilePicker = (_, _, _) => Task.FromResult<string?>(null);

        // Act
        await window.ExportCsv().CompleteAsync();

        // Assert
        Assert.Empty(Directory.GetFiles(workspace.Root, "*.csv"));
    }

    [AvaloniaFact]
    public async Task ImportCsv_ValidFileApplied_AddsTheEntriesAndReportsTheCounts()
    {
        // Arrange
        using var workspace = new DesktopWorkspace();
        workspace.SeedStore();
        workspace.WriteSettings();
        var window = await workspace.OpenConfiguredWindowAsync(withSnapshotKey: true);
        var source = Path.Combine(workspace.Root, "in.csv");
        File.WriteAllText(source, ValidCsv, new UTF8Encoding(false));
        window.OpenFilePicker = (_, _) => Task.FromResult<string?>(source);

        // Act
        var operation = window.ImportCsv();
        var preview = await window.WaitForDialogAsync<CsvImportPreviewWindow>();
        var message = preview.Text("MessageText");
        preview.ClickDialogButton("Apply import");
        await operation.CompleteAsync();
        await DesktopWorkspace.WaitUntilAsync(() => Status(window).StartsWith("Imported"), "the import result");

        // Assert
        Assert.Contains("2 rows: 1 expenses, 0 income, 0 transfers, 1 openings.", message);
        Assert.Contains("All rows are valid. Apply this batch?", message);
        Assert.Equal("Imported 2 entries; 0 unchanged.", Status(window));
        var snapshot = workspace.SeedStore().ReadSnapshot();
        Assert.Equal("Savings", Assert.Single(snapshot.Accounts).Name);
        Assert.Equal("Bread", Assert.Single(snapshot.Entries).Draft.Description);
    }

    [AvaloniaFact]
    public async Task ImportCsv_PreviewInGerman_TranslatesTheSummary()
    {
        // Arrange
        using var workspace = new DesktopWorkspace();
        workspace.SeedStore();
        workspace.WriteSettings(language: "de");
        var window = await workspace.OpenConfiguredWindowAsync(withSnapshotKey: true);
        var source = Path.Combine(workspace.Root, "in.csv");
        File.WriteAllText(source, ValidCsv, new UTF8Encoding(false));
        window.OpenFilePicker = (_, _) => Task.FromResult<string?>(source);

        // Act
        var operation = window.ImportCsv();
        var preview = await window.WaitForDialogAsync<CsvImportPreviewWindow>();
        var message = preview.Text("MessageText");
        preview.ClickDialogButton(UiText.Get("Cancel"));
        await operation.CompleteAsync();

        // Assert
        Assert.Contains("Alle Zeilen sind gültig", message);
        Assert.Contains("Konten und Summen der Quelle:", message);
        Assert.Contains("Kategorien:", message);
        Assert.Contains("2 Zeilen: 1 Ausgaben", message);
    }

    [AvaloniaFact]
    public async Task ImportCsv_InvalidFile_ListsTheErrorsAndCannotBeApplied()
    {
        // Arrange
        using var workspace = new DesktopWorkspace();
        workspace.SeedStore();
        workspace.WriteSettings();
        var window = await workspace.OpenConfiguredWindowAsync(withSnapshotKey: true);
        var source = Path.Combine(workspace.Root, "bad.csv");
        File.WriteAllText(source, "not,the,expected,header\n1,2,3,4\n");
        window.OpenFilePicker = (_, _) => Task.FromResult<string?>(source);

        // Act
        var operation = window.ImportCsv();
        var preview = await window.WaitForDialogAsync<CsvImportPreviewWindow>();
        var message = preview.Text("MessageText");
        var applyEnabled = preview.Find<Button>("ApplyButton").IsEnabled;
        preview.ClickDialogButton("Cancel");
        await operation.CompleteAsync();

        // Assert
        Assert.Contains("Errors (correct source file, then preview again):", message);
        Assert.False(applyEnabled);
        Assert.Empty(workspace.SeedStore().ReadSnapshot().Accounts);
    }

    [AvaloniaFact]
    public async Task ImportCsv_LedgerChangesWhilePreviewIsOpen_ShowsTheErrorInTheDialogAndAllowsRetry()
    {
        // Arrange
        using var workspace = new DesktopWorkspace();
        var store = workspace.SeedStore();
        workspace.WriteSettings();
        var window = await workspace.OpenConfiguredWindowAsync(withSnapshotKey: true);
        var source = Path.Combine(workspace.Root, "in.csv");
        File.WriteAllText(source, ValidCsv, new UTF8Encoding(false));
        window.OpenFilePicker = (_, _) => Task.FromResult<string?>(source);
        var operation = window.ImportCsv();
        var preview = await window.WaitForDialogAsync<CsvImportPreviewWindow>();
        LedgerSeed.Account(store, "Elsewhere");

        // Act
        preview.ClickDialogButton("Apply import");
        await DesktopWorkspace.WaitUntilAsync(() => preview.Text("ErrorText").Length > 0, "the failure to show");

        // Assert
        Assert.Contains("Ledger changed after preview", preview.Text("ErrorText"));
        Assert.True(preview.Find<Button>("ApplyButton").IsEnabled);
        preview.ClickDialogButton("Cancel");
        await operation.CompleteAsync();
    }

    [AvaloniaFact]
    public async Task ImportCsv_PickerCanceled_DoesNothing()
    {
        // Arrange
        using var workspace = new DesktopWorkspace();
        var (window, _) = await OpenSeededAsync(workspace);
        window.OpenFilePicker = (_, _) => Task.FromResult<string?>(null);

        // Act
        await window.ImportCsv().CompleteAsync();

        // Assert
        Assert.Empty(window.OwnedWindows);
    }

    [AvaloniaFact]
    public async Task ExportSnapshot_WithRememberedKey_WritesAValidSnapshot()
    {
        // Arrange
        using var workspace = new DesktopWorkspace();
        var (window, _) = await OpenSeededAsync(workspace);
        var target = Path.Combine(workspace.Root, "out.balancia");
        window.SaveFilePicker = (_, _, _) => Task.FromResult<string?>(target);

        // Act
        await window.ExportSnapshot().CompleteAsync();

        // Assert
        Assert.Equal(2, workspace.ValidateExport(target).Revision);
        Assert.StartsWith("Snapshot exported", Status(window));
    }

    [AvaloniaFact]
    public async Task ExportSnapshot_WithoutKeyAndSetupCanceled_NeverAsksForAFile()
    {
        // Arrange
        using var workspace = new DesktopWorkspace();
        workspace.SeedStore();
        workspace.WriteSettings();
        var window = await workspace.OpenConfiguredWindowAsync(withSnapshotKey: false);
        var asked = false;
        window.SaveFilePicker = (_, _, _) =>
        {
            asked = true;
            return Task.FromResult<string?>(null);
        };

        // Act
        var operation = window.ExportSnapshot();
        var setup = await window.WaitForDialogAsync<SnapshotPassphraseWindow>();
        setup.ClickDialogButton("Cancel");
        await operation.CompleteAsync();

        // Assert
        Assert.False(asked);
    }

    [AvaloniaFact]
    public async Task ExportSnapshot_WithoutKeyThenPassphrase_RemembersTheKeyAndExports()
    {
        // Arrange
        using var workspace = new DesktopWorkspace();
        workspace.SeedStore();
        workspace.WriteSettings();
        var keyStore = new FakeKeyStore();
        var window = workspace.CreateWindowWithKeyStore(keyStore);
        await DesktopWorkspace.OpenAsync(window);
        var target = Path.Combine(workspace.Root, "out.balancia");
        window.SaveFilePicker = (_, _, _) => Task.FromResult<string?>(target);

        // Act
        var operation = window.ExportSnapshot();
        var setup = await window.WaitForDialogAsync<SnapshotPassphraseWindow>();
        setup.SubmitPassphrase("synthetic passphrase 123", "Save");
        await operation.CompleteAsync();

        // Assert
        Assert.True(File.Exists(target));
        Assert.Equal(1, keyStore.SaveCount);
        Assert.True(window.EncryptionConfigured);
    }

    [AvaloniaFact]
    public async Task RestoreSnapshot_MatchingRememberedKey_RestoresAndKeepsARecoveryCopy()
    {
        // Arrange
        using var workspace = new DesktopWorkspace();
        var (window, store) = await OpenSeededAsync(workspace);
        var snapshot = Path.Combine(workspace.Root, "current.balancia");
        using (var key = DesktopWorkspace.CreateKey())
        {
            store.ExportSnapshot(snapshot, key);
        }

        window.OpenFilePicker = (_, _) => Task.FromResult<string?>(snapshot);

        // Act
        await window.RestoreSnapshot().CompleteAsync();

        // Assert
        Assert.StartsWith("Snapshot restored · revision 2 · backup balancia.db.pre-restore-", Status(window));
        Assert.Single(Directory.GetFiles(workspace.DataDirectory, "balancia.db.pre-restore-*.bak"));
    }

    [AvaloniaFact]
    public async Task RestoreSnapshot_OlderThanTheLedger_ShowsTheErrorAndKeepsTheLedger()
    {
        // Arrange
        using var workspace = new DesktopWorkspace();
        var (window, store) = await OpenSeededAsync(workspace);
        var older = Path.Combine(workspace.Root, "older.balancia");
        using (var key = DesktopWorkspace.CreateKey())
        {
            store.ExportSnapshot(older, key);
        }

        LedgerSeed.Account(store, "Later account");
        window.OpenFilePicker = (_, _) => Task.FromResult<string?>(older);

        // Act
        var operation = window.RestoreSnapshot();
        var error = await window.WaitForDialogAsync<ErrorDialog>();
        var errorText = error.Text("MessageText");
        error.ClickDialogButton("Close");
        await operation.CompleteAsync();

        // Assert
        Assert.Contains("older snapshot cannot replace the current ledger", errorText);
        Assert.Equal(2, store.ReadSnapshot().Accounts.Count);
    }

    [AvaloniaFact]
    public async Task RestoreSnapshot_ExportedWithAnotherPassphrase_AsksForItAndRestoresWhenCorrect()
    {
        // Arrange
        using var workspace = new DesktopWorkspace();
        var (window, store) = await OpenSeededAsync(workspace);
        var snapshot = Path.Combine(workspace.Root, "other.balancia");
        var otherKey = SnapshotKey.Create("synthetic other passphrase");
        store.ExportSnapshot(snapshot, otherKey);
        window.OpenFilePicker = (_, _) => Task.FromResult<string?>(snapshot);

        // Act
        var operation = window.RestoreSnapshot();
        var wrong = await window.WaitForDialogAsync<SnapshotPassphraseWindow>();
        wrong.SubmitPassphrase("synthetic wrong passphrase", "Unlock");
        var error = await window.WaitForDialogAsync<ErrorDialog>();
        var errorText = error.Text("MessageText");
        error.ClickDialogButton("Close");
        await operation.CompleteAsync();
        var retry = window.RestoreSnapshot();
        var correct = await window.WaitForDialogAsync<SnapshotPassphraseWindow>();
        correct.SubmitPassphrase("synthetic other passphrase", "Unlock");
        await retry.CompleteAsync();

        // Assert
        Assert.Contains("passphrase is incorrect or the file is damaged", errorText);
        Assert.StartsWith("Snapshot restored", Status(window));
        otherKey.Dispose();
    }

    [AvaloniaFact]
    public async Task RestoreSnapshot_UnlockCanceled_LeavesTheLedgerUntouched()
    {
        // Arrange
        using var workspace = new DesktopWorkspace();
        var (window, store) = await OpenSeededAsync(workspace);
        var snapshot = Path.Combine(workspace.Root, "other.balancia");
        using (var otherKey = SnapshotKey.Create("synthetic other passphrase"))
        {
            store.ExportSnapshot(snapshot, otherKey);
        }

        window.OpenFilePicker = (_, _) => Task.FromResult<string?>(snapshot);
        var before = store.ReadLiveMetadata();

        // Act
        var operation = window.RestoreSnapshot();
        var unlock = await window.WaitForDialogAsync<SnapshotPassphraseWindow>();
        unlock.ClickDialogButton("Cancel");
        await operation.CompleteAsync();

        // Assert
        Assert.Equal(before, store.ReadLiveMetadata());
        Assert.Empty(Directory.GetFiles(workspace.DataDirectory, "*.bak"));
    }

    [AvaloniaFact]
    public async Task RestoreSnapshot_PickerCanceled_DoesNothing()
    {
        // Arrange
        using var workspace = new DesktopWorkspace();
        var (window, _) = await OpenSeededAsync(workspace);
        window.OpenFilePicker = (_, _) => Task.FromResult<string?>(null);

        // Act
        await window.RestoreSnapshot().CompleteAsync();

        // Assert
        Assert.Empty(window.OwnedWindows);
    }
}
