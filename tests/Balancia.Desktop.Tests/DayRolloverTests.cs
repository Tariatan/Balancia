using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Xunit;

namespace Balancia.Desktop.Tests;

/// <summary>The window re-reads "today" when the calendar day changes while the app stays open.</summary>
public sealed class DayRolloverTests
{
    // Late March of this year: always in the past, so the ledger accepts transactions dated on it.
    private static DateTimeOffset LastDayOfMarch => new(DateTime.Today.Year, 3, 31, 12, 0, 0, TimeSpan.Zero);

    private static (DateOnly Day, string Account) SeedMarchTransaction(DesktopWorkspace workspace)
    {
        var store = workspace.SeedStore();
        var day = DateOnly.FromDateTime(LastDayOfMarch.Date);
        var account = LedgerSeed.Account(store, opened: day.AddDays(-500));
        LedgerSeed.Expense(store, account, "March purchase", day);
        LedgerSeed.Expense(store, account, "Last year", day.AddDays(-400));
        return (day, account);
    }

    [AvaloniaFact]
    public async Task RefreshIfDayChanged_AfterMidnight_MovesTheMonthLabelToTheNewMonth()
    {
        // Arrange
        using var workspace = new DesktopWorkspace();
        SeedMarchTransaction(workspace);
        var clock = new ManualClock(LastDayOfMarch);
        var window = workspace.CreateSeparateFolderWindow(clock);
        await DesktopWorkspace.OpenAsync(window);
        var summary = window.Find<TextBlock>("SummaryLabel");
        Assert.StartsWith("March", summary.Text);

        // Act
        clock.Advance(TimeSpan.FromDays(1));
        await window.RefreshIfDayChanged();

        // Assert
        Assert.StartsWith("April", summary.Text);
        window.Close();
    }

    [AvaloniaFact]
    public async Task RefreshIfDayChanged_OnTheSameDay_DoesNotReloadTheLedger()
    {
        // Arrange
        using var workspace = new DesktopWorkspace();
        var (day, account) = SeedMarchTransaction(workspace);
        var clock = new ManualClock(LastDayOfMarch);
        var window = workspace.CreateSeparateFolderWindow(clock);
        await DesktopWorkspace.OpenAsync(window);
        LedgerSeed.Expense(workspace.SeedStore(), account, "Added behind the window", day);

        // Act
        clock.Advance(TimeSpan.FromHours(6));
        await window.RefreshIfDayChanged();

        // Assert
        Assert.Equal(1, window.TransactionCount());
        window.Close();
    }

    [AvaloniaFact]
    public async Task RefreshIfDayChanged_AfterMidnight_PicksUpChangesMadeBehindTheWindow()
    {
        // Arrange
        using var workspace = new DesktopWorkspace();
        var (day, account) = SeedMarchTransaction(workspace);
        var clock = new ManualClock(LastDayOfMarch);
        var window = workspace.CreateSeparateFolderWindow(clock);
        await DesktopWorkspace.OpenAsync(window);
        window.Click("FilterButtonAll");
        // "All" adds the older transaction, so waiting for the second row also waits for that refresh to finish.
        await DesktopWorkspace.WaitUntilAsync(() => window.TransactionCount() == 2, "the full list");
        LedgerSeed.Expense(workspace.SeedStore(), account, "Added behind the window", day);

        // Act
        clock.Advance(TimeSpan.FromDays(1));
        await window.RefreshIfDayChanged();

        // Assert
        Assert.Equal(3, window.TransactionCount());
        window.Close();
    }
}
