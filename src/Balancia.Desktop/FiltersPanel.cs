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

    /// <summary>Brings the controls that show the overview state in line with what <paramref name="change"/> says changed.</summary>
    private void ShowOverviewChange(OverviewChange change)
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
            SyncOverviewFilterInputs();
        }

        if (change.HasFlag(OverviewChange.Dates))
        {
            SyncOverviewFilterDates();
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

    /// <summary>Shows <paramref name="change"/> at once, then reloads the data when it asks for that.</summary>
    private async Task ApplyOverviewChange(OverviewChange change)
    {
        ShowOverviewChange(change);
        if (change.HasFlag(OverviewChange.Reload))
        {
            await operations.RequestRefreshAsync();
        }
    }

    private Task SelectOverviewPeriod(OverviewPeriod period) => ApplyOverviewChange(overviewState.SelectPeriod(period, displayDate));

    private Task ClearOverviewFilters() => ApplyOverviewChange(overviewState.Clear());

    private Task ApplyOverviewFilterValues()
    {
        var values = FilterRow.Read();
        return ApplyOverviewChange(overviewState.ApplyFilterValues(values.Description, values.From, values.To, values.Minimum, values.Maximum, displayDate));
    }
}
