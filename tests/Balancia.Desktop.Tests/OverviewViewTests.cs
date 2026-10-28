using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.LogicalTree;
using Balancia.Core;
using Balancia.Storage;
using Xunit;
using static Balancia.Desktop.Localization.UiText;

namespace Balancia.Desktop.Tests;

public sealed class OverviewViewTests : IDisposable
{
    private readonly string path = Path.Combine(Path.GetTempPath(), $"balancia-overview-view-{Guid.NewGuid():N}.db");
    private readonly LedgerStore store;
    private readonly string account;
    private static DateOnly Today => LedgerSeed.Today;

    public OverviewViewTests()
    {
        store = new LedgerStore(path);
        store.Initialize();
        account = LedgerSeed.Account(store);
    }

    public void Dispose()
    {
        foreach (var file in Directory.GetFiles(Path.GetTempPath(), Path.GetFileName(path) + "*"))
        {
            File.Delete(file);
        }
    }

    private static OverviewView Show()
    {
        var view = new OverviewView();
        new Window { Width = 1600, Height = 1000, Content = view }.Show();
        return view;
    }

    private async Task<OverviewView> LoadedAsync()
    {
        var view = Show();
        await view.LoadAsync(store, Today);
        view.Render(null);
        ((Window)TopLevel.GetTopLevel(view)!).UpdateLayout();
        return view;
    }

    private static T Find<T>(OverviewView view, string name) where T : Control => view.FindControl<T>(name)!;

    private static string[] Highlighted(OverviewView view) =>
        [.. new[] { "FilterButtonAll", "FilterButtonToday", "FilterButtonWeek", "FilterButtonMonth", "FilterButtonYear", "FilterButtonLast30Days" }
            .Where(name => view.FilterRow.FindControl<Button>(name)!.FontWeight == FontWeight.SemiBold)];

    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    // Opening period

    [AvaloniaFact]
    public async Task StartAsync_TheMonthHasTransactions_KeepsThisMonth()
    {
        // Arrange
        LedgerSeed.Expense(store, account, "Coffee", Today);
        var view = Show();

        // Act
        await view.StartAsync(store, Today);

        // Assert
        Assert.Equal(OverviewPeriod.ThisMonth, view.State.Period);
        Assert.Equal(Today, view.DisplayDate);
    }

    [AvaloniaFact]
    public async Task StartAsync_TheMonthHasNoTransactions_StartsOnTheLast30Days()
    {
        // Arrange
        LedgerSeed.Expense(store, account, "Rent", Today.AddDays(-40));
        var view = Show();

        // Act
        await view.StartAsync(store, Today);

        // Assert
        Assert.Equal(OverviewPeriod.Last30Days, view.State.Period);
        Assert.Equal(Today, view.DisplayDate);
    }

    // Drawing

    [AvaloniaFact]
    public async Task Render_ShowsTheTotalsAndTheScopeOfThePeriod()
    {
        // Arrange
        LedgerSeed.Expense(store, account, "Coffee", Today, 12.5m);
        var view = Show();
        view.State.SelectPeriod(OverviewPeriod.All, Today);

        // Act
        await view.LoadAsync(store, Today);
        view.Render(null);

        // Assert
        Assert.Equal(Get("All dates"), Find<TextBlock>(view, "SummaryLabel").Text);
        Assert.Equal("12.50", Find<StatCard>(view, "ExpensesCard").Value);
        Assert.Equal(Get("All dates"), Find<StatCard>(view, "ExpensesCard").Scope);
        Assert.Equal(Get("Per calendar month"), Find<StatCard>(view, "AverageExpensesCard").Scope);
    }

    [AvaloniaFact]
    public async Task Render_AFilteredOverview_SaysItShowsFilteredTransactions()
    {
        // Arrange
        LedgerSeed.Expense(store, account, "Coffee", Today);
        LedgerSeed.Expense(store, account, "Rent", Today);
        var view = Show();
        view.State.ApplyFilterValues("Coffee", null, null, null, null, Today);

        // Act
        await view.LoadAsync(store, Today);
        view.Render(null);

        // Assert
        Assert.Equal(Get("Filtered transactions"), Find<TextBlock>(view, "SummaryLabel").Text);
    }

    [AvaloniaFact]
    public async Task ShowChange_PeriodButtons_HighlightsTheButtonOfTheCurrentPeriod()
    {
        // Arrange
        var view = await LoadedAsync();
        var change = view.State.SelectPeriod(OverviewPeriod.All, Today);

        // Act
        view.ShowChange(change);

        // Assert
        Assert.Equal(["FilterButtonAll"], Highlighted(view));
    }

    [AvaloniaFact]
    public async Task ShowChange_Inputs_ShowsTheStoredSearchText()
    {
        // Arrange
        var view = await LoadedAsync();
        var search = view.FilterRow.FindControl<TextBox>("FilterSearch")!;
        search.Text = "Coffee";
        view.State.ApplyFilterValues("Coffee", null, null, null, null, Today);
        var change = view.State.Clear();

        // Act
        view.ShowChange(change);

        // Assert
        Assert.Equal("", search.Text);
    }

    [AvaloniaFact]
    public async Task ShowChange_Nothing_LeavesTheControlsAlone()
    {
        // Arrange
        var view = await LoadedAsync();
        var before = Highlighted(view);

        // Act
        view.ShowChange(OverviewChange.None);

        // Assert
        Assert.Equal(before, Highlighted(view));
    }

    // Choices

    [AvaloniaFact]
    public async Task PeriodButton_Pressed_ChangesTheStateShowsItAndAsksForAReload()
    {
        // Arrange
        var view = await LoadedAsync();
        var reloads = 0;
        view.ReloadRequested += () =>
        {
            reloads++;
            return Task.CompletedTask;
        };

        // Act
        Click(view.FilterRow.FindControl<Button>("FilterButtonAll")!);

        // Assert
        Assert.Equal(OverviewPeriod.All, view.State.Period);
        Assert.Equal(["FilterButtonAll"], Highlighted(view));
        Assert.Equal(1, reloads);
    }

    [AvaloniaFact]
    public async Task AccountCheckbox_Ticked_FiltersByThatAccountAndAsksForAReload()
    {
        // Arrange
        var view = await LoadedAsync();
        var reloads = 0;
        view.ReloadRequested += () =>
        {
            reloads++;
            return Task.CompletedTask;
        };
        var check = view.AccountsCard.FindControl<ListBox>("AccountsList")!.GetLogicalDescendants().OfType<CheckBox>().Single();

        // Act
        check.IsChecked = true;

        // Assert
        Assert.True(view.State.IsAccountSelected(account));
        Assert.Equal(1, reloads);
    }

    [AvaloniaFact]
    public async Task NextPage_Pressed_MovesTheOffsetAndAsksForAReload()
    {
        // Arrange
        for (var index = 1; index <= 150; index++)
        {
            LedgerSeed.Expense(store, account, $"Purchase {index:000}", Today);
        }

        var view = await LoadedAsync();
        var reloads = 0;
        view.ReloadRequested += () =>
        {
            reloads++;
            return Task.CompletedTask;
        };

        // Act
        Click(view.TransactionsCard.FindControl<Button>("TransactionsNextPage")!);

        // Assert
        Assert.Equal(100, view.State.Offset);
        Assert.Equal(1, reloads);
    }

    [AvaloniaFact]
    public async Task Choice_WithoutASubscriber_StillChangesTheState()
    {
        // Arrange
        var view = await LoadedAsync();

        // Act
        Click(view.FilterRow.FindControl<Button>("FilterButtonAll")!);

        // Assert
        Assert.Equal(OverviewPeriod.All, view.State.Period);
    }

    // Status and settings

    [AvaloniaFact]
    public void ShowStatus_ShowsTheTextInTheCurrentLanguage()
    {
        // Arrange
        var view = Show();

        // Act
        view.ShowStatus("Saved locally");

        // Assert
        Assert.Equal(Get("Saved locally"), view.StatusText);
        Assert.Equal(Get("Saved locally"), Find<TextBlock>(view, "OverviewStatus").Text);
    }

    [AvaloniaFact]
    public void SettingsButton_Pressed_RaisesSettingsRequested()
    {
        // Arrange
        var view = Show();
        var requests = 0;
        view.SettingsRequested += () => requests++;

        // Act
        Click(Find<Button>(view, "SettingsButton"));

        // Assert
        Assert.Equal(1, requests);
    }
}
