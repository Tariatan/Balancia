using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Balancia.Core;
using Balancia.Storage;

using static Balancia.Desktop.InputParsing;
using static Balancia.Desktop.Localization.UiText;

namespace Balancia.Desktop;

/// <summary>What the inputs of the filter row hold: the search text, the date range, the transaction type and the amount range.</summary>
internal sealed record FilterRowValues(string? Description, DateOnly? From, DateOnly? To, Money? Minimum, Money? Maximum, TransactionKind? Kind = null);

/// <summary>
/// The filter row: the period buttons, the date range, the transaction type, the search text, the amount range and the clear buttons. It shows what it is given and raises
/// requests; what the filter is, and what a period means, stays with the window.
/// </summary>
public partial class FilterRow : UserControl
{

    private readonly (OverviewPeriod Period, Button Button, string TitleKey)[] periodButtons;
    private OverviewPeriod? shownPeriod;
    private bool updating;

    public FilterRow()
    {
        InitializeComponent();
        periodButtons =
        [
            (OverviewPeriod.All, FilterButtonAll, "All"),
            (OverviewPeriod.Day, FilterButtonToday, "Today"),
            (OverviewPeriod.ThisWeek, FilterButtonWeek, "Week"),
            (OverviewPeriod.ThisMonth, FilterButtonMonth, "Month"),
            (OverviewPeriod.ThisYear, FilterButtonYear, "Year"),
            (OverviewPeriod.Last30Days, FilterButtonLast30Days, "Last 30 days"),
        ];
        foreach (var (period, button, _) in periodButtons)
        {
            button.Click += (_, _) => PeriodSelected?.Invoke(period);
        }

        Localize();
        FilterKind.SelectionChanged += (_, _) => Commit();

        FilterFrom.CalendarClosed += (_, _) => CommitAfterCalendarClosed();
        FilterTo.CalendarClosed += (_, _) => CommitAfterCalendarClosed();
        foreach (Control input in new Control[] { FilterFrom, FilterTo, FilterSearch, FilterMinimum, FilterMaximum })
        {
            input.LostFocus += (_, _) => Commit();
        }

        FilterSearch.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                Commit();
            }
        };
        FilterSearch.TextChanged += (_, _) => ShowTone(FilterSearch);
        FilterMinimum.TextChanged += (_, _) => ShowTone(FilterMinimum);
        FilterMaximum.TextChanged += (_, _) => ShowTone(FilterMaximum);
        FilterFrom.SelectedDateChanged += (_, _) => ShowTone(FilterFrom);
        FilterTo.SelectedDateChanged += (_, _) => ShowTone(FilterTo);
        FilterClearSearch.Click += (_, _) =>
        {
            FilterSearch.Text = string.Empty;
            Commit();
        };
        FilterClearAll.Click += (_, _) => ClearAllRequested?.Invoke();
        ShowTone(FilterSearch);
        ShowTone(FilterMinimum);
        ShowTone(FilterMaximum);
        ShowTone(FilterFrom);
        ShowTone(FilterTo);
    }

    /// <summary>Raised when a period button is pressed.</summary>
    public event Action<OverviewPeriod>? PeriodSelected;

    /// <summary>Raised when a transaction type was selected, an input was left, the search was confirmed with Enter or a calendar closed; read the inputs with <see cref="Read"/>.</summary>
    public event Action? ValuesCommitted;

    /// <summary>Raised when the clear-all button is pressed.</summary>
    public event Action? ClearAllRequested;

    /// <summary>Names the buttons, placeholders and tooltips in the current language.</summary>
    public void Localize()
    {
        foreach (var (_, button, titleKey) in periodButtons)
        {
            button.Content = Get(titleKey);
        }

        var selectedKind = (FilterKind.SelectedItem as Choice<TransactionKind?>)?.Value;
        var wasUpdating = updating;
        updating = true;
        try
        {
            FilterKind.ItemsSource = new Choice<TransactionKind?>[]
            {
                new(null, Get("All")),
                new(TransactionKind.Expense, Get("Expenses")),
                new(TransactionKind.Income, Get("Income")),
                new(TransactionKind.Transfer, Get("Transfer")),
            };
            FilterKind.SelectedItem = FilterKind.Items.Cast<Choice<TransactionKind?>>().Single(choice => choice.Value == selectedKind);
            Avalonia.Automation.AutomationProperties.SetName(FilterKind, Get("Type"));
            ToolTip.SetTip(FilterKind, Get("Type"));
        }
        finally
        {
            updating = wasUpdating;
        }

        FilterFrom.PlaceholderText = Get("Select a date");
        FilterTo.PlaceholderText = Get("Select a date");
        FilterSearch.PlaceholderText = Get("Description");
        FilterMinimum.PlaceholderText = Get("Min amount");
        FilterMaximum.PlaceholderText = Get("Max amount");
        ToolTip.SetTip(FilterClearSearch, Get("Clear search"));
        ToolTip.SetTip(FilterClearAll, Get("Clear filters"));
    }

    /// <summary>Highlights the button of <paramref name="period"/>; a period without a button, such as a custom range, highlights none.</summary>
    public void ShowPeriod(OverviewPeriod period)
    {
        if (shownPeriod == period)
        {
            return;
        }

        foreach (var (buttonPeriod, button, _) in periodButtons)
        {
            if (buttonPeriod == period)
            {
                button.Background = Palette.SelectionBackground;
                button.Foreground = Palette.SelectionForeground;
                button.FontWeight = FontWeight.SemiBold;
            }
            else
            {
                button.ClearValue(BackgroundProperty);
                button.ClearValue(ForegroundProperty);
                button.ClearValue(FontWeightProperty);
            }
        }

        shownPeriod = period;
    }

    /// <summary>Makes the next <see cref="ShowPeriod"/> restyle the buttons even for the period already shown.</summary>
    public void ForgetShownPeriod() => shownPeriod = null;

    /// <summary>Puts the transaction type, search text and the amount range of <paramref name="filter"/> into the inputs, without raising <see cref="ValuesCommitted"/>.</summary>
    public void ShowInputs(TransactionsFilter filter)
    {
        updating = true;
        try
        {
            FilterKind.SelectedItem = FilterKind.Items.Cast<Choice<TransactionKind?>>().Single(choice => choice.Value == filter.Kind);
            FilterSearch.Text = filter.Description ?? "";
            FilterMinimum.Text = filter.Minimum?.Francs.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) ?? "";
            FilterMaximum.Text = filter.Maximum?.Francs.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) ?? "";
        }
        finally
        {
            updating = false;
        }
    }

    /// <summary>Puts the date range into the date pickers, without raising <see cref="ValuesCommitted"/>.</summary>
    public void ShowDates(DateOnly? from, DateOnly? to)
    {
        updating = true;
        try
        {
            FilterFrom.SelectedDate = from?.ToDateTime(TimeOnly.MinValue);
            FilterTo.SelectedDate = to?.ToDateTime(TimeOnly.MinValue);
        }
        finally
        {
            updating = false;
        }
    }

    /// <summary>Reads the inputs.</summary>
    /// <exception cref="FormatException">An amount is not a number.</exception>
    internal FilterRowValues Read() => new(
        FilterSearch.Text,
        FilterFrom.SelectedDate is not null ? ParseDate(FilterFrom) : null,
        FilterTo.SelectedDate is not null ? ParseDate(FilterTo) : null,
        OptionalMoney(FilterMinimum),
        OptionalMoney(FilterMaximum),
        (FilterKind.SelectedItem as Choice<TransactionKind?>)?.Value);

    private void Commit()
    {
        if (!updating)
        {
            ValuesCommitted?.Invoke();
        }
    }

    // CalendarClosed can precede the control's final SelectedDate property update on the first click, so commit after that UI event turn.
    private void CommitAfterCalendarClosed() => Dispatcher.UIThread.Post(Commit);

    private static void ShowTone(TextBox input) => input.Foreground = ToneBrush(!string.IsNullOrWhiteSpace(input.Text));

    private static void ShowTone(CalendarDatePicker input) => input.Foreground = ToneBrush(input.SelectedDate is not null);

    private static IBrush ToneBrush(bool hasValue) => hasValue ? Palette.Text : Palette.TextMuted;
}