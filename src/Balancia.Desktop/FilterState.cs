using Avalonia.Controls;
using Avalonia.Media;
using Balancia.Core;
using Balancia.Storage;

using static Balancia.Desktop.Localization.UiText;

namespace Balancia.Desktop;

public partial class MainWindow
{
    private readonly OverviewState overviewState = new();
    private bool overviewFiltersVisible;
    private bool pendingOverviewFilterRefresh;

    private readonly Dictionary<OverviewPeriod, Button> overviewPeriodButtons = [];
    private (OverviewPeriod Period, bool FiltersVisible)? renderedPeriodState;

    private bool updatingFilterControls;

    private static readonly IBrush SelectedPeriodBackground = Brush.Parse("#D8ECF3");
    private static readonly IBrush SelectedPeriodForeground = Brush.Parse("#1C627E");

    private async Task RequestOverviewFilterRefresh()
    {
        if (busy)
        {
            pendingOverviewFilterRefresh = true;
            return;
        }

        await Run(() => RefreshCore(true), false);
    }

    private string OverviewPeriodLabel() => overviewState.Period switch
    {
        OverviewPeriod.All => "All dates",
        OverviewPeriod.Day => DayText(displayDate),
        OverviewPeriod.ThisWeek => "This week",
        OverviewPeriod.ThisMonth => displayDate.ToString("MMMM yyyy", Culture),
        OverviewPeriod.ThisYear => displayDate.Year.ToString(Culture),
        OverviewPeriod.Last30Days => "Last 30 days",
        _ => $"{overviewState.CustomFrom?.ToString(DayFormat, Culture)} – {overviewState.CustomTo?.ToString(DayFormat, Culture)}"
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

    private void UpdateFilterButtons()
    {
        var current = (overviewState.Period, overviewFiltersVisible);
        if (renderedPeriodState == current)
        {
            return;
        }

        foreach (var (period, button) in overviewPeriodButtons)
        {
            var selected = period == overviewState.Period ||
                (period == OverviewPeriod.Custom && overviewFiltersVisible);
            if (selected)
            {
                button.Background = SelectedPeriodBackground;
                button.Foreground = SelectedPeriodForeground;
                button.FontWeight = FontWeight.SemiBold;
            }
            else
            {
                button.ClearValue(BackgroundProperty);
                button.ClearValue(ForegroundProperty);
                button.ClearValue(FontWeightProperty);
            }
        }

        renderedPeriodState = current;
    }

    private void SyncOverviewFilterDates()
    {
        var range = overviewState.Range(displayDate);
        updatingFilterControls = true;
        try
        {
            FilterFrom.SelectedDate = (overviewState.Filter.From ?? range.From)?.ToDateTime(TimeOnly.MinValue);
            FilterTo.SelectedDate = (overviewState.Filter.To ?? range.To)?.ToDateTime(TimeOnly.MinValue);
        }
        finally
        {
            updatingFilterControls = false;
        }
    }
}
