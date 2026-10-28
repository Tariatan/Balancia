using Balancia.Core;
using Balancia.Storage;

using static Balancia.Desktop.InputParsing;

namespace Balancia.Desktop;

public partial class MainWindow
{
    private void InitializeOverviewFilters()
    {
        OverviewPane.FilterRow.PeriodSelected += async period => await SelectOverviewPeriod(period);
        OverviewPane.FilterRow.ValuesCommitted += async () => await ApplyOverviewFilterValues();
        OverviewPane.FilterRow.ClearAllRequested += async () => await ClearOverviewFilters();
    }

    /// <summary>Shows <paramref name="change"/> at once, then reloads the data when it asks for that.</summary>
    private async Task ApplyOverviewChange(OverviewChange change)
    {
        OverviewPane.ShowChange(change);
        if (change.HasFlag(OverviewChange.Reload))
        {
            await operations.RequestRefreshAsync();
        }
    }

    private Task SelectOverviewPeriod(OverviewPeriod period) => ApplyOverviewChange(OverviewPane.State.SelectPeriod(period, OverviewPane.DisplayDate));

    private Task ClearOverviewFilters() => ApplyOverviewChange(OverviewPane.State.Clear());

    private Task ApplyOverviewFilterValues()
    {
        var values = OverviewPane.FilterRow.Read();
        return ApplyOverviewChange(OverviewPane.State.ApplyFilterValues(values.Description, values.From, values.To, values.Minimum, values.Maximum, OverviewPane.DisplayDate));
    }
}
