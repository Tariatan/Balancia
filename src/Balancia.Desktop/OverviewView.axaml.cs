using Avalonia.Controls;
using Balancia.Core;
using Balancia.Storage;

using static Balancia.Desktop.InputParsing;
using static Balancia.Desktop.Localization.UiText;

namespace Balancia.Desktop;

/// <summary>
/// The overview: the totals, averages, charts, top categories, reminders and page of transactions for one period and filter.
/// It owns the <see cref="OverviewState"/> that says what is shown and the <see cref="OverviewData"/> that was loaded for it, and draws them.
/// The window wires the card events to its dialogs and decides when to load again.
/// </summary>
public partial class OverviewView : UserControl
{
    private const int TransactionsPageSize = 100;

    public OverviewView()
    {
        InitializeComponent();
        RefreshTrendIntervalLabel();
        FilterRow.PeriodSelected += async period => await ApplyAsync(State.SelectPeriod(period, DisplayDate));
        FilterRow.ValuesCommitted += async () => await ApplyFilterValuesAsync();
        FilterRow.ClearAllRequested += async () => await ApplyAsync(State.Clear());
        AccountsCard.FilterToggled += async (id, selected) => await ApplyAsync(State.ToggleAccount(id, selected));
        CategoriesCard.FilterToggled += async (id, selected) => await ApplyAsync(State.ToggleCategory(id, selected, Data!.Snapshot.Categories));
        CategoriesCard.SelectAllRequested += async () =>
        {
            if (Data is not null)
            {
                await ApplyAsync(State.ToggleAllCategories(Data.Snapshot.Categories));
            }
        };
        TransactionsCard.PreviousPageRequested += async () => await ChangePageAsync(-TransactionsPageSize);
        TransactionsCard.NextPageRequested += async () => await ChangePageAsync(TransactionsPageSize);
        TrendCard.ZoomRequested += zoomOut =>
        {
            var change = State.ZoomTrend(zoomOut);
            if (change != OverviewChange.None)
            {
                ShowChange(change);
                UpdateAnalyticsCharts();
            }
        };
        SettingsButton.Click += (_, _) => SettingsRequested?.Invoke();
    }

    /// <summary>Asks for the settings dialog.</summary>
    internal event Action? SettingsRequested;

    /// <summary>Asks the window to load again, because what the user chose needs other data. The task ends when the new data is shown.</summary>
    internal event Func<Task>? ReloadRequested;

    /// <summary>What the overview shows: the period, the filter, the page and the trend zoom.</summary>
    internal OverviewState State { get; } = new();

    /// <summary>What was loaded for <see cref="State"/>; null until the first load succeeds.</summary>
    internal OverviewData? Data { get; private set; }

    /// <summary>The day the overview was loaded for; "today" and the month label are shown relative to it.</summary>
    internal DateOnly DisplayDate { get; set; }

    /// <summary>The text of the status line.</summary>
    internal string? StatusText => OverviewStatus.Text;

    /// <summary>Shows <paramref name="text"/>, in the current language, on the status line.</summary>
    internal void ShowStatus(string text) => OverviewStatus.Text = Get(text);

    /// <summary>Loads what <see cref="State"/> asks for as of <paramref name="today"/>, which becomes <see cref="DisplayDate"/>.</summary>
    internal async Task LoadAsync(LedgerStore store, DateOnly today)
    {
        DisplayDate = today;
        Data = await OverviewLoader.LoadAsync(store, State.Query(today));
    }

    /// <summary>Draws everything from <see cref="Data"/>. <paramref name="defaultAccountId"/> marks the default account in the accounts card.</summary>
    internal void Render(string? defaultAccountId)
    {
        var data = Data!;
        var snapshot = data.Snapshot;
        Localize();
        AccountsCard.Render(snapshot.Accounts, snapshot.NetWorth, defaultAccountId, State.IsAccountSelected);
        CategoriesCard.Render(snapshot.Categories, State.IsCategorySelected);
        RemindersCard.Render(data.Reminders, DisplayDate);
        TopExpendituresCard.Render(snapshot.LargestCategories, TopExpendituresScope(snapshot));
        SyncFilterInputs();
        SyncFilterDates();
        FilterRow.ForgetShownPeriod();
        TransactionsCard.Invalidate();
        RenderInPlace();
    }

    /// <summary>Draws the parts that follow <see cref="Data"/> without redrawing the lists, so a selection and a scroll position survive.</summary>
    internal void RenderInPlace()
    {
        var data = Data!;
        RenderSummaryCards(data);
        UpdateAnalyticsCharts();
        UpdateFilterButtons();
        TopExpendituresCard.RenderIfChanged(data.Snapshot.LargestCategories, TopExpendituresScope(data.Snapshot));
        TransactionsCard.Render(data.Transactions, State.Offset);
    }

    /// <summary>Brings the controls that show the state in line with what <paramref name="change"/> says changed.</summary>
    internal void ShowChange(OverviewChange change)
    {
        if (change.HasFlag(OverviewChange.PeriodButtons))
        {
            UpdateFilterButtons();
        }

        if (change.HasFlag(OverviewChange.TrendInterval))
        {
            RefreshTrendIntervalLabel();
        }

        if (change.HasFlag(OverviewChange.Inputs))
        {
            SyncFilterInputs();
        }

        if (change.HasFlag(OverviewChange.Dates))
        {
            SyncFilterDates();
        }

        if (change.HasFlag(OverviewChange.AccountChecks))
        {
            AccountsCard.SyncFilterChecks();
        }

        if (change.HasFlag(OverviewChange.CategoryChecks))
        {
            CategoriesCard.SyncFilterChecks();
        }
    }

    /// <summary>Shows <paramref name="change"/> at once, then asks for a reload when it needs one.</summary>
    private async Task ApplyAsync(OverviewChange change)
    {
        ShowChange(change);
        if (change.HasFlag(OverviewChange.Reload) && ReloadRequested is { } reload)
        {
            await reload();
        }
    }

    private Task ApplyFilterValuesAsync()
    {
        var values = FilterRow.Read();
        return ApplyAsync(State.ApplyFilterValues(values.Description, values.From, values.To, values.Minimum, values.Maximum, DisplayDate));
    }

    private async Task ChangePageAsync(int delta)
    {
        State.ChangePage(delta);
        if (ReloadRequested is { } reload)
        {
            await reload();
        }

        TransactionsCard.ScrollToTop();
    }

    private void Localize()
    {
        ToolTip.SetTip(SettingsButton, Get("Open settings"));
        IncomeCard.Title = Get("Income");
        ExpensesCard.Title = Get("Expenses");
        AverageIncomeCard.Title = Get("Average");
        AverageExpensesCard.Title = Get("Average");
        FilterRow.Localize();
        TransactionsCard.Localize();
        RefreshTrendIntervalLabel();
    }

    private void RenderSummaryCards(OverviewData data)
    {
        var snapshot = data.Snapshot;
        var scope = Get(ScopeLabel());
        var averageScope = Get(AverageScopeLabel());
        SummaryLabel.Text = scope;
        IncomeCard.Value = AmountText(snapshot.MonthlyIncome);
        IncomeCard.Scope = scope;
        ExpensesCard.Value = AmountText(snapshot.MonthlyExpenses);
        ExpensesCard.Scope = scope;
        AverageIncomeCard.Value = AmountText(data.Averages.Income);
        AverageIncomeCard.Scope = averageScope;
        AverageExpensesCard.Value = AmountText(data.Averages.Expenses);
        AverageExpensesCard.Scope = averageScope;
    }

    private string? TopExpendituresScope(LedgerSnapshot snapshot) =>
        State.FilteredParent(snapshot.Categories)?.Name;

    private string OverviewPeriodLabel() => State.Period switch
    {
        OverviewPeriod.All => "All dates",
        OverviewPeriod.Day => FlowChartModel.DayText(DisplayDate),
        OverviewPeriod.ThisWeek => "This week",
        OverviewPeriod.ThisMonth => DisplayDate.ToString("MMMM yyyy", Culture),
        OverviewPeriod.ThisYear => DisplayDate.Year.ToString(Culture),
        OverviewPeriod.Last30Days => "Last 30 days",
        _ => $"{State.CustomFrom?.ToString(FlowChartModel.DayFormat, Culture)} – {State.CustomTo?.ToString(FlowChartModel.DayFormat, Culture)}"
    };

    private string ScopeLabel() =>
        State.IsFiltered(DisplayDate) ? Get("Filtered transactions") : Get(OverviewPeriodLabel());

    private string AverageScopeLabel() => OverviewPeriods.IntervalFor(State.Period) switch
    {
        AverageInterval.Day => "Per calendar day",
        AverageInterval.Week => "Per calendar week",
        AverageInterval.Year => "Per calendar year",
        _ => "Per calendar month",
    };

    private void UpdateFilterButtons() => FilterRow.ShowPeriod(State.Period);

    private void SyncFilterInputs() => FilterRow.ShowInputs(State.Filter);

    private void SyncFilterDates()
    {
        var range = State.Range(DisplayDate);
        FilterRow.ShowDates(State.Filter.From ?? range.From, State.Filter.To ?? range.To);
    }

    private void RefreshTrendIntervalLabel() => TrendCard.ShowInterval(State.TrendInterval);

    private void UpdateAnalyticsCharts()
    {
        if (Data?.Analytics is not { } data)
        {
            return;
        }

        var filter = Data.Query.Filter;
        var focus = FlowChartModel.Focus(filter, data);
        var empty = FlowChartModel.EmptyMessage(filter, data);
        TrendCard.Render(FlowChartModel.Trend(data, State.TrendInterval, focus, empty));
        TimelineCard.Render(FlowChartModel.Timeline(data, focus == FlowFocus.Income, empty));
    }
}
