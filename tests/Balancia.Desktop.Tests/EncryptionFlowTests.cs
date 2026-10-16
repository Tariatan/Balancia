using System.Security.Cryptography;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Xunit;

namespace Balancia.Desktop.Tests;

/// <summary>Startup and Settings behavior around the remembered snapshot key, driven through the real dialogs with a fake key store.</summary>
public sealed class EncryptionFlowTests
{
    private const string Passphrase = "synthetic passphrase 123";

    [AvaloniaFact]
    public async Task Open_RememberedKeyCannotBeDecrypted_ReportsItThenOffersSetupAndRemembersTheNewKey()
    {
        // Arrange
        using var workspace = new DesktopWorkspace();
        workspace.SeedStore();
        workspace.WriteSettings(backupPath: workspace.BackupDirectory);
        var keyStore = new FakeKeyStore { LoadFailure = new CryptographicException("The data is invalid.") };
        var window = workspace.CreateWindowWithKeyStore(keyStore);

        // Act
        window.Show();
        var error = await window.WaitForDialogAsync<ErrorDialog>();
        var errorText = error.Text("MessageText");
        error.ClickDialogButton("Close");
        var setup = await window.WaitForDialogAsync<SnapshotPassphraseWindow>();
        setup.SubmitPassphrase(Passphrase, "Save");
        await DesktopWorkspace.WaitForOverviewAsync(window);

        // Assert
        Assert.Contains("remembered snapshot key is unavailable", errorText);
        Assert.True(window.EncryptionConfigured);
        Assert.Equal(1, keyStore.SaveCount);
    }

    [AvaloniaFact]
    public async Task Open_SetupCanceled_PausesAutomaticExportsAndStaysUnconfigured()
    {
        // Arrange
        using var workspace = new DesktopWorkspace();
        workspace.SeedStore();
        workspace.WriteSettings(snapshotPath: workspace.SnapshotDirectory);
        var keyStore = new FakeKeyStore();
        var window = workspace.CreateWindowWithKeyStore(keyStore);

        // Act
        window.Show();
        var setup = await window.WaitForDialogAsync<SnapshotPassphraseWindow>();
        setup.ClickDialogButton("Cancel");
        var paused = await window.WaitForDialogAsync<ErrorDialog>();
        var pausedText = paused.Text("MessageText");
        paused.ClickDialogButton("Close");
        await DesktopWorkspace.WaitForOverviewAsync(window);

        // Assert
        Assert.Contains("paused until a passphrase is configured", pausedText);
        Assert.False(window.EncryptionConfigured);
        Assert.Equal(0, keyStore.SaveCount);
    }

    [AvaloniaFact]
    public async Task Open_NoFoldersConfiguredAndNoKey_DoesNotAskForAPassphrase()
    {
        // Arrange
        using var workspace = new DesktopWorkspace();
        workspace.SeedStore();
        workspace.WriteSettings();
        var window = workspace.CreateWindowWithKeyStore(new FakeKeyStore());

        // Act
        await DesktopWorkspace.OpenAsync(window);

        // Assert
        Assert.Empty(window.OwnedWindows);
        Assert.False(window.EncryptionConfigured);
    }

    [AvaloniaFact]
    public async Task Open_RememberedKeyAvailable_UsesItWithoutAnyDialog()
    {
        // Arrange
        using var workspace = new DesktopWorkspace();
        workspace.SeedStore();
        workspace.WriteSettings(backupPath: workspace.BackupDirectory);
        var window = workspace.CreateWindowWithKeyStore(new FakeKeyStore { Stored = DesktopWorkspace.CreateKey() });

        // Act
        await DesktopWorkspace.OpenAsync(window);

        // Assert
        Assert.Empty(window.OwnedWindows);
        Assert.True(window.EncryptionConfigured);
    }

    [AvaloniaFact]
    public async Task ConfigureEncryption_SavingTheKeyFails_ReportsItAndKeepsThePreviousKey()
    {
        // Arrange
        using var workspace = new DesktopWorkspace();
        workspace.SeedStore();
        workspace.WriteSettings();
        var keyStore = new FakeKeyStore { SaveFailure = new IOException("disk full") };
        var window = workspace.CreateWindowWithKeyStore(keyStore);
        await DesktopWorkspace.OpenAsync(window);
        window.UseSnapshotKey(DesktopWorkspace.CreateKey());

        // Act
        var operation = window.ConfigureEncryption(window);
        var setup = await window.WaitForDialogAsync<SnapshotPassphraseWindow>();
        setup.SubmitPassphrase(Passphrase, "Save");
        var error = await window.WaitForDialogAsync<ErrorDialog>();
        var errorText = error.Text("MessageText");
        error.ClickDialogButton("Close");
        await DesktopWorkspace.WaitUntilAsync(() => operation.IsCompleted, "setup to finish");

        // Assert
        Assert.False(await operation);
        Assert.Contains("could not be saved", errorText);
        Assert.True(window.EncryptionConfigured);
    }

    [AvaloniaTheory]
    [InlineData("short", "short", "Use at least 12 characters for the passphrase.")]
    [InlineData("long enough phrase", "different phrase!", "The passphrases do not match or are empty.")]
    public async Task PassphraseSetup_InvalidInput_ShowsTheErrorAndStaysOpen(string passphrase, string confirmation, string expected)
    {
        // Arrange
        using var workspace = new DesktopWorkspace();
        workspace.SeedStore();
        workspace.WriteSettings();
        var window = await workspace.OpenConfiguredWindowAsync(withSnapshotKey: false);
        var asking = SnapshotPassphraseWindow.Ask(window, setup: true);
        var dialog = await window.WaitForDialogAsync<SnapshotPassphraseWindow>();

        // Act
        dialog.SubmitPassphrase(passphrase, "Save", confirmation);

        // Assert
        Assert.Equal(expected, dialog.Text("Error"));
        Assert.False(asking.IsCompleted);
        dialog.ClickDialogButton("Cancel");
        Assert.Null(await asking);
    }

    [AvaloniaFact]
    public async Task PassphraseUnlock_EmptyThenValid_ReturnsTheEnteredPassphraseAndClearsTheFields()
    {
        // Arrange
        using var workspace = new DesktopWorkspace();
        workspace.SeedStore();
        workspace.WriteSettings();
        var window = await workspace.OpenConfiguredWindowAsync(withSnapshotKey: false);
        var asking = SnapshotPassphraseWindow.Ask(window, setup: false);
        var dialog = await window.WaitForDialogAsync<SnapshotPassphraseWindow>();
        dialog.ClickDialogButton("Unlock");
        var emptyError = dialog.Text("Error");

        // Act
        dialog.Find<TextBox>("Passphrase").Text = "short one";
        dialog.ClickDialogButton("Unlock");

        // Assert
        Assert.Equal("The passphrases do not match or are empty.", emptyError);
        Assert.Equal("short one", await asking);
        Assert.False(dialog.Find<TextBox>("Confirmation").IsVisible);
        Assert.Equal(string.Empty, dialog.Find<TextBox>("Passphrase").Text);
    }
}
