using Avalonia.Controls;
using Avalonia.Media;
using Balancia.Core;
using Balancia.Storage;

using static Balancia.Desktop.Localization.UiText;

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

    private TransactionsFilter filter = new();
    private bool overviewFiltersVisible;
    private bool pendingOverviewFilterRefresh;
    private OverviewPeriod overviewPeriod = OverviewPeriod.ThisMonth;
    private DateOnly? customFrom;
    private DateOnly? customTo;

    private readonly Dictionary<OverviewPeriod, Button> overviewPeriodButtons = [];
    private (OverviewPeriod Period, bool FiltersVisible)? renderedPeriodState;

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
        OverviewPeriod.ThisWeek => (CalendarWeek.StartOfWeek(displayDate), CalendarWeek.EndOfWeek(displayDate)),
        OverviewPeriod.ThisMonth => (new DateOnly(displayDate.Year, displayDate.Month, 1),
            new DateOnly(displayDate.Year, displayDate.Month, 1).AddMonths(1).AddDays(-1)),
        OverviewPeriod.ThisYear => (new DateOnly(displayDate.Year, 1, 1), new DateOnly(displayDate.Year, 12, 31)),
        _ => (_customFrom: customFrom, _customTo: customTo)
    };

    private string OverviewPeriodLabel() => overviewPeriod switch
    {
        OverviewPeriod.All => "All dates",
        OverviewPeriod.Day => displayDate.ToString("dd MMM yyyy", Culture),
        OverviewPeriod.ThisWeek => "This week",
        OverviewPeriod.ThisMonth => displayDate.ToString("MMMM yyyy", Culture),
        OverviewPeriod.ThisYear => displayDate.Year.ToString(Culture),
        _ => $"{customFrom?.ToString("dd MMM yyyy", Culture)} – {customTo?.ToString("dd MMM yyyy", Culture)}"
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

    private string AverageScopeLabel() => overviewPeriod switch
    {
        OverviewPeriod.Day => "Per calendar day",
        OverviewPeriod.ThisWeek => "Per calendar week",
        OverviewPeriod.ThisYear => "Per calendar year",
        _ => "Per calendar month",
    };

    private void UpdateFilterButtons()
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
            FilterFrom.SelectedDate = (filter.From ?? range.From)?.ToDateTime(TimeOnly.MinValue);
            FilterTo.SelectedDate = (filter.To ?? range.To)?.ToDateTime(TimeOnly.MinValue);
        }
        finally
        {
            updatingFilterControls = false;
        }
    }
}
