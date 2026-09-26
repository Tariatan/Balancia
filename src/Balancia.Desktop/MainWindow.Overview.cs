using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Balancia.Core;
using Balancia.Storage;

namespace Balancia.Desktop;

public partial class MainWindow
{
    private enum OverviewPeriod
    {
        All, Day, ThisWeek, ThisMonth, ThisYear, Custom
    }
    private const int HistoryPageSize = 100;
    private HistoryPage? overviewHistory;
    private int overviewOffset;
    private HistoryFilter overviewFilter = new();
    private bool overviewFiltersVisible;
    private bool pendingOverviewFilterRefresh;
    private OverviewPeriod overviewPeriod = OverviewPeriod.ThisMonth;
    private DateOnly? customFrom;
    private DateOnly? customTo;
    private Grid? overviewLayout;
    private Border? overviewFilterCard;
    private readonly Dictionary<OverviewPeriod, Button> overviewPeriodButtons = [];
    private (OverviewPeriod Period, bool FiltersVisible)? renderedPeriodState;
    private TextBlock? overviewSummaryLabel;
    private TextBlock? overviewStatus;
    private TextBlock? overviewIncomeValue;
    private TextBlock? overviewIncomeScope;
    private TextBlock? overviewExpensesValue;
    private TextBlock? overviewExpensesScope;
    private TextBlock? overviewAverageIncomeValue;
    private TextBlock? overviewAverageIncomeScope;
    private TextBlock? overviewAverageExpensesValue;
    private TextBlock? overviewAverageExpensesScope;
    private StackPanel? overviewCategoryBody;
    private TextBlock? overviewCategoryHeading;
    private IReadOnlyList<CategoryTotal> renderedOverviewCategories = [];
    private string? renderedOverviewCategoryId;
    private TextBlock? overviewHistoryHeading;
    private ListBox? overviewHistoryList;
    private TextBlock? overviewHistoryEmpty;
    private Button? overviewPreviousPage;
    private Button? overviewNextPage;
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

    private void RenderOverview(LedgerSnapshot ledgerSnapshot)
    {
        var label = OverviewScopeLabel();
        overviewFilterCard = null;
        overviewFilterFrom = null;
        overviewFilterTo = null;
        renderedPeriodState = null;
        var layout = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,Auto,*"),
            RowSpacing = 11
        };
        var overviewHeader = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto")
        };
        overviewSummaryLabel = QuietText($"{label}", 12);
        overviewSummaryLabel.Foreground = Brush.Parse("#FFB95D4D");
        overviewSummaryLabel.VerticalAlignment = VerticalAlignment.Center;
        AddColumn(overviewHeader, overviewSummaryLabel, 0);
        overviewStatus = QuietText(Status.Text ?? string.Empty, 11);
        overviewStatus.HorizontalAlignment = HorizontalAlignment.Right;
        overviewStatus.VerticalAlignment = VerticalAlignment.Center;
        overviewStatus.Margin = new Thickness(0, 0, 8, 0);
        AddColumn(overviewHeader, overviewStatus, 1);
        var settings = IconButton("⚙", "Open settings", OpenSettingsDialog);
        settings.VerticalAlignment = VerticalAlignment.Center;
        AddColumn(overviewHeader, settings, 2);
        AddRow(layout, overviewHeader, 0);
        overviewFiltersVisible = true;
        overviewFilterCard = OverviewFilterPanel();
        AddRow(layout, overviewFilterCard, 1);

        var accountRows = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*,Auto"),
            RowSpacing = 5
        };
        var accountHeader = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            Margin = new Thickness(0, 0, 0, 4)
        };
        accountHeader.Children.Add(Heading("Accounts", 11));
        var accountActions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 3
        };
        accountFilterChecks.Clear();
        var accounts = new ListBox
        {
            Classes = { "compact-list" },
            ItemsSource = ledgerSnapshot.Accounts.Select(a => new Choice<Account>(a, a.Name)).ToArray(),
            MinHeight = ledgerSnapshot.Accounts.Count == 0 ? 0 : 45,
            MaxHeight = 170,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            ItemTemplate = new FuncDataTemplate<Choice<Account>>((choice, _) =>
            {
                if (choice is null)
                {
                    return null;
                }

                var check = new CheckBox
                {
                    IsChecked = overviewFilter.AccountId == choice.Value.Id || overviewFilter.AccountIds?.Contains(choice.Value.Id) == true,
                };
                AutomationProperties.SetName(check, choice.Value.Name);
                accountFilterChecks[choice.Value.Id] = check;
                check.IsCheckedChanged += async (_, _) =>
                {
                    if (!updatingFilterControls && check.FindAncestorOfType<ListBox>() is { } list)
                    {
                        list.SelectedItem = choice;
                    }

                    await ToggleAccountFilter(choice.Value.Id, check.IsChecked == true);
                };
                var row = new Grid
                {
                    ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
                    ColumnSpacing = 5,
                    MinHeight = 18,
                    Margin = new Thickness(0, 5)
                };
                row.Children.Add(check);
                AddColumn(row, new TextBlock
                {
                    Text = choice.Value.Name,
                    FontSize = 12,
                    VerticalAlignment = VerticalAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis
                }, 1);
                AddColumn(row, new TextBlock
                {
                    Text = AmountText(choice.Value.Balance),
                    FontSize = 12,
                    FontWeight = FontWeight.SemiBold,
                    Foreground = BalanceColor(choice.Value.Balance),
                    VerticalAlignment = VerticalAlignment.Center
                }, 2);
                return row;
            })
        };
        accounts.DoubleTapped += async (_, _) =>
        {
            if (accounts.SelectedItem is Choice<Account> selected)
            {
                await EditAccount(selected.Value);
            }
        };
        var addAccount = ActionButton("+", () => EditAccount(null));
        addAccount.Width = 30;
        addAccount.Padding = new Thickness(0);
        addAccount.FontSize = 18;
        addAccount.HorizontalContentAlignment = HorizontalAlignment.Center;

        var removeAccount = ActionButton("🗑", () => DeleteSelectedAccount(accounts));
        removeAccount.Width = 30;
        removeAccount.Padding = new Thickness(0);
        removeAccount.FontSize = 18;
        removeAccount.HorizontalContentAlignment = HorizontalAlignment.Center;

        var archiveAccount = ActionButton("▣", () => ArchiveSelectedAccount(accounts));
        archiveAccount.Width = 30;
        archiveAccount.Padding = new Thickness(0);
        archiveAccount.FontSize = 18;
        archiveAccount.HorizontalContentAlignment = HorizontalAlignment.Center;

        ToolTip.SetTip(addAccount, "Add account");
        ToolTip.SetTip(archiveAccount, "Archive selected account");
        ToolTip.SetTip(removeAccount, "Delete selected account");
        accountActions.Children.Add(addAccount);
        accountActions.Children.Add(archiveAccount);
        accountActions.Children.Add(removeAccount);
        AddColumn(accountHeader, accountActions, 1);
        AddRow(accountRows, accountHeader, 0);

        if (ledgerSnapshot.Accounts.Count == 0)
        {
            var emptyAccounts = QuietText("No accounts yet", 12);
            emptyAccounts.VerticalAlignment = VerticalAlignment.Center;
            AddRow(accountRows, emptyAccounts, 1);
        }
        else
        {
            AddRow(accountRows, accounts, 1);
        }

        var total = TwoColumn("Total net worth", AmountText(ledgerSnapshot.NetWorth), 16,
            BalanceColor(ledgerSnapshot.NetWorth));
        total.Margin = new Thickness(0, 10, 0, 0);
        AddRow(accountRows, total, 2);
        var accountPanel = Panel(accountRows);
        accountPanel.Padding = new Thickness(10, 12, 10, 8);
        var (incomeBorder, incomeValue, incomeScope) = Metric("Income", ledgerSnapshot.MonthlyIncome, label, "#2C8B6D");
        var (expensesBorder, expensesValue, expensesScope) = Metric("Expenses", ledgerSnapshot.MonthlyExpenses, label, "#B95D4D");
        overviewIncomeValue = incomeValue;
        overviewIncomeScope = incomeScope;
        overviewExpensesValue = expensesValue;
        overviewExpensesScope = expensesScope;
        var averageScope = OverviewAverageScopeLabel();
        var (averageIncomeBorder, averageIncomeValue, averageIncomeScope) =
            Metric("Average", overviewAverages.Income, averageScope, "#2C8B6D");
        var (averageExpensesBorder, averageExpensesValue, averageExpensesScope) =
            Metric("Average", overviewAverages.Expenses, averageScope, "#B95D4D");
        overviewAverageIncomeValue = averageIncomeValue;
        overviewAverageIncomeScope = averageIncomeScope;
        overviewAverageExpensesValue = averageExpensesValue;
        overviewAverageExpensesScope = averageExpensesScope;
        var dashboard = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("0.6*,0.4*,0.4*,1.45*,1.45*"),
            RowDefinitions = new RowDefinitions("Auto,*"),
            RowSpacing = 11,
            ColumnSpacing = 11
        };
        AddColumn(dashboard, accountPanel, 0);
        var incomeExpenses = new Grid
        {
            RowDefinitions = new RowDefinitions("*,*"),
            RowSpacing = 6
        };
        AddRow(incomeExpenses, incomeBorder, 0);
        AddRow(incomeExpenses, expensesBorder, 1);
        AddColumn(dashboard, incomeExpenses, 1);
        var averages = new Grid
        {
            RowDefinitions = new RowDefinitions("*,*"),
            RowSpacing = 6
        };
        AddRow(averages, averageIncomeBorder, 0);
        AddRow(averages, averageExpensesBorder, 1);
        AddColumn(dashboard, averages, 2);
        AddColumn(dashboard, TrendPanel(), 3);
        AddColumn(dashboard, TimelinePanel(), 4);

        var categoryPanel = CategoryManagementPanel(ledgerSnapshot);
        var historyPanel = HistoryPanel();
        var rightColumn = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*"),
            RowSpacing = 11
        };
        AddRow(rightColumn, CategoriesPanel(ledgerSnapshot), 0);
        AddRow(rightColumn, RemindersPanel(), 1);
        var historyAndRightPanels = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("2.4*,1*"),
            ColumnSpacing = 11
        };
        AddColumn(historyAndRightPanels, historyPanel, 0);
        AddColumn(historyAndRightPanels, rightColumn, 1);
        var lowerPanels = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("0.72*,3.58*"),
            ColumnSpacing = 11
        };
        AddColumn(lowerPanels, categoryPanel, 0);
        AddColumn(lowerPanels, historyAndRightPanels, 1);
        AddRow(dashboard, lowerPanels, 1);
        Grid.SetColumnSpan(lowerPanels, 5);
        AddRow(layout, dashboard, 2);
        overviewLayout = layout;
        ResponsiveBody.Content = layout;
        UpdateAnalyticsCharts();
    }

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

    private void UpdateOverviewInPlace()
    {
        if (snapshot is not { } ledgerSnapshot)
        {
            return;
        }

        var label = OverviewScopeLabel();
        overviewSummaryLabel!.Text = $"{label}";
        overviewIncomeValue!.Text = AmountText(ledgerSnapshot.MonthlyIncome);
        overviewIncomeScope!.Text = label;
        overviewExpensesValue!.Text = AmountText(ledgerSnapshot.MonthlyExpenses);
        overviewExpensesScope!.Text = label;
        overviewAverageIncomeValue!.Text = AmountText(overviewAverages.Income);
        overviewAverageIncomeScope!.Text = OverviewAverageScopeLabel();
        overviewAverageExpensesValue!.Text = AmountText(overviewAverages.Expenses);
        overviewAverageExpensesScope!.Text = OverviewAverageScopeLabel();
        UpdateAnalyticsCharts();
        UpdateOverviewPeriodButtons();
        if (renderedOverviewCategoryId != overviewFilter.CategoryId ||
            !renderedOverviewCategories.SequenceEqual(ledgerSnapshot.LargestCategories))
        {
            FillCategoriesPanel(overviewCategoryBody!, ledgerSnapshot);
        }

        var total = overviewHistory?.TotalCount ?? 0;
        var first = total == 0 ? 0 : overviewOffset + 1;
        var last = overviewOffset + (overviewHistory?.Hits.Count ?? 0);
        overviewHistoryHeading!.Text = $"Transactions · {first:N0}-{last:N0} / {total:N0}";
        overviewHistoryEmpty!.IsVisible = total == 0;
        var currentItems = overviewHistoryList!.ItemsSource?.OfType<HistoryItem>().ToArray() ?? [];
        var nextItems = overviewHistory?.Hits.Select(hit => new HistoryItem(hit)).ToArray() ?? [];
        if (!currentItems.SequenceEqual(nextItems))
        {
            var selectedId = (overviewHistoryList.SelectedItem as HistoryItem)?.Hit.Entry.Id;
            overviewHistoryList.ItemsSource = nextItems;
            overviewHistoryList.SelectedItem = nextItems.FirstOrDefault(item => item.Hit.Entry.Id == selectedId);
        }
        overviewPreviousPage!.IsEnabled = overviewOffset > 0;
        overviewNextPage!.IsEnabled = overviewHistory is { } currentPage &&
            overviewOffset + currentPage.Hits.Count < currentPage.TotalCount;
    }

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

    private static (Border Border, TextBlock Value, TextBlock Scope) Metric(string title, Money value, string scope, string valueColor)
    {
        var valueText = new TextBlock
        {
            Text = AmountText(value),
            FontSize = 23,
            FontWeight = FontWeight.SemiBold,
            Foreground = Brush.Parse(valueColor)
        };
        var scopeText = QuietText(scope, 11);
        var titleText = Heading(title, 11);
        titleText.Margin = new Thickness(0, 0, 0, 2);
        valueText.VerticalAlignment = VerticalAlignment.Center;
        var content = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*,Auto")
        };
        AddRow(content, titleText, 0);
        AddRow(content, valueText, 1);
        AddRow(content, scopeText, 2);
        var border = Panel(content);
        border.Padding = new Thickness(15, 6);
        return (border, valueText, scopeText);
    }

    private Border HistoryPanel()
    {
        var body = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto"),
            RowSpacing = 2
        };
        var historyList = HistoryList(overviewHistory?.Hits ?? []);
        overviewHistoryList = historyList;
        var header = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            Margin = new Thickness(0, 0, 0, 10)
        };
        var total = overviewHistory?.TotalCount ?? 0;
        var first = total == 0 ? 0 : overviewOffset + 1;
        var last = overviewOffset + (overviewHistory?.Hits.Count ?? 0);
        overviewHistoryHeading = Heading($"Transactions · {first:N0}-{last:N0} / {total:N0}", 13);
        header.Children.Add(overviewHistoryHeading);
        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 3
        };
        var add = ActionButton("+", () => EditTransaction(null));
        add.Width = 30;
        add.Padding = new Thickness(0);
        add.FontSize = 18;
        add.HorizontalContentAlignment = HorizontalAlignment.Center;

        var remove = ActionButton("🗑", async () =>
        {
            if (historyList.SelectedItem is HistoryItem item)
            {
                await RemoveTransaction(item.Hit.Entry);
            }
        });
        remove.Width = 30;
        remove.Padding = new Thickness(0);
        remove.FontSize = 18;
        remove.HorizontalContentAlignment = HorizontalAlignment.Center;

        ToolTip.SetTip(add, "Add transaction");
        ToolTip.SetTip(remove, "Delete selected transaction");
        actions.Children.Add(add);
        actions.Children.Add(remove);
        AddColumn(header, actions, 1);
        AddRow(body, header, 0);
        AddRow(body, HistoryRow("Date", "Description", "Category", "Account", "Amount", true), 1);

        var historyContent = new Grid();
        overviewHistoryEmpty = QuietText("No matching transactions.", 13);
        overviewHistoryEmpty.IsVisible = total == 0;
        overviewHistoryEmpty.VerticalAlignment = VerticalAlignment.Top;
        overviewHistoryEmpty.Margin = new Thickness(0, 6, 0, 0);
        historyContent.Children.Add(overviewHistoryEmpty);
        historyList.DoubleTapped += async (_, _) =>
        {
            if (historyList.SelectedItem is HistoryItem item)
            {
                await EditTransaction(item.Hit.Entry);
            }
        };
        historyContent.Children.Add(historyList);
        AddRow(body, historyContent, 2);

        var previous = ActionButton("Previous page", async () =>
        {
            overviewOffset = Math.Max(0, overviewOffset - HistoryPageSize);
            await RequestOverviewFilterRefresh();
        });
        overviewPreviousPage = previous;
        previous.IsEnabled = overviewOffset > 0;
        var next = ActionButton("Next page", async () =>
        {
            overviewOffset += HistoryPageSize;
            await RequestOverviewFilterRefresh();
        });
        overviewNextPage = next;
        next.IsEnabled = overviewHistory is { } currentPage && overviewOffset + currentPage.Hits.Count < currentPage.TotalCount;
        AddRow(body, Row(previous, next), 3);

        var panel = Panel(body);
        panel.Padding = new Thickness(15, 15, 15, 5);
        return panel;
    }
}
