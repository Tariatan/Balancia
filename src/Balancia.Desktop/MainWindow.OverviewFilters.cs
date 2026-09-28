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
            overviewPeriodButtonAll, overviewPeriodButtonDay, overviewPeriodButtonWeek,
            overviewPeriodButtonMonth, overviewPeriodButtonYear
        };
        for (var i = 0; i < OverviewPeriods.Length; i++)
        {
            var period = OverviewPeriods[i].Period;
            var button = buttons[i];
            overviewPeriodButtons.Add(period, button);
            button.Click += async (_, _) => await SelectOverviewPeriod(period);
        }

        overviewFilterFrom.CalendarClosed += (_, _) => ApplyOverviewFilterValuesAfterCalendarClosed();
        overviewFilterTo.CalendarClosed += (_, _) => ApplyOverviewFilterValuesAfterCalendarClosed();
        overviewFilterFrom.LostFocus += async (_, _) => await ApplyOverviewFilterValues();
        overviewFilterTo.LostFocus += async (_, _) => await ApplyOverviewFilterValues();
        overviewFilterSearch.LostFocus += async (_, _) => await ApplyOverviewFilterValues();
        overviewFilterMinimum.LostFocus += async (_, _) => await ApplyOverviewFilterValues();
        overviewFilterMaximum.LostFocus += async (_, _) => await ApplyOverviewFilterValues();
        overviewFilterSearch.KeyDown += async (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                await ApplyOverviewFilterValues();
            }
        };
        overviewFilterSearch.TextChanged += (_, _) =>
            UpdateFilterTone(overviewFilterSearch, !string.IsNullOrWhiteSpace(overviewFilterSearch.Text));
        overviewFilterMinimum.TextChanged += (_, _) =>
            UpdateFilterTone(overviewFilterMinimum, !string.IsNullOrWhiteSpace(overviewFilterMinimum.Text));
        overviewFilterMaximum.TextChanged += (_, _) =>
            UpdateFilterTone(overviewFilterMaximum, !string.IsNullOrWhiteSpace(overviewFilterMaximum.Text));
        overviewFilterFrom.SelectedDateChanged += (_, _) =>
            UpdateFilterTone(overviewFilterFrom, overviewFilterFrom.SelectedDate is not null);
        overviewFilterTo.SelectedDateChanged += (_, _) =>
            UpdateFilterTone(overviewFilterTo, overviewFilterTo.SelectedDate is not null);

        overviewFilterClearSearch.Click += async (_, _) =>
        {
            overviewFilterSearch.Text = string.Empty;
            await ApplyOverviewFilterValues();
        };
        overviewFilterClearAll.Click += async (_, _) => await ClearOverviewFilters();

        SyncOverviewFilterInputs();
        SyncOverviewFilterDates();
        UpdateFilterTone(overviewFilterSearch, !string.IsNullOrWhiteSpace(overviewFilterSearch.Text));
        UpdateFilterTone(overviewFilterMinimum, !string.IsNullOrWhiteSpace(overviewFilterMinimum.Text));
        UpdateFilterTone(overviewFilterMaximum, !string.IsNullOrWhiteSpace(overviewFilterMaximum.Text));
        UpdateFilterTone(overviewFilterFrom, overviewFilterFrom.SelectedDate is not null);
        UpdateFilterTone(overviewFilterTo, overviewFilterTo.SelectedDate is not null);
    }

    private void LocalizeOverviewFilters()
    {
        overviewPeriodButtonAll.Content = Get("All");
        overviewPeriodButtonDay.Content = Get("Day");
        overviewPeriodButtonWeek.Content = Get("Week");
        overviewPeriodButtonMonth.Content = Get("Month");
        overviewPeriodButtonYear.Content = Get("Year");
        overviewFilterFrom.PlaceholderText = Get("Select a date");
        overviewFilterTo.PlaceholderText = Get("Select a date");
        overviewFilterSearch.PlaceholderText = Get("Description");
        overviewFilterMinimum.PlaceholderText = Get("Min amount");
        overviewFilterMaximum.PlaceholderText = Get("Max amount");
        ToolTip.SetTip(overviewFilterClearSearch, Get("Clear search"));
        ToolTip.SetTip(overviewFilterClearAll, Get("Clear filters"));
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
            overviewFilterSearch.Text = overviewFilter.Description ?? "";
            overviewFilterMinimum.Text = overviewFilter.Minimum?.Francs.ToString("0.00", CultureInfo.InvariantCulture) ?? "";
            overviewFilterMaximum.Text = overviewFilter.Maximum?.Francs.ToString("0.00", CultureInfo.InvariantCulture) ?? "";
        }
        finally
        {
            updatingFilterControls = false;
        }
    }

    private async Task SelectOverviewPeriod(OverviewPeriod period)
    {
        overviewPeriod = period;
        overviewOffset = 0;
        var range = OverviewRange();
        overviewFilter = overviewFilter with
        {
            From = range.From,
            To = range.To
        };
        SetDefaultTrendInterval(period, range.From, range.To);
        SyncOverviewFilterDates();
        UpdateOverviewPeriodButtons();
        await RequestOverviewFilterRefresh();
    }

    private async Task ClearOverviewFilters()
    {
        overviewFilter = new HistoryFilter();
        overviewOffset = 0;
        if (overviewPeriod == OverviewPeriod.Custom)
        {
            overviewPeriod = OverviewPeriod.ThisMonth;
            customFrom = null;
            customTo = null;
            UpdateOverviewPeriodButtons();
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
        var nextFilter = overviewFilter with
        {
            Description = string.IsNullOrWhiteSpace(overviewFilterSearch.Text) ? null : overviewFilterSearch.Text,
            From = overviewFilterFrom.SelectedDate is not null ? ParseDate(overviewFilterFrom) : null,
            To = overviewFilterTo.SelectedDate is not null ? ParseDate(overviewFilterTo) : null,
            Minimum = OptionalMoney(overviewFilterMinimum),
            Maximum = OptionalMoney(overviewFilterMaximum)
        };
        if (nextFilter == overviewFilter)
        {
            return;
        }

        if (nextFilter.From != periodRange.From || nextFilter.To != periodRange.To)
        {
            customFrom = nextFilter.From;
            customTo = nextFilter.To;
            overviewPeriod = OverviewPeriod.Custom;
            SetDefaultTrendInterval(overviewPeriod, nextFilter.From, nextFilter.To);
            UpdateOverviewPeriodButtons();
        }

        overviewFilter = nextFilter;
        overviewOffset = 0;
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
