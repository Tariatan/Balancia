using Avalonia.Controls;
using Avalonia.Media;
using Balancia.Core;
using Balancia.Storage;

using static Balancia.Desktop.Localization.UiText;

namespace Balancia.Desktop;

public partial class MainWindow
{
    private TransactionsFilter filter = new();
    private bool overviewFiltersVisible;
    private bool pendingOverviewFilterRefresh;
    private OverviewPeriod overviewPeriod = OverviewPeriod.ThisMonth;
    private DateOnly? customFrom;
    private DateOnly? customTo;

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

    private DateRange OverviewRange() => OverviewPeriods.Range(overviewPeriod, displayDate, customFrom, customTo);

    private string OverviewPeriodLabel() => overviewPeriod switch
    {
        OverviewPeriod.All => "All dates",
        OverviewPeriod.Day => DayText(displayDate),
        OverviewPeriod.ThisWeek => "This week",
        OverviewPeriod.ThisMonth => displayDate.ToString("MMMM yyyy", Culture),
        OverviewPeriod.ThisYear => displayDate.Year.ToString(Culture),
        OverviewPeriod.Last30Days => "Last 30 days",
        _ => $"{customFrom?.ToString(DayFormat, Culture)} – {customTo?.ToString(DayFormat, Culture)}"
    };

    private string ScopeLabel()
    {
        var range = OverviewRange();
        var hasAdditionalFilter = !string.IsNullOrEmpty(filter.Description) ||
            filter.HasAccountFilter || filter.Kind is not null ||
            filter.HasCategoryFilter || filter.Minimum is not null ||
            filter.Maximum is not null ||
            (filter.From ?? range.From) != range.From ||
            (filter.To ?? range.To) != range.To;
        return hasAdditionalFilter ? Get("Filtered transactions") : Get(OverviewPeriodLabel());
    }

    private string AverageScopeLabel() => OverviewPeriods.IntervalFor(overviewPeriod) switch
    {
        AverageInterval.Day => "Per calendar day",
        AverageInterval.Week => "Per calendar week",
        AverageInterval.Year => "Per calendar year",
        _ => "Per calendar month",
    };

    private void UpdateFilterButtons()
    {
        var current = (overviewPeriod, overviewFiltersVisible);
        if (renderedPeriodState == current)
        {
            return;
        }

        foreach (var (period, button) in overviewPeriodButtons)
        {
            var selected = period == overviewPeriod ||
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
        var range = OverviewRange();
        updatingFilterControls = true;
        try
        {
            FilterFrom.SelectedDate = (filter.From ?? range.From)?.ToDateTime(TimeOnly.MinValue);
            FilterTo.SelectedDate = (filter.To ?? range.To)?.ToDateTime(TimeOnly.MinValue);
        }
        finally
        {
            updatingFilterControls = false;
        }
    }
}
