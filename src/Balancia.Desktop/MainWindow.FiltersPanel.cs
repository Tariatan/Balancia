using System.Globalization;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Balancia.Storage;

using static Balancia.Desktop.Localization.UiText;

namespace Balancia.Desktop;

public partial class MainWindow
{
    private static readonly (OverviewPeriod Period, string TitleKey)[] OverviewPeriods =
    [
        (OverviewPeriod.All, "All"),
        (OverviewPeriod.Day, "Day"),
        (OverviewPeriod.ThisWeek, "Week"),
        (OverviewPeriod.ThisMonth, "Month"),
        (OverviewPeriod.ThisYear, "Year"),
    ];

    private void InitializeOverviewFilters()
    {
        overviewFiltersVisible = true;
        overviewPeriodButtons.Clear();
        var buttons = new[]
        {
            FilterButtonAll, FilterButtonToday, FilterButtonWeek,
            FilterButtonMonth, FilterButtonYear
        };
        for (var i = 0; i < OverviewPeriods.Length; i++)
        {
            var period = OverviewPeriods[i].Period;
            var button = buttons[i];
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
        FilterSearch.TextChanged += (_, _) =>
            UpdateFilterTone(FilterSearch, !string.IsNullOrWhiteSpace(FilterSearch.Text));
        FilterMinimum.TextChanged += (_, _) =>
            UpdateFilterTone(FilterMinimum, !string.IsNullOrWhiteSpace(FilterMinimum.Text));
        FilterMaximum.TextChanged += (_, _) =>
            UpdateFilterTone(FilterMaximum, !string.IsNullOrWhiteSpace(FilterMaximum.Text));
        FilterFrom.SelectedDateChanged += (_, _) =>
            UpdateFilterTone(FilterFrom, FilterFrom.SelectedDate is not null);
        FilterTo.SelectedDateChanged += (_, _) =>
            UpdateFilterTone(FilterTo, FilterTo.SelectedDate is not null);

        FilterClearSearch.Click += async (_, _) =>
        {
            FilterSearch.Text = string.Empty;
            await ApplyOverviewFilterValues();
        };
        FilterClearAll.Click += async (_, _) => await ClearOverviewFilters();

        SyncOverviewFilterInputs();
        SyncOverviewFilterDates();
        UpdateFilterTone(FilterSearch, !string.IsNullOrWhiteSpace(FilterSearch.Text));
        UpdateFilterTone(FilterMinimum, !string.IsNullOrWhiteSpace(FilterMinimum.Text));
        UpdateFilterTone(FilterMaximum, !string.IsNullOrWhiteSpace(FilterMaximum.Text));
        UpdateFilterTone(FilterFrom, FilterFrom.SelectedDate is not null);
        UpdateFilterTone(FilterTo, FilterTo.SelectedDate is not null);
    }

    private void LocalizeFilters()
    {
        FilterButtonAll.Content = Get("All");
        FilterButtonToday.Content = Get("Day");
        FilterButtonWeek.Content = Get("Week");
        FilterButtonMonth.Content = Get("Month");
        FilterButtonYear.Content = Get("Year");
        FilterFrom.PlaceholderText = Get("Select a date");
        FilterTo.PlaceholderText = Get("Select a date");
        FilterSearch.PlaceholderText = Get("Description");
        FilterMinimum.PlaceholderText = Get("Min amount");
        FilterMaximum.PlaceholderText = Get("Max amount");
        ToolTip.SetTip(FilterClearSearch, Get("Clear search"));
        ToolTip.SetTip(FilterClearAll, Get("Clear filters"));
    }

    private static void UpdateFilterTone(Control control, bool hasValue)
    {
        var foreground = Brush.Parse(hasValue ? "#263C48" : "#71838D");
        switch (control)
        {
            case TextBox textBox:
                textBox.Foreground = foreground;
                break;
            case ComboBox comboBox:
                comboBox.Foreground = foreground;
                break;
            case CalendarDatePicker datePicker:
                datePicker.Foreground = foreground;
                break;
        }
    }

    private void SyncOverviewFilterInputs()
    {
        updatingFilterControls = true;
        try
        {
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
        overviewPeriod = period;
        offset = 0;
        var range = OverviewRange();
        filter = filter with
        {
            From = range.From,
            To = range.To
        };
        SetDefaultTrendInterval(period, range.From, range.To);
        SyncOverviewFilterDates();
        UpdateFilterButtons();
        await RequestOverviewFilterRefresh();
    }

    private async Task ClearOverviewFilters()
    {
        filter = new TransactionsFilter();
        offset = 0;
        if (overviewPeriod == OverviewPeriod.Custom)
        {
            overviewPeriod = OverviewPeriod.ThisMonth;
            customFrom = null;
            customTo = null;
            UpdateFilterButtons();
        }

        SyncOverviewFilterInputs();
        SyncOverviewFilterDates();
        SyncCategoryFilterChecks();
        SyncAccountFilterChecks();
        await RequestOverviewFilterRefresh();
    }

    private async Task ApplyOverviewFilterValues()
    {
        if (updatingFilterControls)
        {
            return;
        }

        var periodRange = OverviewRange();
        var nextFilter = filter with
        {
            Description = string.IsNullOrWhiteSpace(FilterSearch.Text) ? null : FilterSearch.Text,
            From = FilterFrom.SelectedDate is not null ? ParseDate(FilterFrom) : null,
            To = FilterTo.SelectedDate is not null ? ParseDate(FilterTo) : null,
            Minimum = OptionalMoney(FilterMinimum),
            Maximum = OptionalMoney(FilterMaximum)
        };
        if (nextFilter == filter)
        {
            return;
        }

        if (nextFilter.From != periodRange.From || nextFilter.To != periodRange.To)
        {
            customFrom = nextFilter.From;
            customTo = nextFilter.To;
            overviewPeriod = OverviewPeriod.Custom;
            SetDefaultTrendInterval(overviewPeriod, nextFilter.From, nextFilter.To);
            UpdateFilterButtons();
        }

        filter = nextFilter;
        offset = 0;
        SyncCategoryFilterChecks();
        SyncAccountFilterChecks();
        await RequestOverviewFilterRefresh();
    }

    private void ApplyOverviewFilterValuesAfterCalendarClosed()
    {
        // CalendarClosed can precede the control's final SelectedDate
        // property update on the first click. Run after that UI event turn.
        Dispatcher.UIThread.Post(() => _ = ApplyOverviewFilterValues());
    }
}
