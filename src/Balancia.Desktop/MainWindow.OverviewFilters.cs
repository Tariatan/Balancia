using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Balancia.Storage;

namespace Balancia.Desktop;

public partial class MainWindow
{
    private Border OverviewFilterPanel()
    {
        var activeFilter = overviewFilter with
        {
            From = overviewFilter.From ?? OverviewRange().From,
            To = overviewFilter.To ?? OverviewRange().To
        };
        var search = Input(activeFilter.Description ?? "");
        search.PlaceholderText = "Description";
        search.Width = 230;
        var from = DateInput(activeFilter.From);
        from.Width = 150;
        var to = DateInput(activeFilter.To);
        to.Width = 150;
        overviewFilterFrom = from;
        overviewFilterTo = to;
        var minimum = Input(activeFilter.Minimum?.Francs.ToString("0.00", CultureInfo.InvariantCulture) ?? "");
        minimum.PlaceholderText = "Min amount";
        minimum.Width = 115;
        var maximum = Input(activeFilter.Maximum?.Francs.ToString("0.00", CultureInfo.InvariantCulture) ?? "");
        maximum.PlaceholderText = "Max amount";
        maximum.Width = 115;

        from.CalendarClosed += (_, _) => ApplyAfterCalendarClosed();
        to.CalendarClosed += (_, _) => ApplyAfterCalendarClosed();
        from.LostFocus += async (_, _) => await ApplyValues();
        to.LostFocus += async (_, _) => await ApplyValues();
        search.LostFocus += async (_, _) => await ApplyValues();
        minimum.LostFocus += async (_, _) => await ApplyValues();
        maximum.LostFocus += async (_, _) => await ApplyValues();
        search.KeyDown += async (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                await ApplyValues();
            }
        };
        search.TextChanged += (_, _) => UpdateFilterTone(search, !string.IsNullOrWhiteSpace(search.Text));
        minimum.TextChanged += (_, _) => UpdateFilterTone(minimum, !string.IsNullOrWhiteSpace(minimum.Text));
        maximum.TextChanged += (_, _) => UpdateFilterTone(maximum, !string.IsNullOrWhiteSpace(maximum.Text));
        from.SelectedDateChanged += (_, _) => UpdateFilterTone(from, from.SelectedDate is not null);
        to.SelectedDateChanged += (_, _) => UpdateFilterTone(to, to.SelectedDate is not null);

        var mainRow = new WrapPanel { Orientation = Orientation.Horizontal };
        overviewPeriodButtons.Clear();
        foreach (var (period, periodTitle) in new[]
        {
            (OverviewPeriod.All, "All"),
            (OverviewPeriod.Day, "Day"),
            (OverviewPeriod.ThisWeek, "Week"),
            (OverviewPeriod.ThisMonth, "Month"),
            (OverviewPeriod.ThisYear, "Year")
        })
        {
            var periodButton = ActionButton(periodTitle, async () =>
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
            });
            periodButton.MinWidth = 70;
            periodButton.HorizontalContentAlignment = HorizontalAlignment.Center;
            periodButton.Margin = new Thickness(0, 2, 7, 2);
            mainRow.Children.Add(periodButton);
            overviewPeriodButtons.Add(period, periodButton);
        }

        from.Margin = new Thickness(0, 2, 7, 2);
        to.Margin = new Thickness(0, 2, 7, 2);
        search.Width = 230;
        search.Margin = new Thickness(0, 2, 0, 2);
        var clearSearch = new Button
        {
            Content = "×",
            Width = 28,
            Height = 34,
            Padding = new Thickness(0),
            FontSize = 17,
            Background = Brushes.Transparent,
            Foreground = Brush.Parse("#71838D"),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 2, 7, 2)
        };
        ToolTip.SetTip(clearSearch, "Clear search");
        clearSearch.Click += async (_, _) =>
        {
            search.Text = string.Empty;
            await ApplyValues();
        };
        mainRow.Children.Add(from);
        mainRow.Children.Add(to);
        mainRow.Children.Add(search);
        mainRow.Children.Add(clearSearch);
        minimum.Margin = new Thickness(0, 2, 7, 2);
        maximum.Margin = new Thickness(0, 2, 7, 2);
        mainRow.Children.Add(minimum);
        mainRow.Children.Add(maximum);

        var clear = ActionButton("🗑", async () =>
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

            updatingFilterControls = true;
            try
            {
                search.Text = string.Empty;
                minimum.Text = string.Empty;
                maximum.Text = string.Empty;
                SyncOverviewFilterDates();
            }
            finally
            {
                updatingFilterControls = false;
            }

            SyncCategoryFilterChecks();
            SyncAccountFilterChecks();
            await RequestOverviewFilterRefresh();
        });
        clear.Width = 30;
        clear.Padding = new Thickness(0);
        clear.FontSize = 16;
        clear.HorizontalContentAlignment = HorizontalAlignment.Center;
        ToolTip.SetTip(clear, "Clear filters");
        clear.VerticalAlignment = VerticalAlignment.Center;
        var filterRow = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto")
        };
        AddColumn(filterRow, mainRow, 0);
        AddColumn(filterRow, clear, 1);

        UpdateFilterTone(search, !string.IsNullOrWhiteSpace(search.Text));
        UpdateFilterTone(minimum, !string.IsNullOrWhiteSpace(minimum.Text));
        UpdateFilterTone(maximum, !string.IsNullOrWhiteSpace(maximum.Text));
        UpdateFilterTone(from, from.SelectedDate is not null);
        UpdateFilterTone(to, to.SelectedDate is not null);

        var panel = Panel(filterRow);
        panel.Padding = new Thickness(10);
        return panel;

        static void UpdateFilterTone(Control control, bool hasValue)
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

        async Task ApplyValues()
        {
            if (updatingFilterControls || overviewFilterFrom != from)
            {
                return;
            }

            var periodRange = OverviewRange();
            var nextFilter = overviewFilter with
            {
                Description = string.IsNullOrWhiteSpace(search.Text) ? null : search.Text,
                From = from.SelectedDate is not null ? ParseDate(from) : null,
                To = to.SelectedDate is not null ? ParseDate(to) : null,
                Minimum = OptionalMoney(minimum),
                Maximum = OptionalMoney(maximum)
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

        void ApplyAfterCalendarClosed()
        {
            // CalendarClosed can precede the control's final SelectedDate
            // property update on the first click. Run after that UI event turn.
            Dispatcher.UIThread.Post(() => _ = ApplyValues());
        }
    }
}
