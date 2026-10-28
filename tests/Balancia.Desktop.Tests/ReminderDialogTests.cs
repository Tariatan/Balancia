using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Balancia.Core;
using Xunit;

namespace Balancia.Desktop.Tests;

public sealed class ReminderDialogTests
{
    private static async Task<Window> OpenAddDialogAsync(MainWindow window)
    {
        var dialog = await window.OpenDialogAsync("ReminderAddButton", "Add reminder");
        dialog.Fill("Description (exact match)", "Rent");
        dialog.Fill("Expected date", LedgerSeed.Today.AddDays(3));
        dialog.Fill("Amount", "1500");
        dialog.Fill("Repeat every N months", "1");
        return dialog;
    }

    [AvaloniaFact]
    public async Task Add_WithValidValues_CreatesTheReminder()
    {
        // Arrange
        using var workspace = new DesktopWorkspace();
        var store = workspace.SeedStore();
        var window = await workspace.OpenWindowAsync();
        var dialog = await OpenAddDialogAsync(window);

        // Act
        await dialog.SaveAndCloseAsync(window);
        await DesktopWorkspace.WaitUntilAsync(() => window.Find<ListBox>("RemindersList").ItemCount == 1, "the reminder to appear");

        // Assert
        var template = Assert.Single(store.ReadReminders()).Template;
        Assert.Equal("Rent", template.Description);
        Assert.Equal(Money.FromFrancs(1500m), template.IndicativeAmount);
        Assert.Equal(1, template.IntervalMonths);
        Assert.Equal(LedgerSeed.Today.AddDays(3), template.ExpectedDate);
        Assert.False(window.Find<TextBlock>("RemindersEmpty").IsVisible);
    }

    [AvaloniaTheory]
    [InlineData("Description (exact match)", "  ", "Enter a description.")]
    [InlineData("Amount", "0", "Enter amount greater than zero.")]
    [InlineData("Repeat every N months", "0", "Repeat interval must be positive.")]
    public async Task Add_WithAnInvalidValue_ShowsAnErrorAndCreatesNothing(string label, string value, string expectedError)
    {
        // Arrange
        using var workspace = new DesktopWorkspace();
        var store = workspace.SeedStore();
        var window = await workspace.OpenWindowAsync();
        var dialog = await OpenAddDialogAsync(window);
        dialog.Fill(label, value);

        // Act
        var error = await dialog.SaveExpectingErrorAsync();

        // Assert
        Assert.Equal(expectedError, error);
        Assert.Empty(store.ReadReminders());
    }

    [AvaloniaFact]
    public async Task Add_WithTheDescriptionOfAnActiveReminder_ShowsAnErrorAndCreatesNothing()
    {
        // Arrange
        using var workspace = new DesktopWorkspace();
        var store = workspace.SeedStore();
        store.SaveReminder(null, "Rent", LedgerSeed.Today.AddDays(5), Money.FromFrancs(1m), 1);
        var window = await workspace.OpenWindowAsync();
        var dialog = await OpenAddDialogAsync(window);

        // Act
        var error = await dialog.SaveExpectingErrorAsync();

        // Assert
        Assert.Equal(ReminderRules.DuplicateDescriptionMessage, error);
        Assert.Single(store.ReadReminders());
    }

    [AvaloniaFact]
    public async Task Add_WithTheDescriptionOfAnActiveReminderInAnotherLetterCase_ShowsAnError()
    {
        // Arrange
        using var workspace = new DesktopWorkspace();
        var store = workspace.SeedStore();
        store.SaveReminder(null, "rent", LedgerSeed.Today.AddDays(5), Money.FromFrancs(1m), 1);
        var window = await workspace.OpenWindowAsync();
        var dialog = await OpenAddDialogAsync(window);

        // Act
        var error = await dialog.SaveExpectingErrorAsync();

        // Assert
        Assert.Equal(ReminderRules.DuplicateDescriptionMessage, error);
        Assert.Single(store.ReadReminders());
    }

    [AvaloniaFact]
    public async Task Edit_ByDoubleClickingTheRow_ShowsTheCurrentValuesAndSavesChanges()
    {
        // Arrange
        using var workspace = new DesktopWorkspace();
        var store = workspace.SeedStore();
        store.SaveReminder(null, "Rent", LedgerSeed.Today.AddDays(3), Money.FromFrancs(1500m), 1, false);
        var window = await workspace.OpenWindowAsync();
        var list = window.Find<ListBox>("RemindersList");

        // Act
        window.DoubleClickRow(list, 0);
        var dialog = await window.WaitForDialogAsync("Edit reminder");
        var shownAmount = dialog.TextOf("Amount");
        dialog.Fill("Amount", "1600");
        await dialog.SaveAndCloseAsync(window);

        // Assert
        Assert.Equal("1500.00", shownAmount);
        Assert.Equal(Money.FromFrancs(1600m), Assert.Single(store.ReadReminders()).Template.IndicativeAmount);
    }

    [AvaloniaFact]
    public async Task Delete_SelectedReminder_RemovesIt()
    {
        // Arrange
        using var workspace = new DesktopWorkspace();
        var store = workspace.SeedStore();
        store.SaveReminder(null, "Rent", LedgerSeed.Today.AddDays(3), Money.FromFrancs(1500m), 1, false);
        var window = await workspace.OpenWindowAsync();
        window.Find<ListBox>("RemindersList").SelectedIndex = 0;
        var dialog = await window.OpenDialogAsync("ReminderRemoveButton", "Delete reminder");

        // Act
        await dialog.SaveAndCloseAsync(window, "Delete");
        await DesktopWorkspace.WaitUntilAsync(() => window.Find<TextBlock>("RemindersEmpty").IsVisible, "the reminder list to empty");

        // Assert
        Assert.Empty(store.ReadReminders());
    }

    [AvaloniaFact]
    public async Task Delete_WithNothingSelected_AsksForASelectionInsteadOfOpeningADialog()
    {
        // Arrange
        using var workspace = new DesktopWorkspace();
        workspace.SeedStore().SaveReminder(null, "Rent", LedgerSeed.Today.AddDays(3), Money.FromFrancs(1500m), 1, false);
        var window = await workspace.OpenWindowAsync();

        // Act
        window.Click("ReminderRemoveButton");
        await DesktopWorkspace.WaitUntilAsync(() => window.Find<TextBlock>("OverviewStatus").Text == "Select a row first.", "the prompt");

        // Assert
        Assert.Empty(window.OwnedWindows);
    }
}
