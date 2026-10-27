using Balancia.Core;
using Balancia.Storage;

using static Balancia.Desktop.InputParsing;

namespace Balancia.Desktop;

public partial class MainWindow
{
    private void InitializeOverviewFilters()
    {
        FilterRow.PeriodSelected += async period => await SelectOverviewPeriod(period);
        FilterRow.ValuesCommitted += async () => await ApplyOverviewFilterValues();
        FilterRow.ClearAllRequested += async () => await ClearOverviewFilters();
        SyncOverviewFilterInputs();
        SyncOverviewFilterDates();
    }

    private void SyncOverviewFilterInputs() => FilterRow.ShowInputs(overviewState.Filter);

    private void SyncOverviewFilterDates()
    {
        var range = overviewState.Range(displayDate);
        FilterRow.ShowDates(overviewState.Filter.From ?? range.From, overviewState.Filter.To ?? range.To);
    }

    private async Task SelectOverviewPeriod(OverviewPeriod period)
    {
        overviewState.SelectPeriod(period, displayDate);
        RefreshTrendIntervalLabel();
        SyncOverviewFilterDates();
        UpdateFilterButtons();
        await operations.RequestRefreshAsync();
    }

    private async Task ClearOverviewFilters()
    {
        var wasCustomRange = overviewState.Period == OverviewPeriod.Custom;
        overviewState.Clear();
        if (wasCustomRange)
        {
            UpdateFilterButtons();
        }

        SyncOverviewFilterInputs();
        SyncOverviewFilterDates();
        CategoriesCard.SyncFilterChecks();
        AccountsCard.SyncFilterChecks();
        await operations.RequestRefreshAsync();
    }

    private async Task ApplyOverviewFilterValues()
    {
        var values = FilterRow.Read();
        var application = overviewState.ApplyFilterValues(values.Description, values.From, values.To, values.Minimum, values.Maximum, displayDate);
        if (application == FilterApplication.Unchanged)
        {
            return;
        }

        if (application == FilterApplication.AppliedAsCustomRange)
        {
            RefreshTrendIntervalLabel();
            UpdateFilterButtons();
        }

        CategoriesCard.SyncFilterChecks();
        AccountsCard.SyncFilterChecks();
        await operations.RequestRefreshAsync();
    }
}
