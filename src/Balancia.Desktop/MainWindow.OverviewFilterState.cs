using System.Globalization;
using Avalonia.Controls;
using Avalonia.Media;
using Balancia.Storage;

namespace Balancia.Desktop;

public partial class MainWindow
{
    private enum OverviewPeriod
    {
        All,
        Day,
        ThisWeek,
        ThisMonth,
        ThisYear,
        Custom,
    }

    private HistoryFilter overviewFilter = new();
    private bool overviewFiltersVisible;
    private bool pendingOverviewFilterRefresh;
    private OverviewPeriod overviewPeriod = OverviewPeriod.ThisMonth;
    private DateOnly? customFrom;
    private DateOnly? customTo;

    private readonly Dictionary<OverviewPeriod, Button> overviewPeriodButtons = [];
    private (OverviewPeriod Period, bool FiltersVisible)? renderedPeriodState;

    private CalendarDatePicker? overviewFilterFrom;
    private CalendarDatePicker? overviewFilterTo;
    private bool updatingFilterControls;

    private Task RefreshFilteredOverview() => RefreshCore(true);

    private async Task RequestOverviewFilterRefresh()
    {
        if (busy)
        {
            pendingOverviewFilterRefresh = true;
            return;
        }

        await Run(RefreshFilteredOverview, false);
    }

    private (DateOnly? From, DateOnly? To) OverviewRange() => overviewPeriod switch
    {
        OverviewPeriod.All => (null, null),
        OverviewPeriod.Day => (displayDate, displayDate),
        OverviewPeriod.ThisWeek => WeekRange(displayDate),
        OverviewPeriod.ThisMonth => (new DateOnly(displayDate.Year, displayDate.Month, 1),
            new DateOnly(displayDate.Year, displayDate.Month, 1).AddMonths(1).AddDays(-1)),
        OverviewPeriod.ThisYear => (new DateOnly(displayDate.Year, 1, 1), new DateOnly(displayDate.Year, 12, 31)),
        _ => (_customFrom: customFrom, _customTo: customTo)
    };

    private static (DateOnly From, DateOnly To) WeekRange(DateOnly date)
    {
        var daysSinceMonday = ((int)date.DayOfWeek + 6) % 7;
        var monday = date.AddDays(-daysSinceMonday);
        return (monday, monday.AddDays(6));
    }

    private string OverviewPeriodLabel() => overviewPeriod switch
    {
        OverviewPeriod.All => "All dates",
        OverviewPeriod.Day => displayDate.ToString("dd MMM yyyy", CultureInfo.CurrentCulture),
        OverviewPeriod.ThisWeek => "This week",
        OverviewPeriod.ThisMonth => displayDate.ToString("MMMM yyyy", CultureInfo.CurrentCulture),
        OverviewPeriod.ThisYear => displayDate.Year.ToString(CultureInfo.CurrentCulture),
        _ => $"{customFrom:dd MMM yyyy} – {customTo:dd MMM yyyy}"
    };

    private string OverviewScopeLabel()
    {
        var range = OverviewRange();
        var hasAdditionalFilter = !string.IsNullOrEmpty(overviewFilter.Description) ||
            overviewFilter.HasAccountFilter || overviewFilter.Kind is not null ||
            overviewFilter.HasCategoryFilter || overviewFilter.Minimum is not null ||
            overviewFilter.Maximum is not null ||
            (overviewFilter.From ?? range.From) != range.From ||
            (overviewFilter.To ?? range.To) != range.To;
        return hasAdditionalFilter ? "Filtered transactions" : OverviewPeriodLabel();
    }

    private string OverviewAverageScopeLabel() => overviewPeriod switch
    {
        OverviewPeriod.Day => "Per calendar day",
        OverviewPeriod.ThisWeek => "Per calendar week",
        OverviewPeriod.ThisYear => "Per calendar year",
        _ => "Per calendar month",
    };

    private void UpdateOverviewPeriodButtons()
    {
        var current = (_overviewPeriod: overviewPeriod, _overviewFiltersVisible: overviewFiltersVisible);
        if (renderedPeriodState == current)
        {
            return;
        }

        foreach (var (period, button) in overviewPeriodButtons)
        {
            var selected = period == overviewPeriod ||
                period == OverviewPeriod.Custom && overviewFiltersVisible;
            if (selected)
            {
                button.Background = Brush.Parse("#D8ECF3");
                button.Foreground = Brush.Parse("#1C627E");
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
            overviewFilterFrom?.SelectedDate = (overviewFilter.From ?? range.From)?.ToDateTime(TimeOnly.MinValue);

            overviewFilterTo?.SelectedDate = (overviewFilter.To ?? range.To)?.ToDateTime(TimeOnly.MinValue);
        }
        finally
        {
            updatingFilterControls = false;
        }
    }
}
