using Balancia.Core;
using Balancia.Storage;

using static Balancia.Desktop.Localization.UiText;

namespace Balancia.Desktop;

public partial class MainWindow
{
    private readonly OverviewState overviewState = new();

    private string OverviewPeriodLabel() => overviewState.Period switch
    {
        OverviewPeriod.All => "All dates",
        OverviewPeriod.Day => FlowChartModel.DayText(displayDate),
        OverviewPeriod.ThisWeek => "This week",
        OverviewPeriod.ThisMonth => displayDate.ToString("MMMM yyyy", Culture),
        OverviewPeriod.ThisYear => displayDate.Year.ToString(Culture),
        OverviewPeriod.Last30Days => "Last 30 days",
        _ => $"{overviewState.CustomFrom?.ToString(FlowChartModel.DayFormat, Culture)} – {overviewState.CustomTo?.ToString(FlowChartModel.DayFormat, Culture)}"
    };

    private string ScopeLabel() =>
        overviewState.IsFiltered(displayDate) ? Get("Filtered transactions") : Get(OverviewPeriodLabel());

    private string AverageScopeLabel() => OverviewPeriods.IntervalFor(overviewState.Period) switch
    {
        AverageInterval.Day => "Per calendar day",
        AverageInterval.Week => "Per calendar week",
        AverageInterval.Year => "Per calendar year",
        _ => "Per calendar month",
    };

    private void UpdateFilterButtons() => FilterRow.ShowPeriod(overviewState.Period);
}
