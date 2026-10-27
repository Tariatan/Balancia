using System.Globalization;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Balancia.Core;
using Balancia.Storage;

using static Balancia.Desktop.InputParsing;
using static Balancia.Desktop.Localization.UiText;

namespace Balancia.Desktop;

public partial class MainWindow
{
    private static readonly IBrush FilledFilterBrush = Brush.Parse("#263C48");
    private static readonly IBrush EmptyFilterBrush = Brush.Parse("#71838D");

    private (OverviewPeriod Period, Button Button, string TitleKey)[] OverviewPeriodButtons =>
    [
        (OverviewPeriod.All, FilterButtonAll, "All"),
        (OverviewPeriod.Day, FilterButtonToday, "Today"),
        (OverviewPeriod.ThisWeek, FilterButtonWeek, "Week"),
        (OverviewPeriod.ThisMonth, FilterButtonMonth, "Month"),
        (OverviewPeriod.ThisYear, FilterButtonYear, "Year"),
        (OverviewPeriod.Last30Days, FilterButtonLast30Days, "Last 30 days"),
    ];

    private void InitializeOverviewFilters()
    {
        overviewFiltersVisible = true;
        overviewPeriodButtons.Clear();
        foreach (var (period, button, _) in OverviewPeriodButtons)
        {
            overviewPeriodButtons.Add(period, button);
            button.Click += async (_, _) => await SelectOverviewPeriod(period);
        }

        FilterFrom.CalendarClosed += (_, _) => ApplyOverviewFilterValuesAfterCalendarClosed();
        FilterTo.CalendarClosed += (_, _) => ApplyOverviewFilterValuesAfterCalendarClosed();
        FilterFrom.LostFocus += async (_, _) => await ApplyOverviewFilterValues();
        FilterTo.LostFocus += async (_, _) => await ApplyOverviewFilterValues();
        FilterSearch.LostFocus += async (_, _) => await ApplyOverviewFilterValues();
        FilterMinimum.LostFocus += async (_, _) => await ApplyOverviewFilterValues();
        FilterMaximum.LostFocus += async (_, _) => await ApplyOverviewFilterValues();
        FilterSearch.KeyDown += async (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                await ApplyOverviewFilterValues();
            }
        };
        FilterSearch.TextChanged += (_, _) => UpdateTextFilterTone(FilterSearch);
        FilterMinimum.TextChanged += (_, _) => UpdateTextFilterTone(FilterMinimum);
        FilterMaximum.TextChanged += (_, _) => UpdateTextFilterTone(FilterMaximum);
        FilterFrom.SelectedDateChanged += (_, _) => UpdateDateFilterTone(FilterFrom);
        FilterTo.SelectedDateChanged += (_, _) => UpdateDateFilterTone(FilterTo);

        FilterClearSearch.Click += async (_, _) =>
        {
            FilterSearch.Text = string.Empty;
            await ApplyOverviewFilterValues();
        };
        FilterClearAll.Click += async (_, _) => await ClearOverviewFilters();

        SyncOverviewFilterInputs();
        SyncOverviewFilterDates();
        UpdateTextFilterTone(FilterSearch);
        UpdateTextFilterTone(FilterMinimum);
        UpdateTextFilterTone(FilterMaximum);
        UpdateDateFilterTone(FilterFrom);
        UpdateDateFilterTone(FilterTo);
    }

    private void LocalizeFilters()
    {
        foreach (var (_, button, titleKey) in OverviewPeriodButtons)
        {
            button.Content = Get(titleKey);
        }

        FilterFrom.PlaceholderText = Get("Select a date");
        FilterTo.PlaceholderText = Get("Select a date");
        FilterSearch.PlaceholderText = Get("Description");
        FilterMinimum.PlaceholderText = Get("Min amount");
        FilterMaximum.PlaceholderText = Get("Max amount");
        ToolTip.SetTip(FilterClearSearch, Get("Clear search"));
        ToolTip.SetTip(FilterClearAll, Get("Clear filters"));
    }

    private static void UpdateTextFilterTone(TextBox input) =>
        input.Foreground = ToneBrush(!string.IsNullOrWhiteSpace(input.Text));

    private static void UpdateDateFilterTone(CalendarDatePicker input) =>
        input.Foreground = ToneBrush(input.SelectedDate is not null);

    private static IBrush ToneBrush(bool hasValue) => hasValue ? FilledFilterBrush : EmptyFilterBrush;

    private void SyncOverviewFilterInputs()
    {
        updatingFilterControls = true;
        try
        {
            var filter = overviewState.Filter;
            FilterSearch.Text = filter.Description ?? "";
            FilterMinimum.Text = filter.Minimum?.Francs.ToString("0.00", CultureInfo.InvariantCulture) ?? "";
            FilterMaximum.Text = filter.Maximum?.Francs.ToString("0.00", CultureInfo.InvariantCulture) ?? "";
        }
        finally
        {
            updatingFilterControls = false;
        }
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
        if (updatingFilterControls)
        {
            return;
        }

        var application = overviewState.ApplyFilterValues(
            FilterSearch.Text,
            FilterFrom.SelectedDate is not null ? ParseDate(FilterFrom) : null,
            FilterTo.SelectedDate is not null ? ParseDate(FilterTo) : null,
            OptionalMoney(FilterMinimum),
            OptionalMoney(FilterMaximum),
            displayDate);
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

    private void ApplyOverviewFilterValuesAfterCalendarClosed()
    {
        // CalendarClosed can precede the control's final SelectedDate
        // property update on the first click. Run after that UI event turn.
        Dispatcher.UIThread.Post(() => _ = ApplyOverviewFilterValues());
    }
}
